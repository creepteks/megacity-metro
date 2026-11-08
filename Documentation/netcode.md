# Netcode Reference

Everything about how Megacity Metro uses Netcode for Entities 1.14: how worlds are created, how a connection
becomes a flying ship, what is replicated and how, how prediction and lag compensation are configured, how the
Multiplayer Services SDK is wired in, and how to debug and extend it. Read
[architecture.md](architecture.md) first for the world/scene layout.

---

## 1. Design summary

| Aspect | Choice |
|---|---|
| Authority | Server-authoritative. Clients send `PlayerVehicleInput` commands; the server simulates and replicates. |
| Player ghost | `VehicleControlMultiplayer.prefab`, **Owner Predicted** (owner predicts, everyone else interpolates). |
| Movement | `MoveJob` runs identically in server simulation and owner prediction inside `PredictedFixedStepSimulationSystemGroup` (30 Hz). |
| Shooting | Hitscan auto-aim laser, predicted for feedback, **damage applied server-only**, target selection **lag compensated** with `PhysicsWorldHistory`. |
| Scale | 150 players (`NetCodeBootstrap.MaxPlayerCount`), distance-based importance scaling, position-only transform replication. |
| Rates | Simulation tick 30, network tick 30, predicted fixed step ratio 1 (`RateSettings`). |
| Services | Unity Multiplayer Services SDK sessions (matchmaking queue `default`, session browser), Multiplay hosting, Vivox. |
| Transport | UDP via Unity Transport; send/receive queue capacity raised to `150 * 5` (see §5 for where this actually applies). |

---

## 2. Project settings that affect netcode

| Setting | Location | Value | Meaning |
|---|---|---|---|
| NetCode Client Target | `ProjectSettings/NetCodeClientSettings.asset` → `ClientTarget` | `0` = Client **and** Server | Editor runs both worlds. Set to Client (1) before building a client (see `multiplayer-setup.md`). |
| Server excluded baking assemblies | `ProjectSettings/NetCodeServerSettings.asset` | 3 assemblies | Client/UI bakers excluded from server builds. |
| Global NetCode config | `ProjectSettings/NetCodeClientAndServerSettings.asset` → `GlobalNetCodeConfig` | none | Tick rate and ghost config come from code (`ServerInGame`) and prefabs. |
| Importance suggestions | same asset | Low 1–4, Medium 5–40, High 50–250, Critical 1000 | Editor hints only. The player prefab uses 10. |
| Lag compensation | `Assets/Scenes/Main/Common.unity` → `NetCodePhysicsConfig` | `EnableLagCompensation 1`, history sizes 0 (package defaults), `ClientNonGhostWorldIndex 1` | Creates `PhysicsWorldHistorySingleton` on server and client. |
| Fixed timestep | `ProjectSettings/TimeManager.asset` | 0.0333 | Matches `RateSettings.tickRate = 30`. |
| PlayMode tools | `Assets/Settings/PlayMode/Two Players.asset` | Multiplayer Play Mode scenario (one extra instance, server CLI port 7777) | Editor multi-instance testing. |

---

## 3. Bootstrap and world creation

`NetCodeBootstrap : ClientServerBootstrap` (`Gameplay/Mix/Multiplayer/Netcode/NetcodeBootstrap.cs`) is the only
bootstrap in the project. `Initialize` runs once at startup, before the first scene's `Awake`.

```
Initialize(defaultWorldName)
 ├─ PlayModeContext.CheckCompatibility()   // logs an error if the editor PlayMode Type contradicts the build target
 ├─ if PlayModeContext.IsServer (Linux/Windows/macOS Server platform)
 │    ├─ if active scene is "Menu": SceneController.LoadGame()
 │    ├─ CreateDefaultClientServerWorlds()  // server world (RequestedPlayType decides)
 │    └─ TryStartUgs()  -> new GameObject("MultiplayerServices").AddComponent<ServerBehaviour>()   [UNITY_SERVER]
 └─ else (client or editor)
      ├─ ServerConnectionUtils.SetDefaultWorld(isLocal: true)   // dispose Game worlds, create "LocalWorld", AutoConnectPort = 0
      ├─ if editor: EditorWorldCreation()
      │    ├─ IsEditorConnectedFromTheMainScene(): if the open scene is a game scene ("Main"/"ThinClientTest")
      │    │     read Resources/MultiplayerServerSettings.AutoRunGameModeInEditorMain:
      │    │       Multiplayer or None -> AutoConnectPort = 7979; CreateDefaultClientServerWorlds()
      │    │       SinglePlayer        -> CreateLocalWorld(defaultWorldName)
      │    └─ else if PlayMode Type == Client and PlayMode Tools has a valid address: AutoConnectPort/DefaultConnectAddress from it
      └─ else (player build): TryCreateThinClientsIfRequested()  // --enable-thin-clients N
```

Key helpers:

- `ServerConnectionUtils` (`Mix/Multiplayer/Netcode/ServerConnectionUtils.cs`)
  - `RequestConnection(ip, port)`: parses the endpoint and, by `RequestedPlayType`:
    `ClientAndServer` + loopback → `StartClientServer`; `Server` → `StartServer`; otherwise `ConnectToServer`.
    Every path first calls `DestroyActiveSimulationWorld()` (disposes all non-thin `WorldFlags.Game` worlds),
    creates the new world(s), sets `World.DefaultGameObjectInjectionWorld`, calls `SceneController.LoadGame()`,
    then `Listen`/`Connect` on `NetworkStreamDriver`.
  - `SetDefaultWorld(isLocal)`: the menu's world (`LocalWorld`) or a bare `ClientWorld`.
  - `TryParseDomain`: DNS-resolves `http(s)://host` style strings typed into the IP field.
- `PlayModeContext` (`Mix/Multiplayer/SDK/Utils/PlayModeContext.cs`): platform-based `IsEditor` / `IsServer`
  helpers and the compatibility check. It `using UnityEditor` at the top of a runtime file; it compiles because the
  Mix assembly is included in the editor, but keep that in mind when moving it.
- `CommandLineConfig`: static constructor parses args once (see architecture.md §8). On `UNITY_SERVER` it builds
  `MultiplayEndpoint` = `0.0.0.0:<-port>` (fallback `127.0.0.1:7979`).
- `SetRpcSystemDynamicAssemblyListSystem`: sets `RpcCollection.DynamicAssemblyList = true` in all netcode worlds so
  client and server can be built from different assembly subsets. Required because the server build strips assemblies.

Worlds created by each entry point:

| Entry | Worlds |
|---|---|
| Dedicated server build | `ServerWorld` (listens in `ServerInGame.OnCreate` on `MultiplayEndpoint.Port`) |
| Player build, menu | `LocalWorld` |
| Player build, single player | `LocalWorld` continues into `Main` |
| Player build, join/matchmake | `ClientWorld` (twice, see §13) |
| Editor on `Menu` | `LocalWorld`, then per the menu like a player |
| Editor opened on `Main`, PlayMode `Client & Server` | `ServerWorld` + `ClientWorld` auto-connected on 7979 |
| Editor opened on `Main`, `AutoRunGameModeInEditorMain = SinglePlayer` | `LocalWorld` |
| `--enable-thin-clients N` | N × `ThinClientWorld`, following the main client's connection or auto-matchmaking when headless |

---

## 4. Connection lifecycle

### 4.1 Server side (`ServerInGame`, `Gameplay/Server/Netcode/ServerInGame.cs`)

`OnCreate` (runs once per server world):

1. Creates an entity with a `PlayerConnectedElement` buffer (the server's roster).
2. Requires `PlayerSpawner` and `SpawnPointElement` (both baked in subscenes), so the system is idle until `Main` streams in.
3. On `UNITY_SERVER && !UNITY_EDITOR`: `NetworkStreamDriver.Listen(AnyIpv4:<port>)`.
4. Creates the **importance grid**: `GhostDistanceData { TileSize = (256, 8192, 256), TileCenter = 0, TileBorderWidth = 5 }`
   and `GhostImportance` with `GhostDistanceImportance.ScaleFunctionPointer`, keyed on `GhostConnectionPosition`.
5. Creates `ClientServerTickRate` with `SimulationTickRate = NetworkTickRate = 30`, `PredictedFixedStepSimulationTickRatio = 1`.
6. `RateSettings.ApplyFrameRate()`.

`OnUpdate` for every connection entity that has `NetworkId` but no `NetworkStreamInGame`:

1. Skip if that `NetworkId` is already in the roster (defensive).
2. `GetPositionJob` picks a random unused spawn point (cycle resets when all are used).
3. Through the `BeginSimulationEntityCommandBufferSystem` buffer:
   `AddComponent<NetworkStreamInGame>(connection)`, `Instantiate(PlayerSpawner.Player)`, set `GhostOwner.NetworkId`,
   set `LocalTransform.Position`, copy `VehicleHealth` from the prefab, **append the player to the connection's
   `LinkedEntityGroup`** (so disconnect destroys the ship), `AddComponent<GhostConnectionPosition>(connection)`,
   set `CommandTarget.targetEntity = player`.
4. `UpdateConnectionPositionSystemJob` copies each ship's position into its connection's `GhostConnectionPosition`
   every update, which drives distance importance.

### 4.2 Client side

| System | File | What it does |
|---|---|---|
| `ClientInGame` (client + thin, Initialization) | `Client/Netcode/ClientInGame.cs` | Registers `MegacityMetroSmoothingAction` for `LocalTransform` on `GhostPredictionSmoothing`; adds `NetworkStreamInGame` + `ConnectionState` to every `NetworkId` connection; creates a `GhostSendSystemData { MinSendImportance = 2 }` singleton (client-side, inert, see known issues); applies frame rate. |
| `SendPlayerInfoSystem` (client + thin) | `Client/Player/SetupPlayerInfoSystem.cs` | Once in game, sends the RPC `SetPlayerInfoRequest { Name, UASId, IsClient = !IsThinClient, Platform }` and tags the connection with `NameAssigned`. |
| `SetPlayerInfoSystem` (server) | `Server/Player/SetPlayerInfoSystem.cs` | Consumes the RPC: finds the ghost whose `GhostOwner.NetworkId` matches the sender, sets `PlayerName` (thin clients get a name from the baked `BotNameElement` bank, real clients get their typed name or a random one), `PlayerPlatform`, adds `PlayerUASID`, appends to `PlayerConnectedElement`, destroys the request. |
| `MatchMakingMainMenuSystem` (client) | `Client/UI/Systems/MainMenu/MatchMakingMainMenuSystem.cs` | If `MatchMakingConnector.IsTryingToConnect` and the world has no `NetworkStreamConnection`, connects `NetworkStreamDriver` to `IP:Port` manually; reports success/failure to `MainMenu`. |
| `CheckServerConnectionTimeoutSystem` (client) | `Client/Netcode/CheckServerConnectionTimeoutSystem.cs` | While `ConnectionState == Connecting`, counts up to `MultiplayerServerSettings.ConnectionTimeout` (20 s) then shows a modal and disconnects. |
| `CheckServerConnectionSystem` (client) | `Client/Netcode/ClientConnectionSystem.cs` | On `ConnectionState.State.Disconnected`: logs the reason, shows a modal, loads `Menu`, removes `ConnectionState`. |
| `QuitSystem` (client + local) | `Client/Netcode/QuitSystem.cs` | `DisconnectAllPlayers()` adds `NetworkStreamRequestDisconnect` to the connection and loads `Menu`; `WantsToQuit` exits play mode / the app. |

### 4.3 Disconnect and cleanup (server)

```
connection entity destroyed by Netcode
  -> LinkedEntityGroup destroys the player ghost
  -> VerifyPlayerDisconnectedSystem: roster entry whose Entity no longer Exists -> RemoveAt + create PlayerLeftEvent { PlayerId = UASId }
  -> PlayerLeftEventSystem [UNITY_SERVER]: ServerBehaviour.Instance.ServerManager.SessionEventManager.RemovePlayer(id) -> Session.AsHost().RemovePlayerAsync
  -> ConnectionMonitorSystem (needs EnableConnectionMonitor singleton, baked in Common.unity):
       adds ConnectionState to new connections, logs "[Server] New connection ID:x" / "Connection disconnected ID:x Reason:y",
       and broadcasts ConnectionStateInfoRequest RPCs (no client system consumes them today).
```

### 4.4 Components you will see on a connection entity

`NetworkStreamConnection`, `NetworkId`, `NetworkStreamInGame` (added by the project), `CommandTarget`
(→ player ghost), `GhostConnectionPosition` (server), `ConnectionState` (client and monitored server),
`NetworkSnapshotAck` (RTT source for the stats panel), `LinkedEntityGroup` (contains the ship),
`NameAssigned` (client), `InitializedConnection` (server monitor).

---

## 5. Transport and driver configuration

Two `INetworkStreamDriverConstructor` implementations exist:

| Class | Registered by | Used when |
|---|---|---|
| `CustomDriverConstructor` (`Mix/Multiplayer/SDK/Client/CustomDriverConstructor.cs`) | `CustomEntitiesNetworkHandler.SetupDriverConstructor()` sets `NetworkStreamReceiveSystem.DriverConstructor` when the Multiplayer Services SDK starts a session network | After a client has joined/matchmade a session in this process. Supports Direct and Relay, IPC for host, and raises server UDP queue capacity to `MaxPlayerCount * 5`. |
| `MegacityMetroDriverConstructor` (`Mix/Multiplayer/Netcode/MegacityMetroDriverConstructor.cs`) | **nothing** (referenced only in a doc comment) | Never. It was the original 150-player queue fix. |

Consequence: the dedicated server (which never runs the client SDK handler) uses Netcode's default driver
constructor and therefore the **default** send/receive queue capacity. If you see Unity Transport "error code 5"
style warnings under load, register the constructor in `NetCodeBootstrap.Initialize` before creating worlds:

```csharp
NetworkStreamReceiveSystem.DriverConstructor = new MegacityMetroDriverConstructor();
```

---

## 6. Tick, time and rate management

| Item | Where | Value |
|---|---|---|
| `tickRate` | `Utils/Misc/RateSettings.cs` | 30 |
| `fixedRateRatio` | same | 1 → predicted physics step = 1/30 s |
| `framerate` | same | -1 (uncapped); `UnlockFramerate` toggles vSync off and 120 fps on mobile |
| `ClientServerTickRate` | `ServerInGame.OnCreate` | sent to clients in the handshake |
| `FixedStepSimulationSystemGroup.Timestep` (single player) | `InitLocalRateSystem` | `1 / (30 * 1)` |

The client `SimulationSystemGroup` runs per frame; `PredictedSimulationSystemGroup` runs the ticks between the last
server tick and the current predicted tick (including partial ticks for non-fixed-step systems).
`PredictedFixedStepSimulationSystemGroup` only runs whole ticks, so `MoveJob`'s `dt` is always exactly 1/30.

---

## 7. Ghosts: what is replicated

### 7.1 Player prefab ghost configuration (`GhostAuthoringComponent` on `VehicleControlMultiplayer.prefab`)

| Field | Value | Meaning |
|---|---|---|
| `DefaultGhostMode` | 2 = **OwnerPredicted** | Owner predicts, others interpolate. |
| `SupportedGhostModes` | 3 = All | Can be switched to all-predicted or all-interpolated at runtime. |
| `OptimizationMode` | 0 = Dynamic | Every snapshot includes it when relevant. |
| `Importance` | 10 | Base priority (then scaled by distance). |
| `HasOwner` | true | `GhostOwner` present; enables `GhostOwnerIsLocal`. |
| `SupportAutoCommandTarget` | true | `AutoCommandTarget` added; input buffer is routed automatically. |
| `TrackInterpolationDelay` | true | Adds `CommandDataInterpolationDelay`, required by lag compensation in `LaserJob`. |
| `UsePreSerialization` | false | – |
| `GhostGroup` | false | – |

A `GhostAuthoringInspectionComponent` with no overrides is also present. The single-player prefab is a variant
with the same ghost configuration (inert in `LocalWorld`).

### 7.2 Replicated components and fields

| Component | Prefab type / send | Fields replicated | Notes |
|---|---|---|---|
| `LocalTransform` via `MegacityMetroLocalTransform` variant | All; `AllClients`; `SendDataForChildEntity = false` | `Position` q1000, `InterpolateAndExtrapolate`, `MaxSmoothingDistance = 20` | **Rotation and Scale are not sent.** Children (visuals) never replicate transform. |
| `PhysicsVelocity` via `MegacityMetroPhysicsVelocity` variant | All; `OnlyPredictedClients` | `Linear` q1000 | Only the owner receives velocity (needed for prediction rollback). Angular not sent. |
| `VehicleMovementState` | default (all) | `PitchRadians`, `YawRadians`, `RollRadians`, `BaseRoll`, `RollAccumulationFromTurn` (floats q1000, Interpolate), `LastLookVelocity` (float2 q1000, Interpolate) | Rotation is rebuilt from these on every client. |
| `PlayerVehicleInput : IInputComponentData` | `AllPredicted` | all fields as a command | Lives only on predicted instances (owner on client, everything on server). |
| `VehicleLaser` | default | `Energy` q1000, `IsShooting`, `DetectedTarget` (Entity) | Visual nodes and filters are not replicated. |
| `VehicleHealth` | default | `Value` q1000, `IsDead` (byte), `LookAtEnemyDegrees` | `_deathTimer`, `_DeathFXState` are local. |
| `Immunity` | default | `Counter` | `Duration` baked, `_immunityFXState` local. |
| `PlayerScore` | default | `Value` q1000, `Kills`, `KilledPlayer` (FixedString64), `KillerName` (FixedString64) | Strings cost bytes on change only (change mask). |
| `PlayerName` | default | `Name` (FixedString64) | Set by server from RPC. |
| `PlayerUASID` | default (added at runtime) | `UASId` (FixedString64) | Runtime-added components are **not** ghost-serialized unless on the prefab; treat as server-only data. |
| `PlayerPlatform` | default | `Value` | Shown in leaderboard icons. |
| `PlayerCameraTarget` | `PredictedClient` | none | Exists only on the owner's client instance. |

The variants are registered in `MegacityMetroVariants : DefaultVariantSystemBase` with `Rule.OnlyParents`, so child
entities keep Netcode's defaults (which, combined with `SendDataForChildEntity = false`, means children are static
relative to the root as far as the network is concerned).

### 7.3 Why rotation is three floats

`LocalTransform.Rotation` is excluded from replication; instead the controller's own state (pitch/yaw/roll in
radians) is sent and both `ReconstructVehicleRotationSystem` (owner) and `InterpolatedVehicleRotationSystem`
(remote ghosts, `[WithDisabled(GhostOwnerIsLocal)]`, before `TransformSystemGroup`) rebuild the quaternion. This
keeps the replicated state exactly what the simulation integrates (no quaternion drift during rollback) at the cost
of the wrap artefact described in [known-issues.md](known-issues.md).

---

## 8. Prediction and reconciliation

- Systems in `PredictedSimulationSystemGroup` (and the physics group inside it) run on the server and on clients for
  predicted ghosts. Every job uses `[WithAll(typeof(Simulate))]` so only entities Netcode wants simulated this tick
  are touched (this is how partial ticks and non-owned ghosts are excluded).
- Predicted on the owner: `MoveJob`, rotation systems, physics, `LaserJob` (energy, `IsShooting`,
  `DetectedTarget`), collision throttle cut.
- Server-only even though they live in gameplay groups: `VehicleHealthAndImmunitySystem`, `ShootingServerSystem`
  (`DamageJob`), `SetPlayerInfoSystem`, `VerifyPlayerDisconnectedSystem`. Health, death, score, immunity are
  therefore never predicted; the client sees them one RTT later.
- Rollback: when a snapshot arrives for the owner's ghost, Netcode restores all ghost fields to the server values
  and re-runs prediction from that tick to the current one with the stored input buffer. `VehicleMovementState`
  contains everything `MoveJob` needs (including `LastLookVelocity`), so the re-simulation is exact.
- **Smoothing** (`MegacityMetroSmoothingAction`, `Components/Gameplay/Player/MegacitySmoothing.cs`): after
  rollback, if the position error `d` satisfies `0.5 < d < 20`, the displayed position moves only 0.5 units toward
  the corrected one per frame (constant-speed slide, so 30 units/s at 60 fps). Errors ≤ 0.5 or ≥ 20 are applied
  instantly. Parameters are `SharedStatic` (`maxDist`, `delta`) and can be changed at runtime from managed code.
- Physics in prediction: `Unity.NetCode.Physics` moves `PhysicsSystemGroup` into
  `PredictedFixedStepSimulationSystemGroup`; predicted ghosts live in the predicted physics world, interpolated
  ghosts in the client's non-ghost world index 1 (`ClientNonGhostWorldIndex`).

---

## 9. Lag compensation (hitscan laser)

- Enabled by `NetCodePhysicsConfig { EnableLagCompensation = 1 }` in `Common.unity`, which makes
  `PhysicsWorldHistory` record collision worlds each tick (history length = package default since sizes are 0).
- `ShootingSystem` (`Mix/Shooting/ShootingSystem.cs`, `PredictedSimulationSystemGroup`) schedules `LaserJob` with
  `PredictingTick = NetworkTime.ServerTick` and each ship's `CommandDataInterpolationDelay`.
- `LaserJob` calls `CollisionHistory.GetCollisionWorldFromTick(PredictingTick, interpolationDelay.Delay, ...)` to
  obtain the collision world **as the shooter saw it** (server tick minus the shooter's interpolation delay), then
  runs a `SphereCastCustom` with the `AutoAimHitsCollector` (cone test, min radius, alive and non-immune targets,
  obstruction ray against `LaserObstructionsFilter`, ignores entities in `WeaponShotIgnoredEntity`). The chosen
  entity becomes `VehicleLaser.DetectedTarget`.
- On the server the same job produces the authoritative `DetectedTarget`; `DamageJob` (server, per frame at tick
  rate) applies `DamagePerSecond * dt` to it, updates kill/score/name fields and `LookAtEnemyDegrees`.
- On the client the job runs in prediction so the crosshair, laser beam end point and hit VFX are immediate.
- Requirements for new weapons: run in `PredictedSimulationSystemGroup`, read `CommandDataInterpolationDelay`,
  keep `TrackInterpolationDelay` on the prefab, and query through the history singleton rather than the live world.

---

## 10. Input and commands

- `PlayerVehicleInput : IInputComponentData` on an `AutoCommandTarget` ghost. Netcode copies the component into
  `InputBufferData<PlayerVehicleInput>` for the current tick in `GhostInputSystemGroup` and sends the last few
  ticks of input in every command packet.
- `PlayerVehicleInputSystemBase` (client, `GhostInputSystemGroup`) writes the component every frame for every
  entity that has `PlayerVehicleSettings` **and** `PlayerVehicleInput` in the client world. Today only the owner's
  predicted ship has `PlayerVehicleInput` (interpolated ghosts have it stripped by `AllPredicted`), so this is safe;
  if you switch remote ships to predicted mode this job would stomp their input.
- `AimAssistSensitivity` is sent as part of every command. It changes only when the settings UI changes it, so a
  ghost field or RPC would be cheaper.
- Thin clients (`Client/Netcode/MegacityMetroThinInputSystem.cs`): `AddThinClientInputSystem` creates a bare input
  entity with `GhostOwner`, `PlayerVehicleInput`, `InputBufferData` and sets `CommandTarget`;
  `MoveThinClientInputSystem` randomizes the input every 30 ticks (note the `NextInt() < 0.4f` comparisons, which are
  effectively 50/50, and the tiny `LookVelocity` range; see known issues).
- Cheat input (`Cheat_1`, key `1`) is only filled in dev builds and only acted on in the editor
  (`CheatShootingSystem`, server side: hurts yourself by 1 HP per predicted tick).

---

## 11. Relevancy, importance and bandwidth

- No relevancy set is used; every ghost is sent to every client, prioritized by importance.
- `GhostDistanceImportance` scales the prefab importance (10) by the chunk's grid distance to the connection's
  `GhostConnectionPosition`. Grid tiles are 256 × 8192 × 256 (effectively 2D over the city) with a 5-unit border
  to stop flicker at tile edges. Ships are partitioned into chunks by `GhostDistancePartitionShared`.
- Per-ghost payload: position (3 × q1000) + 7 floats of movement state + laser/health/score fields. Strings and
  entity references only cost when changed.
- `GhostSendSystemData { MinSendImportance = 2 }` is created in `ClientInGame` (client world) where
  `GhostSendSystem` does not run; to actually skip low-importance ghosts, create it in the server world
  (`ServerInGame.OnCreate`). Other knobs worth considering at 150 players: `DefaultSnapshotPacketSize`,
  `MaxSendChunks`, `MaxSendEntities`, `IrrelevantImportanceDownScale`.
- Bandwidth is bounded by the snapshot packet size, not by player count; far ships simply update less often.

---

## 12. Spawning and ownership

- Spawn points: `SpawnPointAuthoring` collects **all** `SpawnPoint` MonoBehaviours found at bake time
  (`FindObjectsByType`) into a `SpawnPointElement` buffer. Keep the spawn markers in the same subscene
  (`MegacityMetroLevelBounds.unity`) to avoid bake-order surprises.
- Multiplayer: `ServerInGame` (above). Respawn after death is **in place** (no teleport) via
  `HealthAndImmunityJob`: 5 s death timer, then HP 100 and `Immunity.Counter = Duration` (4.5 s on the prefab).
- Single player: `SpawnSinglePlayer` instantiates `PlayerSpawner.SinglePlayer` at the first spawn point and adds
  `SinglePlayer` and `GhostOwnerIsLocal` (so client-side systems treat it as the local ship).
- Ownership on the client: `GhostOwnerIsLocal` is the universal "my ship" filter (camera, HUD, bounds, Vivox,
  stats). The server identifies players by `GhostOwner.NetworkId` and the `PlayerConnectedElement` roster
  (name, UGS id, network id, entity).
- Visuals: `RandomShipVisualsSystem` picks one of the `ShipRandomVisuals` prefabs seeded by the player's name hash
  (so every client picks the same hull once the name has replicated) and parents it under the ship; it also copies
  the visual's `LocalLaserStartPoint` into `VehicleLaser` and `FXEntity` into `PlayerVehicleSettings.VehicleFX`.
  It runs in every world, including the server.

---

## 13. Unity Gaming Services integration

### 13.1 Client (menu scene)

```
MatchMakingConnector.Start  (Mix/UI/MonoBehaviours/Matchmaker/MatchMakingConnector.cs)
 ├─ requires Application.cloudProjectId (else modal)
 ├─ PlayerInfoItemSettings.PlayerName = BotNameGenerator.GetRandomName()
 ├─ PlayerAuthentication.SignIn(profileName)  -> UnityServices.InitializeAsync(profile hash) + SignInAnonymouslyAsync
 └─ ClientManager.Start(); ClientManager.LoginAsync(profile)   -> GameState.Menu

Find Match:   MatchMakingUI.Matchmake -> MatchMakingConnector.Matchmake
 ├─ ServerConnectionUtils.SetDefaultWorld()        // fresh "ClientWorld"
 ├─ ClientManager.MatchmakeAsync: MultiplayerService.MatchmakeSessionAsync(queue "default", SessionOptions{Type "MegacityMetroSession", MaxPlayers 150}.WithDirectNetwork(...).WithNetworkHandler(CustomEntitiesNetworkHandler))
 │     └─ SDK calls CustomEntitiesNetworkHandler.StartAsync(config): sets DriverConstructor, connects ClientServerBootstrap.ClientWorld to the allocated endpoint, waits ≤10 s for Connected
 ├─ IP/Port = ClientManager.ConnectionEndpoint
 └─ ConnectToServer(ip:port) -> ServerConnectionUtils.RequestConnection -> ConnectToServer:
       DestroyActiveSimulationWorld (disposes the world the SDK just connected), CreateClientWorld, LoadGame, Connect

Connect (session browser):  SessionBrowser -> MatchMakingConnector.JoinSession(id)
 ├─ SetDefaultWorld(); ClientManager.JoinSessionAsync(id) (JoinSessionOptions{Type "MegacityMetroSession"}, same handler)
 └─ ConnectToServer(endpoint.Address)  // same second-connection pattern
```

So the gameplay connection is always a **second** connection made by `ServerConnectionUtils`; the SDK-made
connection is torn down when its world is disposed. The server sees a connect/disconnect pair per joining client
(and spawns/destroys a ship for the first one). This works but is worth knowing when reading server logs or
refactoring: the clean fix is to keep the SDK-connected world and load `Main` into it instead of reconnecting.

Leaving: `MainMenu.Start` on return calls `SetDefaultWorld(true)` and `MatchMakingConnector.ClearSession()`
(`ClientManager.LeaveSessionAsync` → `Session.LeaveAsync` + `handler.StopAsync` which requests a disconnect).

`ClientMatchmaker` (`UGS/Client/ClientMatchmaker.cs`) is the older direct Matchmaker-service ticket flow
(create ticket, poll, retry on "maximum capacity reached"). It is not referenced by the current session-based flow.

### 13.2 Server (dedicated build)

```
NetCodeBootstrap.TryStartUgs -> ServerBehaviour (Mix/Multiplayer/SDK/Server/ServerBehaviour.cs)
 ├─ reads --api-key-id / --api-key-secret
 ├─ loads Resources/GameServiceData (GameServerInfo_Data: MaxPlayers 150, ServerName, GameType, Map, BuildID, ServerStartupBufferMS)
 └─ ServerManager.StartAsync
      ├─ UnityServices.InitializeAsync
      ├─ try ServerAuthenticationService.SignInFromServerAsync   // succeeds only on Multiplay
      ├─ Multiplay: MultiplayerServerService.StartMultiplaySessionManagerAsync(
      │       SessionOptions{Type "NFESession", MaxPlayers, properties}.WithDirectNetwork().WithBackfillingConfiguration(true,true,true,30,1),
      │       MultiplayServerOptions(name, gameType, Application.buildGUID, map), callbacks)
      │     callbacks: Allocated -> Session = SessionManager.Session (also feeds SessionEventManager); Deallocated; StateChanged; PlayerReadinessChanged
      │     SQP query responder starts immediately.
      └─ else with service-account keys: SignInWithServiceAccountAsync; MultiplayerServerService.CreateSessionAsync(...) (local testing; no backfill)
```

`SessionEventManager` subscribes to all `IServerSession` events and logs them with a `[SessionEventManager]`
prefix; `RemovePlayer(id)` is the only mutation (called from `PlayerLeftEventSystem`). `Dispatcher` marshals async
work back to the main thread in `Update`.

Note the session `Type` strings differ between client (`MegacityMetroSession`) and server (`NFESession`). The SDK
uses `Type` to pick the network handler/session kind; verify both sides when changing them.

### 13.3 Vivox

`PlayerInfoController.Start` instantiates the `VivoxManager` prefab in multiplayer; `InitializedVivoxSystem` logs in
with the replicated `PlayerName` once it is non-empty and tags the ship with `VivoxConnectionStatus`. Everything else
is MonoBehaviour-side (`VivoxSession`, `VivoxChannel`, `VivoxDevicesVolume`, `VivoxPanel`, `VivoxPlayerList`).

---

## 14. Debugging and tooling

| Tool | How |
|---|---|
| Netcode stats panel | Key `P` in dev builds / editor (`GameUI.prefab` PlayerInput → `HUD.ToggleNetcodePanel`). Shows FPS, player count, `EstimatedRTT ± DeviationRTT` (from `NetworkSnapshotAck`), systems/frame (`GlobalSystemVersion` delta), entity count, server ghost count (`GhostCount`), quality level. Systems in `Utils/NetcodeExtensions/Systems`. |
| Entities Hierarchy | Window → Entities → Hierarchy, pick the world. Check `PhysicsCustomTags`, `GhostOwnerIsLocal` (enabled state), `CommandTarget`, `ConnectionState`. |
| Multiplayer PlayMode Tools | Window → Multiplayer → PlayMode Tools: PlayMode Type (Client / Server / Client & Server), thin clients, simulated latency/jitter/loss. `NetCodeBootstrap` honours the address typed there for `Client` mode. |
| `ThinClientDebugDisplay` | IMGUI box listing thin-client worlds (green = has `NetworkId`) and matchmaking status when `--enable-thin-clients` is used. |
| Logs | Prefixes: `[CLI]` command line, `[Server]` connection monitor, `[Client]` disconnects, `[Matchmaker]`, `[ClientManager]`, `[ServerManager]`, `[SessionEventManager]`, `[TryCreateThinClientsIfRequested]`, `[DestroyActiveSimulationWorld]`, `[EditorSettings]`. |
| Cheat | Key `1` in the editor: self-damage, to test death/respawn/kill-feed without a second player. |
| `PlayModeContext.DebugInfo` | String with platform/editor/server flags; `CheckCompatibility` error explains missing worlds. |
| Unity Netcode debug | `NetDebug` singleton log level; Netcode's own "Netcode Stats"/"Ghost Stats" windows under Window → Multiplayer. |

Common failures:

| Symptom | Likely cause |
|---|---|
| No server/client world in editor | PlayMode Type contradicts build target (`PlayModeContext.CheckCompatibility` error); or `AutoRunGameModeInEditorMain` set to SinglePlayer while opening `Main`. |
| Stuck on "Connecting" then modal after 20 s | Wrong IP/port, server not listening, UDP blocked. `ConnectionTimeout` in `Resources/MultiplayerServerSettings`. |
| Player spawns but has no name / name tag | `SetPlayerInfoRequest` not processed: check `BotNameElement` buffer exists (Common subscene loaded on server) and the RPC arrived after the ghost was instantiated. |
| Thin clients vanish immediately in editor | `MultiplayerPlayModePreferences.RequestedNumThinClients` must match; `NetCodeBootstrap` clobbers it when the CLI arg is used. |
| Shooting does nothing in single player | Expected: `ShootingSystem` requires `NetworkTime`. |
| Ship visuals missing for a remote player | `RandomShipVisualsSystem` waits for a non-empty `PlayerName`. |
| Transport queue warnings under load on the server | Default driver constructor in use; see §5. |

---

## 15. Extension recipes

**Add a replicated field**: add `[GhostField]` to a component on the prefab (quantize floats, prefer `byte`/`int`),
bump nothing else; Netcode regenerates serializers. Runtime-added components are not serialized; put them on the
prefab and toggle with an enableable component if needed.

**Add an RPC**: `struct MyRequest : IRpcCommand { ... }`; sender creates an entity with `SendRpcCommandRequest`
(+ `TargetConnection` for a specific client) and the payload; receiver queries `MyRequest` + `ReceiveRpcCommandRequest`
and destroys the entity. Pattern: `SendPlayerInfoSystem` / `SetPlayerInfoSystem`.

**Add a predicted ability**: write an `IJobEntity` with `[WithAll(typeof(Simulate))]`, schedule it from a system in
`PredictedSimulationSystemGroup` (or `BeforePhysicsSystemGroup` if it touches velocity), read only commands and
ghost fields, store all state it needs next tick in ghost fields. Apply irreversible effects (damage, score) in a
`ServerSimulation`-filtered system.

**Add a new ghost type**: prefab with `GhostAuthoringComponent`, reference it from an authoring component in a
subscene (like `PlayerSpawnerAuthoring`) so it is baked into the ghost collection on both sides, instantiate on
the server only.

**Change tick rate**: edit `RateSettings.tickRate`, `TimeManager` fixed timestep, and revisit per-tick constants
(`RollAccumulationSpeed`, thin-client cadence `% 30`).

**Add relevancy** (for maps larger than the importance grid can handle): create `GhostRelevancy` with
`GhostRelevancyMode.SetIsIrrelevant` and fill the set per connection from the server's spatial data.

**Server-side validation**: inputs are already clamped on the client (`AccelerationRange`); add clamps in `MoveJob`
too if you want the server to be the source of truth for limits.

---

## 16. Netcode-specific pitfalls

Collected with reasoning in [known-issues.md](known-issues.md). Headlines: unused 150-player driver constructor;
client-side `GhostSendSystemData`; double connection through the SDK; session type mismatch; thin-client input
scale and probability bug; no consumer for `ConnectionStateInfoRequest`; `RandomShipVisualsSystem` running on the
server; `PlayerUASID` added at runtime and therefore never replicated.
