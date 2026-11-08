# Gameplay Systems

Everything that is not flight control or netcode plumbing: combat, health, score, visuals, HUD, camera glue,
bounds, traffic, blimps, lights, pooling, settings and services. Cross-references:
[flight-control.md](flight-control.md), [netcode.md](netcode.md), [source-reference.md](source-reference.md).

---

## 1. Combat: the laser

Files: `Mix/Shooting/ShootingSystem.cs`, `Mix/Shooting/Jobs/LaserJob.cs`, `Mix/Shooting/Jobs/LaserVisualJob.cs`,
`Server/Shooting/ShootingServerSystem.cs`, `Server/Shooting/Jobs/DamageJob.cs`,
`Components/Gameplay/Vehicle/VehicleLaser.cs`, `WeaponShotIgnoredEntity.cs`.

### Tuning (`PlayerVehicleControllerAuthoring`, "Laser" header → `VehicleLaser`)

| Field | Prefab | Meaning |
|---|---|---|
| `VehiclesPhysicsCategory` | layer mask 512 (`Vehicles`, layer 9) | Sphere-cast filter for targets. |
| `LaserObstructionsPhysicsCategory` | 513 (`Default` + `Vehicles`) | Ray filter for line-of-sight and beam end point. |
| `DamagePerSecond` | 50 | Server damage while `IsShooting` with a target. 2 s to kill a full-health ship. |
| `InitialEnergy` | 100 | Starting energy; max is hard-coded 100. |
| `ChargeSpeed` (`ChargerSpeed`) | 64 /s | Recharge while not firing (empty → full in ~1.6 s). |
| `DrainSpeed` | 16 /s | Drain while firing (full → empty in 6.25 s). |
| `AutoAimDegrees` | 10 | Half-angle of the aim cone, multiplied by the user's `AimAssistanceSensitivity` (default 2 → 20°). |
| `AutoAimMinRadius` | 4 | Targets within this lateral distance of the beam are accepted regardless of cone angle. |
| `LaserRange` | 150 | Cast length; also the camera aim-point distance. |
| `LocalLaserStartPoint` | from `ShipVisualsAuthoring.LaserRoot` | Set at runtime by `RandomShipVisualsSystem`. |

### Per tick (`LaserJob`, predicted on owner and server)

1. Reset `DetectedTarget`. Dead owners cannot shoot.
2. Fetch the lag-compensated collision world for this ship (see netcode.md §9).
3. `SphereCastCustom` from the muzzle along the nose, radius = `tan(cone) * Range`, with `AutoAimHitsCollector`:
   ignore `WeaponShotIgnoredEntity` entries (the ship itself), keep only the closest, skip non-collidable
   materials, require a `VehicleHealth` that is alive and `Immunity.Counter <= 0`, reject targets behind the nose
   (dot < cos 70°) or outside the cone (unless within `AutoAimMinRadius`), then ray-cast for obstructions.
4. `IsShooting = input.Shoot && (Energy > 0 || alive)`; drain or recharge energy.

### Damage (`DamageJob`, server only, single-threaded `Schedule`)

For each shooter with `IsShooting` and a valid `DetectedTarget` that is not immune: subtract `DamagePerSecond * dt`,
add the same amount to the shooter's `PlayerScore.Value`, write the victim's `LookAtEnemyDegrees` (horizontal angle
to the attacker, used by the HUD damage pointer), record `KillerName` / `KilledPlayer`, and on reaching 0 HP increment
`Kills`, clamp HP to 0 and force the victim's laser off.

### Presentation (`LaserVisualJob` scheduled by `VehicleFXSystem`)

Computes the beam start/end (`VFXLaserStartNode`/`VFXLaserEndNode`): to the detected target's position, or to the
first obstruction hit along the nose, or to `Range`. `ShowHitVFX` when something is hit. `VehicleFXSystem` drives the
muzzle, beam (`Length` VFX float), hit (`HitEffect` bool), beam/hit audio.

---

## 2. Health, death, respawn, immunity, score

Files: `Mix/Player/PlayerVehicleControlSystem.cs` (`VehicleHealthAndImmunitySystem`), `Mix/Player/Jobs/PlayerVehicleJobs.cs`
(`HealthAndImmunityJob`), components `VehicleHealth`, `Immunity`, `PlayerScore`, `PlayerScoreAspect`.

Timeline for a death (server authoritative):

```
HP hits 0 (DamageJob)        -> IsDead = 1, _deathTimer = 5 (HealthAndImmunityJob, server)
                              -> MoveJob: controls disabled, fake gravity 20 u/s^2 (predicted once IsDead replicates)
                              -> client CheckVehicleLife: HUD.ShowDeathMessage(killer), 5 s countdown, crosshair hidden
                              -> VehicleFXSystem: explosion + death SFX + electric arcs
after 5 s                    -> HP = 100, IsDead = 0, Immunity.Counter = Immunity.Duration (4.5 s on prefab)
                              -> VehicleFXSystem plays the shield VFX with Lifetime = Duration
                              -> LaserJob/DamageJob ignore immune targets
```

Score: `PlayerScore.Value` accumulates damage dealt; the leaderboard shows `Value / 10` and sorts by score, then
kills, then network id (`SortableScoreComparer`). `UpdateScoreRankingSystem` also marks the top player's badge on
name tags. `UpdateNotification` shows "YOU DESTROYED <name>" when the local `Kills` increases.

Editor cheat: `CheatShootingSystem` (server side, first full prediction of each tick) applies 1 HP of self-damage
per tick while key `1` is held.

---

## 3. Ship visuals, colours and pooled FX

- `ShipRandomVisualsAuthoring` → `ShipRandomVisuals` buffer of hull prefabs on the `Visuals` child;
  `ShipVisualsAuthoring` on each hull → `ShipVisuals { FXEntity, LocalLaserStartPoint }`.
- `RandomShipVisualsSystem` (all worlds): picks a hull seeded by the player-name hash, parents it, links it into the
  root's `LinkedEntityGroup`, copies laser start point and FX entity to the root.
- `PlayerColorAuthoring` / `MultiMaterialObjectTagAuthoring` / `LayeredLitBaseColor3`: `SubmeshColorSystem` adds the
  material-property component to child submeshes; `PlayerColorSystem` assigns a colour per `GhostOwner.NetworkId`
  (`PlayerColor` palette helper).
- FX GameObjects are pooled: `PoolManager` (resource prefab, `PoolType` per hull FX), `GameObjectPool`,
  `DynamicInstanceLinkAuthoring` → `DynamicInstanceLink`; `DynamicInstanceLinkSystem` spawns a pooled instance when
  the linked entity appears and recycles it on destroy/disable using the `DynamicInstanceLinkCleanup` cleanup
  component. `VehicleFXSystem` fetches the instance each frame with `GameObjectPool.GetPooledElement` and positions
  the `ShipFXManager` (VFX graphs + audio sources) on the ship.

---

## 4. Camera

`HybridCameraManager` (MonoBehaviour on the `HybridObjects` rig) is driven by `PlayerCameraTargetUpdater`
(client/local, after `TransformSystemGroup`) for the ship with `GhostOwnerIsLocal`: first frame `PlaceCamera`
(snap), then every frame `SetPlayerTargetPosition(pos + up * VerticalOffset)`,
`UpdateAimCameraTargetPosition(laserEndIgnoringTarget)`, `CameraUpdate(dt)`. It also updates the crosshair.
`StartShaking` is a stub. Post-processing helpers (`DeadScreenFX`, `PostProcessingGlitch`, bloom and vignette
modifiers) animate URP volume overrides. Details and tuning in flight-control.md §4.5 and §6.

---

## 5. HUD, menus and settings

| Piece | Files | Notes |
|---|---|---|
| HUD | `Client/UI/MonoBehaviours/HUD/HUD.cs`, `Crosshair.cs`, `LaserBar.cs`, `MobileControls.cs`; systems `UpdateLifeBar`, `UpdateLaser`, `UpdateNotification`, `CheckVehicleLife`, `UpdateMessageNearBoundsSystem` | Health bar + damage pointer (`LookAtEnemyDegrees`), death and bounds messages, energy bar, notifications. Hidden elements in single player use the `hide-in-single-player` USS class. |
| HUD hotkeys | `GameInput` `HUD` map, wired through a `PlayerInput` component in `Assets/Prefabs/UI/GameUI.prefab` (Unity Events) | `P` netcode panel, `Tab` leaderboard, `V` Vivox mute. No C# subscribes directly. |
| Leaderboard | `UILeaderboard.cs`, `UpdatePlayerRankSystem.cs`, `PlayerScoreAspect` | Full/collapsed modes. |
| Name tags | `PlayerInfoController.cs`, `PlayerInfoRef.cs`, `PlayerInfoItemSettings` asset, systems `InitializePlayerNameTag`, `UpdatePlayerNameTag` | World-to-panel projection, distance scaling, icon mode beyond `MinDistanceToShowPlayerInfo`, top-player badge. |
| Main menu | `MainMenu.cs`, `MainMenuGameMode.cs`, `MultiplayerMenu.cs`, `SessionBrowser.cs`, `MatchMakingUI.cs`, `MatchMakingLoadingBar.cs`, `CustomFocusRing.cs` | Single Player / Multiplayer (Matchmaker or Connect with session list) / Settings / Quit. Supports automatic matchmaking for headless thin-client runs. |
| Settings | `UIGameSettings.cs`, `UISettingsTab.cs`, `UIGraphicsSettings.cs`, `UIAudioSettings.cs`, `UIControlSettings.cs`, `UIScreenResolution.cs`, `GameSettingsOptionsMenu.cs` (in-game ESC menu) | Saved through `PersistentDataManager` to `user_preferences.json`; `UserPreferencesSO` in `Resources/UserPreferences` holds defaults. `UpdateControlSettingsSystem` and `CreateAudioSettingsSystem` bridge to ECS. |
| Misc UI | `ModalWindow.cs`, `LoadingScreen.cs` + `StartingGameSystem`, `Notification.cs`, `TutorialScreen.cs`, `GameVersion.cs`, custom controls in `Mix/UI/Custom` | – |
| UI input | `InputReader.cs` (raises `UIEvents`), `AssignCameraInput.cs` | – |

---

## 6. Level bounds

`LevelBoundsContainerAuthoring` bakes a prefab reference; `InitBoundsSystem` (client/local) instantiates the bounds
prefab at the container position once a local player exists and adds `PlayerLocationBounds` to that player.
`UpdateBoundsSystem` (fixed step) computes `IsInside` via `Collider.CalculateDistance` against the bounds collider
(`LevelBounds.MaxDistance`). `UpdateMessageNearBoundsSystem` toggles the HUD message. Purely informational.

---

## 7. Traffic (NPC vehicles)

Authoring: `Path` (+ `PathNode`, `RoadSettings`) draws and bakes splines (`TrafficConversionRule` →
`TrafficPathBakingSystem` → `RoadPoint` buffers and `RoadOffset`), `TrafficConfigAuthoring` → `TrafficConfig`,
`TrafficEnabled`, one `VehiclePrefabRef` entity per prefab; `TrafficVehicleAuthoring` tags vehicle prefabs.
Runtime (client/local only): `TrafficSetupSystem` builds `NativeSpline` roads and spawns vehicles along them
(`VehiclePathing`), `TrafficSystem.TrafficJob` advances `SplinePos` by `GlobalCarSpeed * dt`, evaluates the spline and
writes `LocalToWorld` directly with lane offset and banking. Traffic is **not simulated on the server** and is not a
network object; players collide with it only through the client's own physics world, so traffic contacts are not
authoritative. `Constants` in `TrafficComponents.cs` are legacy Megacity values; `CatmullRom` is a helper.

---

## 8. Blimps, lights, quality

- `BlimpAuthoring` → `BlimpComponent`; `BlimpSystem` moves each blimp on a circle and faces it along the path.
- `GeneralSettingsAuthoring` → `GeneralSettings` (lights layer 3 `Lights`, `MaxLightsDetectionRange 500`,
  `MaxLights 7`, fade 1.3 s); `LightsManagementSystem` (simulation group, last) keeps only the N closest point
  lights enabled around the camera/player using a physics overlap against a sphere collider per light.
- `QualitySettingsObjectActivator` toggles children by quality level; `QualitySettingsSystem` (behind
  `AUTO_QUALITY_SETTINGS`) lowers quality when FPS drops below 60.
- `OrthographicShadow` + editor inspector: baked top-down shadow capture for the city.
- Shader globals live in `Assets/Art/Shaders/Shader Globals` (own asmdefs).

---

## 9. Audio

`AudioMaster` holds mixer groups and traffic-audio limits (`maxDistance 90`, `maxVehicles 16`,
`closestEmitterPerClipCount 3`); `AudioSystemSettings` is the ECS mirror. `DelayLineDopplerHack` and
`PannerComponent` are legacy Megacity audio helpers. Engine/laser/death sounds are on `ShipFXManager` and driven by
`VehicleFXSystem`. Android device volume is relayed through `AudioMaster.OnReceiveDeviceVolume`.

---

## 10. Loading and scene flow

`SceneController` (static): `LoadGame` → `Main` async, `LoadMenu` → `Menu` and sets `IsReturningToMainMenu`.
`LoadingScreenSystem` counts `SceneSectionData` entities with `RequestSceneLoaded` and how many are loaded into
`GameLoadInfo`; `LoadingScreen` renders the bar; `StartingGameSystem` hides it when sections, camera and local player
are ready. `PersistentObject` marks GameObjects that survive scene loads. `AutonInitMainSceneInEditor` applies the
editor auto-run game mode to `PlayerInfoController` when the editor starts directly in `Main`.
`ConfigureLocalWorldGUIDSystem` (editor) points the local world's `SceneSystem` at the client build configuration
so subscenes load the client bake.

---

## 11. Platform helpers

`AndroidPermissionManager` (microphone permission), `PluginLoader` (trackpad native plugin, macOS only in
practice), `PreventX86BuildProcessor` (errors on 32-bit Windows builds), `ResourcePreloader` /
`ResourcePreloaderUtility` (keeps textures resident), `CursorUtils`, `FileManager` (JSON under
`Application.persistentDataPath`).
