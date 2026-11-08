# Megacity Metro – Architecture Overview

This document is the entry point to the source-level documentation. It explains how the
project is put together at runtime so that the two deep dives make sense:

- [Flight Control Reference and Customization Plan](flight-control.md) – everything that shapes how the ship feels.
- [Netcode Reference](netcode.md) – worlds, connection lifecycle, ghosts, prediction, lag compensation, UGS.
- [Gameplay Systems](gameplay-systems.md) – combat, health, visuals, HUD, traffic, pooling, settings.
- [Source Reference](source-reference.md) – one line per script, grouped by folder.
- [Known Issues and Pitfalls](known-issues.md) – things found while reading the code that will bite you.

The existing docs ([assemblies.md](assemblies.md), [attributes.md](attributes.md),
[multiplayer-setup.md](multiplayer-setup.md), [ugs.md](ugs.md), [gsh.md](gsh.md)) still apply and are referenced
where relevant.

---

## 1. Technology stack

| Area | Package / version | Notes |
|---|---|---|
| Engine | Unity 6000.3.24f1 (6.3 LTS) | `ProjectSettings/ProjectVersion.txt` |
| ECS | `com.unity.entities` (pulled by `com.unity.entities.graphics` 1.4.21) | Burst-compiled `ISystem` everywhere hot |
| Netcode | `com.unity.netcode` 1.14.2 | Netcode for Entities, with `Unity.NetCode.Physics` |
| Transport | `com.unity.transport` 2.7.4 | UDP driver, queue sizes tuned for 150 players |
| Physics | `com.unity.physics` 1.4.7 | Unity Physics (not Havok), 1 solver iteration |
| Rendering | URP 17.3.0, Entities Graphics, VFX Graph | STP / Render Graph |
| Input | `com.unity.inputsystem` 1.20.0 | Generated `GameInput` class from `Assets/Settings/Input/GameInput.inputactions` |
| Services | Multiplayer Services SDK 2.3.1, Authentication 3.7.4, Vivox 16.11.0 | Sessions, matchmaking, voice |
| UI | UI Toolkit (runtime) + custom controls | `Assets/UI` |

---

## 2. Runtime topology: worlds

Netcode for Entities splits the simulation into separate ECS worlds. Which worlds exist depends on how the
game was started (see `NetCodeBootstrap`, documented in [netcode.md](netcode.md#3-bootstrap-and-world-creation)).

| World | Flag (`WorldSystemFilterFlags`) | When it exists | What runs in it |
|---|---|---|---|
| `LocalWorld` | `LocalSimulation` | Menu scene, and the **single-player** game | Vehicle movement, camera, FX, UI systems. No netcode systems. No shooting. |
| `ClientWorld` | `ClientSimulation` | Multiplayer client (also in the editor with "Client & Server") | Input sampling, prediction of the local ship, interpolation of remote ships, presentation. |
| `ServerWorld` | `ServerSimulation` | Dedicated server build, or in-process in the editor | Authoritative simulation, spawning, damage, score, UGS session management. |
| `ThinClientWorld-N` | `ThinClientSimulation` | When `--enable-thin-clients N` is passed or set in PlayMode Tools | Minimal client that only sends random commands. Used for load testing 150 players. |

The `Mix` assembly hosts code that runs on both client and server (movement, shooting). `Client` and `Server`
assemblies hold side-specific code. Side selection is done with `[WorldSystemFilter]` on systems and with
`#if UNITY_SERVER` for MonoBehaviours. See [assemblies.md](assemblies.md) for the build-time filtering.

### Hybrid bridge

ECS systems reach MonoBehaviour singletons through static `Instance` properties. This is the only bridge between
the two worlds and it is worth knowing the complete list, because a `null` here silently disables a system:

| MonoBehaviour singleton | Read by systems |
|---|---|
| `HybridCameraManager.Instance` | `PlayerCameraTargetUpdater`, `PlayerVehicleInputSystemBase` (early-out), `StartingGameSystem`, `CheckVehicleLife`, `UpdateMessageNearBoundsSystem` |
| `HUD.Instance` | `UpdateLifeBar`, `UpdateLaser`, `UpdateNotification`, `UpdateScoreRankingSystem`, `CheckVehicleLife`, `PlayerCameraTargetUpdater` |
| `PlayerInfoController.Instance` | `SendPlayerInfoSystem`, `InitializePlayerNameTag`, `UpdatePlayerNameTag`, `SpawnSinglePlayer`, `InitializedVivoxSystem`, `AutonInitMainSceneInEditor` |
| `UIControlSettings.Instance` | `UpdateControlSettingsSystem` |
| `MobileControls.Instance` | `PlayerVehicleInputSystemBase` (mobile builds) |
| `MatchMakingUI.Instance`, `MatchMakingConnector.Instance`, `MainMenu.Instance` | `CheckServerConnectionTimeoutSystem`, `MatchMakingMainMenuSystem`, `CheckServerConnectionSystem` |
| `ModalWindow.Instance`, `LoadingScreen.Instance`, `GameSettingsOptionsMenu.Instance` | Connection / loading systems |
| `NetcodePanelStats.Instance` | `UpdateNetcodePanelStatsSystem` |
| `VivoxManager.Instance` | `InitializedVivoxSystem` |
| `PoolManager.Instance` | `DynamicInstanceLinkSystem`, `VehicleFXSystem` (via `GameObjectPool`) |
| `ServerBehaviour.Instance` (server only) | `PlayerLeftEventSystem` |

Static state on MonoBehaviours is reset with `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` where the
authors hit "disable domain reload" issues (`HybridCameraManager`, `RateSettings`).

---

## 3. Scenes and subscenes

| Scene | Role |
|---|---|
| `Assets/Scenes/Menu.unity` | Front end: `MainMenu`, `MultiplayerMenu`, `SessionBrowser`, `MatchMakingUI`, settings tabs, `ModalWindow`, `LoadingScreen`, `MatchMakingConnector`. |
| `Assets/Scenes/Main.unity` | The game. Holds the hybrid GameObjects (`HybridObjects` camera rig, `PersistentDataManager`, `InputReader`, `PersistentObjects`, `MatchmakingConnector`, `Player` name-tag controller, HUD UI documents, steam VFX) and six **SubScenes** that bake into entities: |
| `Main/Common.unity` | `Settings` object: `ControlSettingsAuthoring` + `GeneralSettingsAuthoring`. `NetcodeSettings` object: `NetCodePhysicsConfig` (lag compensation on), `EnableConnectionMonitorAuthoring`, `BotAuthoring`. Also the Unity Physics `PhysicsStep` (gravity -9.81, 1 solver iteration, multithreaded). |
| `Main/Player_Subscene.unity` | `VehicleSpawner`: `PlayerSpawnerAuthoring` referencing `VehicleControlMultiplayer.prefab` and `VehicleControlSinglePlayer.prefab`. |
| `Main/MegacityMetroLevelBounds.unity` | `SpawnPointAuthoring` (collects all `SpawnPoint` MonoBehaviours at bake time) and `LevelBoundsContainerAuthoring`. |
| `Main/Level.unity` | The city geometry, lights, holograms (very large). |
| `Main/Traffic.unity` | Road paths (`Path`, `PathNode`, `RoadSettings`) and `TrafficConfigAuthoring`. |
| `Main/Blimps.unity` | `BlimpAuthoring` instances. |
| `Main/Audio_Subscene.unity`, `Holograms.unity`, `AdditiveLaserPoolScene.unity`, `AdditiveLightPoolScene.unity` | Auxiliary content scenes present in the folder. |

The player prefabs are **ghost prefabs** (they carry `GhostAuthoringComponent`). In the `LocalWorld` the ghost
components are inert and `SpawnSinglePlayer` adds `GhostOwnerIsLocal` by hand so the client-side systems treat the
single-player ship exactly like the locally owned multiplayer ship.

Scene loading is driven by `SceneController` (`LoadGame` loads `Main` asynchronously, `LoadMenu` loads `Menu`).
Subscene streaming progress is exposed through the `GameLoadInfo` singleton (`LoadingScreenSystem`) and
consumed by `StartingGameSystem`, which hides the loading screen when all sections are loaded, the camera is
placed, and a local player exists.

---

## 4. Assemblies (dependency direction)

```
Unity.MegacityMetro.Components      <- pure data: IComponentData, IBufferElementData, IInputComponentData, aspects
        ^              ^
Unity.MegacityMetro.Utils           <- MathUtilities, PhysicsUtilities, pooling, save system, RateSettings
        ^
Unity.Netcode.Extensions            <- PlayerName/PlayerStats/connection monitor + Netcode panel
        ^
Unity.MegacityMetro.Gameplay.Mix    <- movement, shooting, bootstrap, UGS SDK wrappers, scene controller
        ^              ^
Unity.MegacityMetro.Client          Unity.MegacityMetro.Server
Unity.MegacityMetro.Authoring       <- bakers only (editor + baking)
Unity.MegacityMetro.Common          <- generated GameInput, UIEvents
Unity.Services.UGS.Authentication / Unity.Services.UGS.Client / Unity.UgsTutorials
```

The server build excludes three baking-system assemblies (`ProjectSettings/NetCodeServerSettings.asset`).
`ProjectSettings/NetCodeClientSettings.asset` currently has `ClientTarget: 0` (Client **and** Server); the
multiplayer-setup guide tells you to switch it to `Client` before building a client.

---

## 5. Boot sequence (summary)

1. `NetCodeBootstrap.Initialize` runs before any scene logic (it is the project's `ClientServerBootstrap`).
   - Server platform: load `Main` if needed, create server world, start `ServerBehaviour` (UGS).
   - Client / editor: create a `LocalWorld` (menu), or auto-create client+server worlds when the editor is opened
     directly on the `Main` scene (`MultiplayerServerSettings.AutoRunGameModeInEditorMain`).
   - Thin clients are created if requested on the command line.
2. `Menu` scene: `MatchMakingConnector.Start` signs in anonymously to UGS and creates a `ClientManager`.
3. Player picks **Single Player** (`SceneController.LoadGame`, `LocalWorld` keeps running) or **Multiplayer**
   (matchmake or join a session, then `ServerConnectionUtils.RequestConnection` creates a fresh `ClientWorld`,
   loads `Main`, and connects).
4. In `Main`, the server spawns a ghost per connection (`ServerInGame`), the client marks the connection in-game
   (`ClientInGame`), sends its name (`SendPlayerInfoSystem`), and the loading screen drops once the local ghost
   arrives (`StartingGameSystem`).

Full detail with every component and RPC involved: [netcode.md §4](netcode.md#4-connection-lifecycle).

---

## 6. Frame and tick anatomy

Netcode runs the **client** `SimulationSystemGroup` once per rendered frame and the **server** at the network tick
rate (30 Hz, see `RateSettings`). Prediction happens inside `PredictedSimulationSystemGroup`; physics runs in
`PredictedFixedStepSimulationSystemGroup` (fixed 1/30 s). In the `LocalWorld` the same physics systems run in the
regular `FixedStepSimulationSystemGroup`, whose timestep `InitLocalRateSystem` sets to 1/30 s.

Ordered map of project systems (package systems omitted):

```
InitializationSystemGroup
  SetRpcSystemDynamicAssemblyListSystem      (client/server/thin, runs once)
  ClientInGame                               (client/thin)  register smoothing action, mark NetworkStreamInGame
  ServerInGame  [OrderLast]                  (server)       tick rate, importance grid, spawn players
  InitLocalRateSystem                        (local)        fixed step = 1/30
  LoadingScreenSystem                        (all)          GameLoadInfo

GhostInputSystemGroup                         (client)      PlayerVehicleInputSystemBase  -> PlayerVehicleInput
                                              (thin)        AddThinClientInputSystem, MoveThinClientInputSystem
SimulationSystemGroup [OrderFirst]            (local)       PlayerVehicleInputSystemSinglePlayer

PredictedSimulationSystemGroup                (client predicted + server)
  PredictedFixedStepSimulationSystemGroup
    PhysicsSystemGroup
      BeforePhysicsSystemGroup
        ReconstructVehicleRotationSystem [OrderFirst]   LocalTransform.Rotation <- VehicleMovementState
        PlayerVehicleControlSystem (MoveJob)            input -> pitch/yaw/roll, PhysicsVelocity.Linear
        ApplyVehicleRotationAsVelocitySystem [OrderLast] PhysicsVelocity.Angular to reach target rotation
      PhysicsSimulationGroup                            integrate, damping, contacts
      AfterPhysicsSystemGroup
        PlayerVehicleCollisionSystem                    throttle cut on contact (see known-issues)
  ShootingSystem (LaserJob)                             auto-aim + energy, lag compensated
  VehicleHealthAndImmunitySystem                        (server only) death timer, respawn, immunity
  CheatShootingSystem                                   (editor only)

SimulationSystemGroup (default position)
  ShootingServerSystem (DamageJob)            (server)   apply damage, kills, score
  SetPlayerInfoSystem                         (server)   consume SetPlayerInfoRequest RPC
  VerifyPlayerDisconnectedSystem              (server)   PlayerConnectedElement cleanup -> PlayerLeftEvent
  PlayerLeftEventSystem                       (server, UNITY_SERVER) UGS RemovePlayer
  ConnectionMonitorSystem                     (server)   ConnectionState bookkeeping + info RPCs
  SendPlayerInfoSystem                        (client/thin)
  CheckServerConnectionSystem, CheckServerConnectionTimeoutSystem, QuitSystem, MatchMakingMainMenuSystem (client)
  InterpolatedVehicleRotationSystem [before TransformSystemGroup] (client) rotation for remote ghosts
  InitBoundsSystem [in TransformSystemGroup] (client/local)
  UpdateBoundsSystem [FixedStepSimulationSystemGroup] (client/local)
  PlayerCameraTargetUpdater [after TransformSystemGroup] (client/local) drives HybridCameraManager
  DynamicInstanceLinkSystem [after TransformSystemGroup] (client/local) pooled FX GameObjects
  UpdateControlSettingsSystem, UpdateMessageNearBoundsSystem, InitializePlayerNameTag, UpdatePlayerNameTag,
  UpdateScoreRankingSystem, UpdateNotification, UpdateLaser, CheckVehicleLife, InitializedVivoxSystem,
  SubmeshColorSystem, PlayerColorSystem, RandomShipVisualsSystem, SpawnSinglePlayer, TrafficSetupSystem,
  TrafficSystem, BlimpSystem, StartingGameSystem
  NetCodePanelStats group -> NetCodePanelStatsPresentation group (dev builds)
  LightsManagementSystem [OrderLast]

PresentationSystemGroup
  VehicleFXSystem                             (client/local) laser/engine/shield/damage/death FX + audio
  UpdateLifeBar [OrderLast]                   (client)
```

---

## 7. Key singletons, tags and buffers

| Component | Kind | Created by | Consumers |
|---|---|---|---|
| `ControlSettings` | singleton | baked from `Common.unity` | input system, `UpdateControlSettingsSystem` |
| `GeneralSettings` | singleton | baked from `Common.unity` | `LightsManagementSystem` |
| `PlayerSpawner` | singleton | baked from `Player_Subscene` | `ServerInGame`, `SpawnSinglePlayer`, `ClientInGame` (RequireForUpdate) |
| `SpawnPointElement` | buffer | baked from `MegacityMetroLevelBounds` | spawning |
| `LevelBoundsContainer`, `LevelBounds` | singleton / instance | baked | bounds systems |
| `BotNameElement` | buffer | baked (`BotAuthoring`) | `SetPlayerInfoSystem` |
| `EnableConnectionMonitor` | tag singleton | baked | `ConnectionMonitorSystem` |
| `TrafficConfig`, `TrafficEnabled`, `VehiclePrefabRef`, `Road` | config | baked | traffic |
| `GameLoadInfo` | singleton | `LoadingScreenSystem` | `StartingGameSystem`, `LoadingScreen` |
| `PlayerConnectedElement` | buffer | `ServerInGame.OnCreate` | `SetPlayerInfoSystem`, `VerifyPlayerDisconnectedSystem` |
| `ClientServerTickRate` | singleton | `ServerInGame.OnCreate` | Netcode |
| `GhostDistanceData` + `GhostImportance` | singleton | `ServerInGame.OnCreate` | Netcode importance scaling |
| `GhostSendSystemData` | singleton | `ClientInGame.OnCreate` (client side, see known issues) | Netcode (server only) |
| `PlayerStats` | per local player | `InitializeNetcodePanelStatsSystem` | Netcode panel |
| `PlayerLocationBounds` | on local player | `InitBoundsSystem` | bounds message |
| `GhostOwnerIsLocal` | enableable tag (Netcode) | Netcode / `SpawnSinglePlayer` | "is this my ship" everywhere on the client |
| `SinglePlayer` | tag | `SpawnSinglePlayer` | `StartingGameSystem` |
| `NameAssigned`, `PlayerNameTag`, `VivoxConnectionStatus`, `MultiMaterialUpdated`, `InitializedConnection` | one-shot tags | various | prevent reprocessing |
| `PlayerLeftEvent` | event entity | `VerifyPlayerDisconnectedSystem` | `PlayerLeftEventSystem` |

---

## 8. Build-time switches and command line

| Define / arg | Effect |
|---|---|
| `UNITY_SERVER` | Dedicated-server code paths (`ServerBehaviour`, `PlayerLeftEventSystem`, Vivox off, stubs in `PluginLoader`). |
| `ENABLE_UCS_SERVER` | Compiles `ServerManager`/`SessionEventManager` outside server builds (testing). |
| `ENABLED_VIRTUAL_JOYSTICK` | Forces the mobile input path (joystick + speed slider) on desktop. |
| `AUTO_QUALITY_SETTINGS` | Enables `QualitySettingsSystem` (drops quality when FPS < 60). |
| `DEVELOPMENT_BUILD` / `UNITY_EDITOR` | Netcode stats panel, cheat key `1`, `CheatShootingSystem` (editor only). |
| `USING_UNITY_LOGGING` | Mentioned in server launch docs (`-define:`), used by Unity Logging. |
| `--enable-thin-clients N` | Create N thin-client worlds; headless (`-batchmode`) also auto-matchmakes. |
| `--max-cars N` | Parsed into `CommandLineConfig.MaxCarCount` (currently not consumed by any system). |
| `-port P` | Server listen port (server builds); default 7979. |
| `--api-key-id`, `--api-key-secret` | UGS service-account credentials for running a server outside Multiplay. |

---

## 9. Where to start when extending

- New replicated gameplay state: add fields with `[GhostField]` to a component in `Components`, write the
  simulation in `Mix` inside `PredictedSimulationSystemGroup` if the owner should predict it, or server-only
  otherwise. Checklist in [netcode.md §15](netcode.md#15-extension-recipes).
- New flight behaviour: everything is in `MoveJob` plus the authoring component. Read
  [flight-control.md](flight-control.md) first; it includes a concrete plan for multiple flight profiles.
- New UI: UI Toolkit documents under `Assets/UI`, MonoBehaviour controllers under `Gameplay/Client/UI`,
  systems that push ECS data into them under `Gameplay/Client/UI/Systems`.
