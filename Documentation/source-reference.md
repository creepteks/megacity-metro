# Source Reference

One line per script under `Assets/Scripts`, grouped by folder. World column: **C** client, **S** server, **L** local
(single player), **T** thin client, **E** editor/baking only, **M** MonoBehaviour (GameObject side). Deep dives:
[flight-control.md](flight-control.md), [netcode.md](netcode.md), [gameplay-systems.md](gameplay-systems.md).

## Authoring (`Unity.MegacityMetro.Authoring`, bakers only)

| File | World | Purpose |
|---|---|---|
| `Authoring/Level/BlimpAuthoring.cs` | E | Bakes `BlimpComponent` (circle radius, speed, look-ahead). |
| `Authoring/Level/LevelBoundsAuthoring.cs` | E | Bakes `LevelBounds { MaxDistance }` on the bounds prefab. |
| `Authoring/Level/LevelBoundsContainerAuthoring.cs` | E | Bakes `LevelBoundsContainer { BoundsPrefab }` at the map centre. |
| `Authoring/Player/PlayerCameraTargetAuthoring.cs` | E | Bakes `PlayerCameraTarget { VerticalOffset }` (prefab value 2). |
| `Authoring/Player/PlayerSpawnerAuthoring.cs` | E | Bakes `PlayerSpawner { Player, SinglePlayer }` prefab entities. |
| `Authoring/Player/PlayerVehicleControllerAuthoring.cs` | E | **All flight, laser and immunity tuning** → `PlayerVehicleSettings`, `VehicleLaser`, `VehicleHealth`, `PlayerScore`, `PlayerPlatform`, `Immunity`, `PlayerVehicleInput`, `VehicleMovementState`, `WeaponShotIgnoredEntity`. |
| `Authoring/Player/ShipRandomVisualsAuthoring.cs` | E | Bakes the `ShipRandomVisuals` buffer of hull prefabs and `RootEntity`. |
| `Authoring/Player/ShipVisualsAuthoring.cs` | E | Bakes `ShipVisuals { FXEntity, LocalLaserStartPoint }` per hull. |
| `Authoring/Player/SpawnPointAuthoring.cs` | E | Collects all `SpawnPoint` MonoBehaviours into a `SpawnPointElement` buffer. |
| `Authoring/Settings/ConfigureLocalWorldGUIDSystem.cs` | E/L | Points the local world's `SceneSystem` at the client build configuration GUID. |
| `Authoring/Settings/ControlSettingsAuthoring.cs` | E | Bakes the `ControlSettings` singleton (trackpad/mouse sensitivity, acceleration range). |
| `Authoring/Settings/GeneralSettingsAuthoring.cs` | E | Bakes `GeneralSettings` for light culling (layer filter, range, max lights, fade). |
| `Authoring/Traffic/Path.cs` | M/E | Editable road path drawn from `PathNode`s; gizmos and lane data. |
| `Authoring/Traffic/PathNode.cs` | M/E | Node on a `Path`; `RoadWaypoint` struct. |
| `Authoring/Traffic/RoadSettings.cs` | M/E | Per-road lane settings. |
| `Authoring/Traffic/TrafficConfigAuthoring.cs` | E | Bakes `TrafficConfig`, `TrafficEnabled`, one `VehiclePrefabRef` entity per prefab. |
| `Authoring/Traffic/TrafficConversionRule.cs` | E | `TrafficPathBaker` + `TrafficPathBakingSystem`: converts paths into `RoadPoint` buffers and `RoadOffset`. |
| `Authoring/Traffic/TrafficVehicleAuthoring.cs` | E | Tags NPC vehicle prefabs. |

## Common (`Unity.MegacityMetro.Common`)

| File | World | Purpose |
|---|---|---|
| `Common/Events/UIEvents.cs` | M | Static `OnNavigate`, `OnSubmit`, `OnCancel` events raised by `InputReader`. |
| `Common/Input/GameInput.cs` | M | Generated Input System wrapper for `GameInput.inputactions` (maps `Gameplay`, `UI`, `HUD`). |

## Components (`Unity.MegacityMetro.Components`, pure data)

| File | Purpose |
|---|---|
| `Components/Audio/AudioSystemSettings.cs` | ECS mirror of `AudioMaster` constants. |
| `Components/Camera/PlayerCameraTarget.cs` | `PredictedClient`-only marker with `VerticalOffset`; tells `PlayerCameraTargetUpdater` which ship to follow. |
| `Components/Gameplay/Bounds/LevelBounds.cs` | `LevelBounds { MaxDistance }`, `LevelBoundsContainer { BoundsPrefab }`. |
| `Components/Gameplay/GameLoadInfo.cs` | Subscene section load progress singleton. |
| `Components/Gameplay/Level/BlimpComponent.cs` | Blimp orbit state. |
| `Components/Gameplay/Level/SpawnPoint.cs` | MonoBehaviour marker with gizmos; read at bake time. |
| `Components/Gameplay/Player/Input/ControlSettings.cs` | Sensitivity / invert / acceleration range singleton. |
| `Components/Gameplay/Player/Input/PlayerVehicleInput.cs` | `IInputComponentData` command: `LookVelocity`, `AimAssistSensitivity`, `Acceleration`, `Roll`, `Shoot`, `Cheat_1`. `AllPredicted`. |
| `Components/Gameplay/Player/MegacitySmoothing.cs` | Burst prediction-error smoothing action for `LocalTransform` (0.5 units/frame, snap ≥ 20). |
| `Components/Gameplay/Player/NameAssigned.cs` | Tag: connection already sent its `SetPlayerInfoRequest`. |
| `Components/Gameplay/Player/PlayerInfoItemSettings.cs` | ScriptableObject: `GameMode`, player name, name-tag distances/scales. |
| `Components/Gameplay/Player/PlayerInfoRef.cs` | UI Toolkit references for one name tag (label, life bar, badge, icon) and their update logic. |
| `Components/Gameplay/Player/PlayerNameTag.cs` | Tag: name tag created for this ghost. |
| `Components/Gameplay/Player/PlayerScoreAspect.cs` | Aspect over `PlayerScore`, `PlayerPlatform`, `PlayerName`, `GhostOwner` for the leaderboard. |
| `Components/Gameplay/Player/RootEntity.cs` | Back-reference from a visual child to the ship root. |
| `Components/Gameplay/Player/SetPlayerInfoRequest.cs` | RPC: `Name`, `UASId`, `IsClient`, `Platform`. |
| `Components/Gameplay/Player/ShipRandomVisuals.cs` | Buffer of hull prefab entities. |
| `Components/Gameplay/Player/ShipVisuals.cs` | `FXEntity`, `LocalLaserStartPoint` of a hull. |
| `Components/Gameplay/Player/Spawner/PlayerSpawner.cs` | `PlayerSpawner { Player, SinglePlayer }`, `SinglePlayer` tag. |
| `Components/Gameplay/Player/Spawner/SpawnPointElement.cs` | Spawn position buffer element. |
| `Components/Gameplay/Traffic/CatmullRom.cs` | Spline helper. |
| `Components/Gameplay/Traffic/TrafficComponents.cs` | `Constants`, `Road`, `VehiclePathing`, `TrafficConfig`, `TrafficEnabled`, `VehiclePrefabRef`, `RoadPoint`, `RoadOffset`. |
| `Components/Gameplay/Vehicle/AnimationCurveBlob.cs` | Baked 20-sample curve blob with looping `Evaluate` (unused). |
| `Components/Gameplay/Vehicle/Immunity.cs` | `Duration`, replicated `Counter`, local FX state. |
| `Components/Gameplay/Vehicle/PlayerLocationBounds.cs` | `IsInside` for the local player. |
| `Components/Gameplay/Vehicle/PlayerPlatform.cs` | Replicated `RuntimePlatform` int. |
| `Components/Gameplay/Vehicle/PlayerScore.cs` | Replicated score, kills, killer/victim names. |
| `Components/Gameplay/Vehicle/PlayerVehicleComponents.cs` | `PlayerVehicleSettings`: the flight tuning component. |
| `Components/Gameplay/Vehicle/VehicleHealth.cs` | Replicated HP, `IsDead`, `LookAtEnemyDegrees`; local death timer/FX state. |
| `Components/Gameplay/Vehicle/VehicleLaser.cs` | Laser tuning + replicated `Energy`, `IsShooting`, `DetectedTarget`; start/end point helpers. |
| `Components/Gameplay/Vehicle/VehicleMovementState.cs` | Replicated pitch/yaw/roll, `BaseRoll`, `RollAccumulationFromTurn`, `LastLookVelocity`; rotation builders. |
| `Components/Gameplay/Vehicle/WeaponShotIgnoredEntity.cs` | Buffer of entities the laser ignores (self). |
| `Components/Settings/GeneralSettings.cs` | Light culling settings + sphere collider blob. |

## Gameplay / Client (`Unity.MegacityMetro.Client`)

| File | World | Purpose |
|---|---|---|
| `Client/Audio/MonoBehaviours/AudioMaster.cs` | M | Mixer groups, traffic audio limits, menu music, Android volume callback. |
| `Client/Camera/MonoBehaviours/DeadScreenFX.cs` | M | Saturation/chromatic aberration transition on death. |
| `Client/Camera/MonoBehaviours/HybridCameraManager.cs` | M | Follow camera: `FollowDistance 10`, `FollowSharpness 18`, `LookAtSharpness 15`; `SmoothFollow` + slerp. |
| `Client/Camera/MonoBehaviours/PostProcessingBloomModifier.cs` | M | Animates URP bloom. |
| `Client/Camera/MonoBehaviours/PostProcessingGlitch.cs` | M | Glitch post effect. |
| `Client/Camera/MonoBehaviours/PostProcessingVignetteModifier.cs` | M | Animates vignette. |
| `Client/Camera/PlayerCameraTargetUpdater.cs` | C/L | Feeds `HybridCameraManager` with the local ship and aim point; updates crosshair. |
| `Client/Input/AssignCameraInput.cs` | M | Sets `PlayerInput.camera = Camera.main`. |
| `Client/Input/InputReader.cs` | M | Enables the `UI` map and raises `UIEvents`. |
| `Client/Input/VirtualJoystick.cs` | M | UI Toolkit touch joystick (`Delta` in [-1,1]). |
| `Client/Level/AutonInitMainSceneInEditor.cs` | E | Applies `AutoRunGameModeInEditorMain` when the editor starts in `Main`. |
| `Client/Level/BoundsSystem.cs` | C/L | `UpdateBoundsSystem` + `CheckStateIsInsideBoundsJob` (signed distance to bounds collider). |
| `Client/Level/InitBoundsSystem.cs` | C/L | Instantiates bounds prefab, adds `PlayerLocationBounds` to the local player. |
| `Client/Level/MonoBehaviours/PersistentObject.cs` | M | `DontDestroyOnLoad` helper. |
| `Client/Level/UpdateMessageNearBoundsSystem.cs` | C/L | Shows/hides the out-of-bounds HUD message. |
| `Client/Netcode/CheckServerConnectionTimeoutSystem.cs` | C | 20 s connect timeout → modal + disconnect. |
| `Client/Netcode/ClientConnectionSystem.cs` | C | `CheckServerConnectionSystem`: disconnected → modal → menu. |
| `Client/Netcode/ClientInGame.cs` | C/T | Registers smoothing action; adds `NetworkStreamInGame` + `ConnectionState`; (misplaced) `GhostSendSystemData`. |
| `Client/Netcode/MegacityMetroThinInputSystem.cs` | T | Creates thin-client input entity; randomizes input every 30 ticks. |
| `Client/Netcode/QuitSystem.cs` | C/L | Disconnect request and application quit. |
| `Client/Player/MonoBehaviours/ShipFXManager.cs` | M | References to VFX graphs and audio sources on a pooled FX prefab. |
| `Client/Player/PlayerNameSystem.cs` | C | `InitializePlayerNameTag`, `UpdatePlayerNameTag`. |
| `Client/Player/PlayerVehicleInputSystem.cs` | C (+L subclass) | **Input sampling**: mouse/keys/gamepad/touch → `PlayerVehicleInput` via `PlayerVehicleInputJob`. |
| `Client/Player/PluginLoader.cs` | M | Loads the trackpad native plugin; `TouchesCount()`. |
| `Client/Player/SetupPlayerInfoSystem.cs` | C/T | `SendPlayerInfoSystem`: sends `SetPlayerInfoRequest` once in game. |
| `Client/Player/UpdateControlSettingsSystem.cs` | C/L | Copies `UIControlSettings` values into the `ControlSettings` singleton. |
| `Client/Player/UpdatePlayerRankSystem.cs` | C | `UpdateScoreRankingSystem` + `SortableScoreComparer`: leaderboard and top badge. |
| `Client/Player/VehicleFXSystem.cs` | C/L (Presentation) | Laser/engine/shield/damage/death VFX and audio via pooled `ShipFXManager`; schedules `LaserVisualJob`. |
| `Client/Settings/InitLocalRateSystem.cs` | L | Fixed step 1/30 in single player. |
| `Client/Traffic/TrafficSetupSystem.cs` | C/L | Builds `NativeSpline` roads and spawns NPC vehicles. |
| `Client/Traffic/TrafficSystem.cs` | C/L | Moves NPC vehicles along splines with lane offset and banking. |
| `Client/UI/MonoBehaviours/HUD/Crosshair.cs` | M | Crosshair + aim icon projection. |
| `Client/UI/MonoBehaviours/HUD/HUD.cs` | M | Health bar, damage pointer, death/bounds messages, netcode panel spawn. |
| `Client/UI/MonoBehaviours/HUD/LaserBar.cs` | M | Energy bar. |
| `Client/UI/MonoBehaviours/HUD/MobileControls.cs` | M | Touch controls: joystick zone, speed slider, fire zone. |
| `Client/UI/MonoBehaviours/Leaderboard/UILeaderboard.cs` | M | Leaderboard list (full/collapsed). |
| `Client/UI/MonoBehaviours/MainMenu/GameVersion.cs` | M | Version label. |
| `Client/UI/MonoBehaviours/MainMenu/MainMenu.cs` | M | Main menu state machine; automatic matchmaking polling; quit. |
| `Client/UI/MonoBehaviours/MainMenu/MainMenuGameMode.cs` | M | Base for per-mode menu panels. |
| `Client/UI/MonoBehaviours/MainMenu/MultiplayerMenu.cs` | M | Matchmaker/Connect toggle, name field, session browser hookup. |
| `Client/UI/MonoBehaviours/MainMenu/SessionBrowser.cs` | M | Lists UGS sessions; join on select. |
| `Client/UI/MonoBehaviours/Matchmaker/MatchMakingLoadingBar.cs` | M | Spinner. |
| `Client/UI/MonoBehaviours/Misc/CustomFocusRing.cs` | M | Gamepad/keyboard focus cycling for the multiplayer form. |
| `Client/UI/MonoBehaviours/Misc/LoadingScreen.cs` | M | Progress bar bound to `GameLoadInfo`. |
| `Client/UI/MonoBehaviours/Misc/Notification.cs` | M | Kill-feed style notification. |
| `Client/UI/MonoBehaviours/Misc/TutorialScreen.cs` | M | Controls tutorial overlay. |
| `Client/UI/MonoBehaviours/Settings/GameSettingsOptionsMenu.cs` | M | In-game ESC menu (resume, settings, disconnect/quit). |
| `Client/UI/MonoBehaviours/Settings/UIAudioSettings.cs` | M | Volume sliders → mixer, Vivox volumes. |
| `Client/UI/MonoBehaviours/Settings/UIControlSettings.cs` | M | Mouse/aim sensitivity, invert toggles; persists `ControlSettingsData`. |
| `Client/UI/MonoBehaviours/Settings/UIGameSettings.cs` | M | Settings tab host. |
| `Client/UI/MonoBehaviours/Settings/UIGraphicsSettings.cs` | M | Quality, resolution, texture detail, post-processing, vsync, STP, render scale. |
| `Client/UI/MonoBehaviours/Settings/UIScreenResolution.cs` | M | Resolution/screen-mode helper. |
| `Client/UI/MonoBehaviours/Settings/UISettingsTab.cs` | M | Shared tab logic (save/reset state helpers). |
| `Client/UI/Systems/Gameplay/CheckVehicleLife.cs` | C | Death message and 5 s cooldown. |
| `Client/UI/Systems/Gameplay/UpdateLaser.cs` | C | Energy bar from `VehicleLaser.Energy`. |
| `Client/UI/Systems/Gameplay/UpdateLifeBar.cs` | C (Presentation, last) | Health bar, damage indicator, camera shake hook. |
| `Client/UI/Systems/MainMenu/CreateAudioSettingsSystem.cs` | C/L | Creates the `AudioSystemSettings` entity from `AudioMaster`. |
| `Client/UI/Systems/MainMenu/MatchMakingMainMenuSystem.cs` | C | Manual connect when the Play button requested it. |
| `Client/UI/Systems/StartingGameSystem.cs` | C/L/E | Hides the loading screen when sections, camera and player are ready. |
| `Client/UI/Systems/UpdateNotification.cs` | C | "YOU DESTROYED" notification on kill count change. |
| `Client/Vivox/InitializedVivoxSystem.cs` | C | Logs the local player into Vivox once named. |
| `Client/Vivox/MonoBehaviours/VivoxChannel.cs` | M | Channel join/leave. |
| `Client/Vivox/MonoBehaviours/VivoxDevicesVolume.cs` | M | Input/output device volume and mute. |
| `Client/Vivox/MonoBehaviours/VivoxManager.cs` | M | Service init and ownership of session/channel/devices. |
| `Client/Vivox/MonoBehaviours/VivoxPanel.cs` | M | Voice UI events. |
| `Client/Vivox/MonoBehaviours/VivoxPlayerList.cs` | M | Speaking indicators. |
| `Client/Vivox/MonoBehaviours/VivoxSession.cs` | M | Login/logout. |

## Gameplay / Mix (`Unity.MegacityMetro.Gameplay.Mix`, shared client and server)

| File | World | Purpose |
|---|---|---|
| `Mix/Level/BlimpSystem.cs` | C/L | Orbits blimps. |
| `Mix/Misc/LightsManagementSystem.cs` | all (simulation, last) | Keeps the N nearest lights enabled with fade-in. |
| `Mix/Misc/LoadingScreenSystem.cs` | all | Fills `GameLoadInfo`. |
| `Mix/Misc/ThinClientDebugDisplay.cs` | M | IMGUI status of thin-client worlds. |
| `Mix/Multiplayer/Netcode/CommandLineConfig.cs` | static | Parses `--enable-thin-clients`, `--max-cars`, `-port`, API keys. |
| `Mix/Multiplayer/Netcode/MegacityMetroDriverConstructor.cs` | – | UDP driver with `150 * 5` queue capacity (not registered). |
| `Mix/Multiplayer/Netcode/MegacityMetroVariants.cs` | baking | Ghost variants: position-only `LocalTransform`, owner-only `PhysicsVelocity.Linear`. |
| `Mix/Multiplayer/Netcode/NetcodeBootstrap.cs` | bootstrap | World creation for server/client/editor/thin clients; `MaxPlayerCount = 150`; default endpoint `127.0.0.1:7979`. |
| `Mix/Multiplayer/Netcode/ServerConnectionUtils.cs` | static | Create worlds and listen/connect; endpoint and domain parsing. |
| `Mix/Multiplayer/Netcode/SetRpcDynamicAssemblyListSystem.cs` | C/S/T | `RpcCollection.DynamicAssemblyList = true`. |
| `Mix/Multiplayer/SDK/Client/ClientManager.cs` | M-side | UGS sign-in, matchmake/join/leave sessions, `GameState`. |
| `Mix/Multiplayer/SDK/Client/CustomDriverConstructor.cs` | – | SDK-aware driver constructor (Direct/Relay, IPC host) with the queue fix on the server path. |
| `Mix/Multiplayer/SDK/Client/CustomEntitiesNetworkHandler.cs` | – | `INetworkHandler` bridging a session's `NetworkConfiguration` to Netcode worlds (listen/connect/validate). |
| `Mix/Multiplayer/SDK/Server/GameServerInfo_Data.cs` | asset | `GameServerInfo` (max players 150, name, type, map, build id, startup buffer). |
| `Mix/Multiplayer/SDK/Server/ServerBehaviour.cs` | M, server | Creates `ServerManager`, reads CLI keys, pumps `Dispatcher`. |
| `Mix/Multiplayer/SDK/Server/ServerManager.cs` | server | Multiplay session manager or local service-account session. |
| `Mix/Multiplayer/SDK/Server/SessionEventManager.cs` | server | Session event logging; `RemovePlayer`. |
| `Mix/Multiplayer/SDK/Utils/Dispatcher.cs` | – | Main-thread queue for actions/tasks/exceptions. |
| `Mix/Multiplayer/SDK/Utils/PlayModeContext.cs` | static | Platform flags and editor PlayMode compatibility check. |
| `Mix/Player/ApplyVehicleRotationAsVelocitySystem.cs` | C/S/L | `ReconstructVehicleRotationSystem`, `InterpolatedVehicleRotationSystem` (client), `ApplyVehicleRotationAsVelocitySystem`. |
| `Mix/Player/Jobs/PlayerVehicleCollisionJob.cs` | C/S/L | Throttle cut on contact for tagged bodies (see known issues F1). |
| `Mix/Player/Jobs/PlayerVehicleInputJob.cs` | C/L | Applies `InvertPitch` and writes `PlayerVehicleInput`. |
| `Mix/Player/Jobs/PlayerVehicleJobs.cs` | C/S/L | **`MoveJob`** (flight math), rotation jobs, `HealthAndImmunityJob`. |
| `Mix/Player/MonoBehaviours/PlayerInfoController.cs` | M | Name-tag manager, game mode, Vivox prefab spawn. |
| `Mix/Player/MonoBehaviours/RandomPositionGenerator.cs` | M | Random positions around a transform (editor/debug helper). |
| `Mix/Player/PlayerVehicleCollisionSystem.cs` | C/S/L | Schedules the collision job after physics. |
| `Mix/Player/PlayerVehicleControlSystem.cs` | C/S/L | Schedules `MoveJob`; `VehicleHealthAndImmunitySystem` (server). |
| `Mix/Player/RandomShipVisualsSystem.cs` | all | Picks a hull per ship (seeded by name) and copies laser/FX references to the root. |
| `Mix/Player/SpawnSinglePlayer.cs` | all (gated) | Spawns the single-player ship with `SinglePlayer` + `GhostOwnerIsLocal`. |
| `Mix/Settings/ScriptableObjects/MultiplayerServerSettings.cs` | asset | Connection timeout (20 s), server list, editor auto-run mode. |
| `Mix/Shooting/CheatShootingSystem.cs` | S (editor) | Self-damage cheat. |
| `Mix/Shooting/Jobs/LaserJob.cs` | C/S | Lag-compensated auto-aim target selection, energy. `AutoAimHitsCollector`. |
| `Mix/Shooting/Jobs/LaserVisualJob.cs` | C/L | Beam start/end and hit flag for VFX. |
| `Mix/Shooting/ShootingSystem.cs` | C/S | Schedules `LaserJob` in prediction. |
| `Mix/UI/Custom/CustomSelector.cs`, `CustomSlider.cs`, `CustomSliderInt.cs`, `CustomToggle.cs` | M | UI Toolkit custom controls. |
| `Mix/UI/MonoBehaviours/MainMenu/SceneController.cs` | static | `LoadGame`, `LoadMenu`, scene flags. |
| `Mix/UI/MonoBehaviours/Matchmaker/MatchMakingConnector.cs` | M | UGS login, matchmaking, session join, connection status; owns `ClientManager`. |
| `Mix/UI/MonoBehaviours/Matchmaker/MatchMakingUI.cs` | M | Multiplayer form UI, IP:port parsing, status label. |
| `Mix/UI/UIConstants.cs` | – | USS class names. |

## Gameplay / Server (`Unity.MegacityMetro.Server`)

| File | World | Purpose |
|---|---|---|
| `Server/Netcode/PlayerLeftEventComponent.cs` | S | `PlayerLeftEvent { PlayerId }`. |
| `Server/Netcode/PlayerLeftEventSystem.cs` | S (UNITY_SERVER) | Removes the player from the UGS session. |
| `Server/Netcode/ServerInGame.cs` | S | Tick rate, importance grid, listen, spawn a ghost per connection, update connection positions. |
| `Server/Netcode/VerifyPlayerDisconnectedSystem.cs` | S | Roster cleanup → `PlayerLeftEvent`. |
| `Server/Player/SetPlayerInfoSystem.cs` | S | Consumes `SetPlayerInfoRequest`: name, platform, UGS id, roster entry. |
| `Server/Shooting/Jobs/DamageJob.cs` | S | Applies laser damage, score, kills, attacker direction. |
| `Server/Shooting/ShootingServerSystem.cs` | S | Schedules `DamageJob`. |

## UGS (`Unity.Services.UGS.*`)

| File | Purpose |
|---|---|
| `UGS/Authentication/PlayerAuthentication.cs` | Anonymous sign-in with a hashed profile name; `PlayerId`. |
| `UGS/Authentication/PlayerProfile.cs` | `Name` + `UASId`. |
| `UGS/Client/ClientMatchmaker.cs` | Legacy Matchmaker ticket flow (unused by the session-based menu). |

## Utils (`Unity.MegacityMetro.Utils`, `Unity.MegacityMetro.Utils.Graphics`, `Unity.Netcode.Extensions`)

| File | Purpose |
|---|---|
| `Utils/Android/AndroidPermissionManager.cs` | Requests microphone permission. |
| `Utils/Graphics/Authoring/LayeredLitBaseColor3Authoring.cs` | Bakes the player colour material property. |
| `Utils/Graphics/Authoring/MultiMaterialObjectTagAuthoring.cs` | Tags multi-submesh objects for colouring. |
| `Utils/Graphics/Authoring/PlayerColorAuthoring.cs` | Tags the ship for per-player colour. |
| `Utils/Graphics/Components/LayeredLitBaseColor3.cs` | `MaterialProperty` colour component. |
| `Utils/Graphics/Components/MultiMaterialObjectTag.cs` | `MultiMaterialObjectTag`, `MultiMaterialUpdated`, `SetPlayerColorTag`. |
| `Utils/Graphics/Systems/PlayerColor.cs` | Colour palette by network id. |
| `Utils/Graphics/Systems/PlayerColorSystem.cs` | `SubmeshColorSystem`, `PlayerColorSystem`. |
| `Utils/Misc/DelayLineDopplerHack.cs` | Legacy audio doppler helper. |
| `Utils/Misc/MathUtilities.cs` | `GetSharpnessInterpolant`, `GetDampingInterpolant`, `SmoothFollow`, cone helpers, `ToEuler`. |
| `Utils/Misc/PannerComponent.cs` | Moves a light/transform by velocity. |
| `Utils/Misc/PhysicsUtilities.cs` | `ClosestRaycastCollisionWithIgnoredEntitiesCollector`, `IsCollidable`, `ProduceCollisionFilter`. |
| `Utils/Misc/PreventX86Compilation.cs` | Build preprocessor: error on 32-bit Windows. |
| `Utils/Misc/RateSettings.cs` | `tickRate 30`, `fixedRateRatio 1`, frame-rate control. |
| `Utils/Misc/ResourcePreloader.cs`, `ResourcePreloaderUtility.cs` | Keep listed assets resident. |
| `Utils/NetcodeExtensions/Authoring/BotAuthoring.cs` | `BotNameGenerator` name bank → `BotNameElement` buffer. |
| `Utils/NetcodeExtensions/Authoring/EnableConnectionMonitorAuthoring.cs` | Enables `ConnectionMonitorSystem`. |
| `Utils/NetcodeExtensions/Authoring/PlayerNameAuthoring.cs` | Adds `PlayerName` to the ship. |
| `Utils/NetcodeExtensions/Components/DeviceInformation.cs` | GPU/CPU/memory info structs (for `RequestPlayerStats`). |
| `Utils/NetcodeExtensions/Components/EnableConnectionMonitor.cs` | Tag + `ConnectionStateInfoRequest` RPC. |
| `Utils/NetcodeExtensions/Components/InitializedConnection.cs` | Tag: connection logged by the monitor. |
| `Utils/NetcodeExtensions/Components/PlayerName.cs` | `PlayerName`, `BotNameElement`, `PlayerUASID`, `PlayerConnectedElement`. |
| `Utils/NetcodeExtensions/Components/PlayerStats.cs` | Local stats for the netcode panel. |
| `Utils/NetcodeExtensions/Components/RequestPlayerStats.cs` | RPC + aspect for sending device stats (not sent anywhere today). |
| `Utils/NetcodeExtensions/Systems/ConnectionMonitorSystem.cs` | Server connect/disconnect logging and info RPCs. |
| `Utils/NetcodeExtensions/Systems/InitializeNetcodePanelStatsSystem.cs` | `NetCodePanelStats` group; adds `PlayerStats` to the local player. |
| `Utils/NetcodeExtensions/Systems/UpdateFPSData.cs` | FPS every 0.25 s. |
| `Utils/NetcodeExtensions/Systems/UpdateNetcodePanelStatsSystem.cs` | Pushes stats to `NetcodePanelStats` UI. |
| `Utils/NetcodeExtensions/Systems/UpdatePingData.cs` | RTT from `NetworkSnapshotAck`. |
| `Utils/NetcodeExtensions/Systems/UpdateSystemsRunningData.cs` | Systems-per-frame estimate. |
| `Utils/NetcodeExtensions/UI/NetcodePanelStats.cs` | Dev-build stats panel (toggle with `P`). |
| `Utils/Pooling/BasePool.cs` | Generic index-based pool. |
| `Utils/Pooling/Components/Components.cs` | `DynamicInstanceLinkCleanup` cleanup component. |
| `Utils/Pooling/Components/DynamicInstanceLinkAuthoring.cs` | `DynamicInstanceLink { PoolType, Linked }`. |
| `Utils/Pooling/GameObjectPool.cs` | Pool of prefab instances, `GetPooledElement` lookup. |
| `Utils/Pooling/PoolManager.cs` | Singleton owning all pools (`PoolType` per hull FX). |
| `Utils/Pooling/Systems/DynamicInstanceLinkSystem.cs` | Spawns/recycles pooled GameObjects for entities. |
| `Utils/QualitySettingsSelector/QualitySettingsObjectActivator.cs` | Enables children per quality level. |
| `Utils/QualitySettingsSelector/Systems/QualitySettingsSelectorSystem.cs` | Auto quality downgrade (`AUTO_QUALITY_SETTINGS`). |
| `Utils/SaveSystem/FileManager.cs` | Read/write files in `persistentDataPath`. |
| `Utils/SaveSystem/PersistentDataManager.cs` | Loads/saves `UserSettingsData` JSON; getters per settings group. |
| `Utils/SaveSystem/UserPreferencesSO.cs` | `UserSettingsData` (graphics, audio, controls) ScriptableObject defaults. |
| `Utils/UI/CursorUtils.cs` | Show/hide/lock cursor. |
| `Utils/UI/ModalWindow.cs` | Modal dialog singleton. |

## Other

| File | Purpose |
|---|---|
| `OrthographicShadows/OrthographicShadow.cs`, `OrthographicShadows/Editor/OrthographicShadowInspector.cs` | Top-down shadow capture and its inspector. |
| `NativePlugins/TrackpadAccessMac`, `NativePlugins/TrackpadAccessWin` | Native sources for the trackpad touch-count plugin used by `PluginLoader`. |
| `Assets/Art/Shaders/Shader Globals/*` | Shader global asmdefs (runtime + editor). |
| `Assets/Tutorials/*` | In-editor tutorial (IET) assets for UGS setup. |
