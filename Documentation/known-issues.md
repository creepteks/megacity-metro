# Known Issues and Pitfalls

Findings from reading the source. Each entry states what the code does, why it matters, and what to do about it.
"Verify" means the conclusion follows from the code and asset data but was not confirmed at runtime.

## Flight control

| # | Finding | Where | Impact | Suggested action |
|---|---|---|---|---|
| F1 | Throttle cut on collision never fires. `PlayerVehicleCollisionJob` requires custom physics tag bit 0, but the prefab is baked from a legacy `Rigidbody`/`CapsuleCollider`, which produces no `PhysicsCustomTags`. | `Mix/Player/Jobs/PlayerVehicleCollisionJob.cs`, `Prefabs/Vehicles/VehicleControlMultiplayer.prefab` | Collisions feel "bouncy/solver-only" rather than the designed speed loss. Verify by inspecting the ship entity for `PhysicsCustomTags`. | Replace the legacy components with `PhysicsBodyAuthoring` (Custom Tags = `PlayerVehicle`) + `PhysicsShapeAuthoring`, or add `PhysicsCustomTags { Value = 1 }` in the baker. |
| F2 | ~~`RollAccumulationSpeed` is applied per tick, not per second.~~ **Fixed** by the flight profile system: scaled by `dt * 30`. | `MoveJob` roll block | – | – |
| F3 | ~~Mouse input is sampled per frame but consumed per tick.~~ **Fixed**: `PlayerVehicleInputSystemBase` accumulates `LookDelta` across the frames of one tick. | `PlayerVehicleInputSystemBase` | – | – |
| F4 | `TargetFollowDamping` and `TargetSqLerpThreshold` are baked into `PlayerVehicleSettings` but never read. | `PlayerVehicleControllerAuthoring`, `PlayerVehicleComponents` | Misleading tuning fields. | Remove, or move the camera to use them. |
| F5 | Trackpad detection only works on macOS: Windows player never loads the DLL (`LoadPlugin` returns early) and Windows editor calls a stub returning 0. | `Client/Player/PluginLoader.cs` | `WinTrackpadSensitivity` has no effect. | Fix the platform branches or replace with a user toggle. |
| F6 | `VehicleControlSinglePlayer.prefab` has stale overrides (`SteeringSharpness`, `PhysicsShapeAuthoring` fields) for components not present on the base prefab. | prefab variant | None functionally; confusing when tuning. | Clean the variant overrides. |
| F7 | ~~Remote ships show a brief spin when `BaseRoll` wraps at ±π.~~ **Fixed**: orientation is now replicated as `VehicleMovementState.Rotation` (quaternion). | `VehicleMovementState` | – | – |
| F8 | Effective mouse sensitivity is defined by `Resources/UserPreferences.asset` (2.0), not by `ControlSettingsAuthoring.MouseSensitivity` (1.0), because the settings UI overwrites the singleton on start. | `UIControlSettings`, `UpdateControlSettingsSystem` | Tuning the authoring field does nothing for returning players. | Treat the preferences asset as the source of truth or remove the duplication. |
| F9 | ~~Death gravity, max bank and other constants are hard-coded.~~ **Fixed**: `DeathGravity`, `MaxBankAngle`, damping, etc. are `FlightProfile` fields. The 0.01 steering multiplier stays a constant (`FlightProfileBlob.BaseSteeringSpeedMultiplier`). | `MoveJob` | – | – |

## Netcode

| # | Finding | Where | Impact | Suggested action |
|---|---|---|---|---|
| N1 | `MegacityMetroDriverConstructor` (queue capacity `150 * 5`) is never registered. Only the SDK's `CustomDriverConstructor` is, and only in processes that joined a session as a client. The dedicated server uses Netcode's default queues. | `Mix/Multiplayer/Netcode/MegacityMetroDriverConstructor.cs`, `NetcodeBootstrap.cs` | Possible transport queue overflow warnings/drops at high player counts on the server. | Set `NetworkStreamReceiveSystem.DriverConstructor = new MegacityMetroDriverConstructor()` at the start of `NetCodeBootstrap.Initialize`. |
| N2 | `GhostSendSystemData { MinSendImportance = 2 }` is created in the **client** world (`ClientInGame.OnCreate`), where `GhostSendSystem` does not run. | `Client/Netcode/ClientInGame.cs` | The intended server-side optimisation is inert. | Create it in `ServerInGame.OnCreate`. |
| N3 | Joining through the Multiplayer Services SDK makes two connections: the handler connects the SDK-created `ClientWorld`, then `ServerConnectionUtils.ConnectToServer` disposes that world and connects a new one. | `MatchMakingConnector`, `CustomEntitiesNetworkHandler`, `ServerConnectionUtils` | Server spawns and destroys a ship for the first connection; extra log noise; fragile if the SDK later relies on its connection entity. | Keep the SDK-connected world and load `Main` into it, or skip the SDK network handler and use the endpoint only. |
| N4 | Session `Type` differs: client `"MegacityMetroSession"` vs server `"NFESession"`. | `ClientManager`, `ServerManager` | Depends on SDK semantics for `Type`; verify sessions are discoverable/joinable with the mismatch. | Share one constant. |
| N5 | `ConnectionStateInfoRequest` RPCs are broadcast by the server but no client system consumes them. | `ConnectionMonitorSystem` | Wasted bandwidth per connect/disconnect (small). | Add a client consumer or remove the sends. |
| N6 | Thin-client input: `rand.NextInt() < 0.4f` and `< 0.3f` compare a full-range int to a fraction (≈50 % true); `LookVelocity` is in [-1, 1] while real input is ×100, so bots barely turn. | `Client/Netcode/MegacityMetroThinInputSystem.cs` | Load tests do not represent real movement/bandwidth. | Use `NextFloat() < 0.4f`; scale look by 100. |
| N7 | `PlayerUASID` is added at runtime with `AddComponent`; ghost serialization only covers components present on the baked prefab, so the `[GhostField]` on it never replicates. | `SetPlayerInfoSystem`, `PlayerName.cs` | Server-only data presented as replicated. | Put the component on the prefab (empty) if clients need it. |
| N8 | `RandomShipVisualsSystem` has no world filter and runs on the server, instantiating visual prefabs there. | `Mix/Player/RandomShipVisualsSystem.cs` | Wasted server entities/CPU. | Filter to `ClientSimulation | LocalSimulation` and keep the server-side laser start point/FX assignment separate (server needs `LocalLaserStartPoint`). |
| N9 | `SpawnPointAuthoring` uses `FindObjectsByType<SpawnPoint>` during baking, which depends on what is loaded in the editor at bake time. | `Authoring/Player/SpawnPointAuthoring.cs` | Spawn list can differ between bakes. | Reference spawn transforms explicitly (children or a serialized list) and `DependsOn` them. |
| N10 | Player prefab `Importance` is 10, inside the project's own "Medium (5–40)" band; the suggestion for player ghosts is 50–250. | prefab ghost settings | Distant ships can starve relative to nothing else, since players are the only ghosts; mostly harmless today. | Revisit when adding other ghost types. |
| N11 | `ClientMatchmaker` (direct Matchmaker ticket flow) is dead code since the move to sessions; `CommandLineConfig.MaxCarCount` is parsed but unused. | `UGS/Client/ClientMatchmaker.cs`, `CommandLineConfig.cs` | Confusion. | Remove or mark deprecated. |
| N12 | `PlayModeContext` has `using UnityEditor;` in a runtime assembly. | `Mix/Multiplayer/SDK/Utils/PlayModeContext.cs` | Compiles today (no `UnityEditor.*` symbols used) but will break the server/player build if any editor symbol is referenced. | Wrap in `#if UNITY_EDITOR` or delete the using. |
| N13 | `PlayerVehicleInputSystemBase` writes the local input into **every** entity with `PlayerVehicleSettings` + `PlayerVehicleInput` in the client world. Safe only because remote ghosts are interpolated (input stripped). | `Client/Player/PlayerVehicleInputSystem.cs` | Switching remote ships to predicted mode would corrupt their input. | Filter with `GhostOwnerIsLocal`. |
| N14 | `NetCodeClientSettings.ClientTarget` is `ClientAndServer`; client builds must be switched to `Client` manually (documented in multiplayer-setup.md). | `ProjectSettings/NetCodeClientSettings.asset` | Oversized client builds with server systems if forgotten. | Build-profile script or CI check. |

## Gameplay / content

| # | Finding | Where | Impact | Suggested action |
|---|---|---|---|---|
| G1 | Traffic runs only on clients and is not authoritative; traffic collisions can differ per client and from the server. | `Client/Traffic/*` | Prediction corrections when a ship hits traffic. | Accept (cosmetic traffic) or move traffic to a deterministic server-side simulation. |
| G2 | `HybridCameraManager.StartShaking` is a stub; `UpdateLifeBar` calls it on damage. | `HybridCameraManager.cs` | No camera shake. | Implement or remove the call. |
| G3 | `LaserJob` energy gate `laser.Energy > 0 || ownerHealth.IsDead == 0` is always true for living ships, so `IsShooting` stays true at zero energy. `LaserVisualJob` and `VehicleFXSystem` hide the beam when `Energy <= 0`, but `DamageJob` never checks energy, so an empty laser still deals damage invisibly. | `LaserJob`, `DamageJob` | Energy bar limits visuals, not damage. Verify in play. | Gate `IsShooting` on `Energy > 0` (and recharge only when not firing) so server and visuals agree. |
| G4 | Engine audio pitch saturates at 30 u/s (≈ 1/3 of top speed). | `VehicleFXSystem` | Weak speed feedback at high speed. | Expose the range (flight profile). |
| G5 | `AnimationCurveBlob` utility is unused. | `Components/Gameplay/Vehicle/AnimationCurveBlob.cs` | None. | Reuse for input response curves. |
