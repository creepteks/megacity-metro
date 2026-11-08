# Flight Control Reference and Customization Plan

The ship's handling is the first thing a player feels. This document explains **every variable** that shapes it,
the exact math that consumes those variables, the hidden constants, how the result travels through netcode, and
a concrete plan for turning the single hard-coded model into a set of selectable flight styles.

Values quoted as "prefab" are the tuned values on `Assets/Prefabs/Vehicles/VehicleControlMultiplayer.prefab`
(the single-player prefab is a variant of it with no flight overrides, so both ships fly identically). Values
quoted as "code default" are the C# field initializers, which only matter if you add the component to a new prefab.

---

## 1. The model in one paragraph

The ship is a **yaw/pitch/roll Euler controller on top of a free-velocity rigid body**. Look input integrates pitch
(clamped to ±85°) and yaw (around world up) as radians. Thrust input adds acceleration along the current nose
direction to `PhysicsVelocity.Linear`, which is otherwise a free vector decayed by linear damping (so the ship drifts
through turns). Roll is cosmetic: an automatic bank that accumulates with yaw rate, plus manual roll, both of which
relax back to level. Rotation is written directly from the state each tick (it is kinematic, not solved by physics);
position comes from physics integration. The same job runs on the server and in client prediction.

---

## 2. File map

| Concern | File |
|---|---|
| Tuning values (authoring) | `Assets/Scripts/Authoring/Player/PlayerVehicleControllerAuthoring.cs` |
| Runtime settings component | `Assets/Scripts/Components/Gameplay/Vehicle/PlayerVehicleComponents.cs` (`PlayerVehicleSettings`) |
| Replicated flight state | `Assets/Scripts/Components/Gameplay/Vehicle/VehicleMovementState.cs` |
| Per-tick command | `Assets/Scripts/Components/Gameplay/Player/Input/PlayerVehicleInput.cs` |
| Player control settings | `Assets/Scripts/Components/Gameplay/Player/Input/ControlSettings.cs`, `Assets/Scripts/Authoring/Settings/ControlSettingsAuthoring.cs` |
| Input sampling (desktop + mobile) | `Assets/Scripts/Gameplay/Client/Player/PlayerVehicleInputSystem.cs` |
| Input write job | `Assets/Scripts/Gameplay/Mix/Player/Jobs/PlayerVehicleInputJob.cs` |
| Movement math | `Assets/Scripts/Gameplay/Mix/Player/Jobs/PlayerVehicleJobs.cs` (`MoveJob`, `ReconstructVehicleRotationJob`, `InterpolatedVehicleRotationJob`, `ApplyVehicleRotationAsVelocityJob`) |
| Movement scheduling | `Assets/Scripts/Gameplay/Mix/Player/PlayerVehicleControlSystem.cs`, `Assets/Scripts/Gameplay/Mix/Player/ApplyVehicleRotationAsVelocitySystem.cs` |
| Collision response | `Assets/Scripts/Gameplay/Mix/Player/PlayerVehicleCollisionSystem.cs`, `.../Jobs/PlayerVehicleCollisionJob.cs` |
| Settings UI and persistence | `Assets/Scripts/Gameplay/Client/UI/MonoBehaviours/Settings/UIControlSettings.cs`, `Assets/Scripts/Gameplay/Client/Player/UpdateControlSettingsSystem.cs`, `Assets/Scripts/Utils/SaveSystem/*` |
| Input actions asset | `Assets/Settings/Input/GameInput.inputactions` (generated `Assets/Scripts/Common/Input/GameInput.cs`) |
| Trackpad detection | `Assets/Scripts/Gameplay/Client/Player/PluginLoader.cs`, `NativePlugins/TrackpadAccess*` |
| Camera | `Assets/Scripts/Gameplay/Client/Camera/MonoBehaviours/HybridCameraManager.cs`, `Assets/Scripts/Gameplay/Client/Camera/PlayerCameraTargetUpdater.cs`, `Assets/Scripts/Components/Camera/PlayerCameraTarget.cs` |
| Math helpers | `Assets/Scripts/Utils/Misc/MathUtilities.cs` (`GetSharpnessInterpolant`, `SmoothFollow`, `ToEuler`) |
| Feedback (engine audio, FX) | `Assets/Scripts/Gameplay/Client/Player/VehicleFXSystem.cs` |

---

## 3. Data flow

```
 Mouse / keyboard / gamepad / touch
        |  GameInput (Input System asset; LookDelta has ScaleVector2(0.05))
        v
 PlayerVehicleInputSystemBase            [client, GhostInputSystemGroup, once per frame]
   - applies ControlSettings (sensitivity, invert, trackpad, acceleration range)
   - mouse delta / frame dt  -> "look velocity";  keys/sticks * 100 -> look velocity
        v
 PlayerVehicleInputJob                   flips pitch if PlayerVehicleSettings.InvertPitch, writes PlayerVehicleInput
        v
 PlayerVehicleInput (IInputComponentData) -> Netcode copies it into the command buffer for the current tick
        v                                    and sends it to the server (redundantly, several ticks per packet)
 MoveJob                                 [server + client prediction, PredictedFixedStep / BeforePhysics, dt = 1/30]
   - smooth look velocity, integrate pitch & yaw, clamp pitch
   - thrust along nose into PhysicsVelocity.Linear, clamp speed
   - auto-bank + manual roll into VehicleMovementState
        v
 ApplyVehicleRotationAsVelocitySystem    angular velocity that reaches the new rotation in one step
 PhysicsSimulationGroup                  integrates position, applies linear damping (Rigidbody drag = 1)
 ReconstructVehicleRotationSystem        (next tick, first) re-asserts rotation from VehicleMovementState
        v
 LocalTransform                          -> Netcode replicates Position (variant) + VehicleMovementState angles
        v
 Remote clients: InterpolatedVehicleRotationSystem rebuilds rotation from interpolated angles
 Local client:   PlayerCameraTargetUpdater -> HybridCameraManager; VehicleFXSystem -> engine pitch, FX
```

---

## 4. Complete variable reference

### 4.1 Flight tuning (`PlayerVehicleControllerAuthoring` → `PlayerVehicleSettings`)

| Field | Code default | Prefab | Unit | Used in | Effect on feel |
|---|---|---|---|---|---|
| `Acceleration` | 5 | **100** | units/s² | `MoveJob` thrust when `input.Acceleration ≥ 0` | Throttle punch. With damping 1 the ship reaches 63 % of top speed in 1 s. |
| `Deceleration` | 10 | **75** | units/s² | `MoveJob` when `input.Acceleration < 0` | Reverse-thrust strength (braking). Note it is a separate *acceleration*, not a drag. |
| `MaxSpeed` | 50 | **100** | units/s | `MoveJob` clamp after thrust | Hard speed cap. Because damping runs after the clamp, sustained top speed is ≈ 96.7 (see §5.3). |
| `SteeringSpeed` | 5 | **1** | multiplier | `MoveJob` (`rotationSpeed = 0.01 * SteeringSpeed`) | Global look-to-rotation gain. 1 → full stick = 1 rad/s ≈ 57°/s. |
| `LookVelocitySharpness` | 10 | **15** | 1/s | `MoveJob` exponential smoothing of look velocity | Input lag/smoothness. Time constant 1/15 ≈ 67 ms. Lower = floatier aim, higher = twitchier. |
| `InvertPitch` | false | false | bool | `PlayerVehicleInputJob` | Flips pitch input at the job level (in addition to the user's `InverseLookVertical`). |
| `MaxPitchAngle` | 89 | **85** | degrees | `MoveJob` pitch clamp | Prevents loops/gimbal flip. The controller cannot fly inverted. |
| `RollAccumulationSpeed` | 0.03 | 0.03 | per **tick** | `MoveJob` auto-bank accumulation | How fast the ship banks into a turn. Not dt-scaled (see pitfalls). |
| `ManualRollSpeed` | 5 | 5 | rad/s | `MoveJob` manual roll (Q/E, shoulders) | 5 rad/s = 286°/s, a full barrel roll in 1.26 s. |
| `RollRestitutionSpeed` | 2 | 2 | 1/s | `MoveJob` relax of both manual roll and bank | Time constant 0.5 s back to level when the roll key is released or the turn stops. |
| `YawSpeedForMaxRoll` | 1.8 | 1.8 | rad/s | `MoveJob` bank restitution scaling | At or above this yaw rate the bank is *held*; below it the bank relaxes proportionally. 0 disables bank restitution. |
| `TargetFollowDamping` | 2 | 2 | – | **unused** | Baked into the component but no system reads it (camera uses `HybridCameraManager` fields instead). |
| `TargetSqLerpThreshold` | 2500 | 2500 | – | **unused** | Same. |
| `VehicleFX` | – | runtime | Entity | `VehicleFXSystem` | Set by `RandomShipVisualsSystem` from the chosen visual's `ShipVisuals.FXEntity`. Not a tuning value. |

Hidden constants in `MoveJob` that are effectively tuning values:

| Constant | Value | Meaning |
|---|---|---|
| `BaseSteeringSpeedMultiplier` | 0.01 | Converts look velocity to rad/s. All sensitivity numbers are relative to this. |
| Death gravity | `-up * 20 * dt` | Dead ships fall at 20 units/s² (the physics gravity factor is 0, see §4.2). |
| Max bank | `math.PI * 0.5` | Auto-bank maps `RollAccumulationFromTurn ∈ [-1,1]` to ±90°. |
| Roll input deadzone | `0.001` | Below this the manual roll relaxes. |
| Bank turn gain | `0.5` | `turnAmount = -(1 - dot) * 0.5 * |yawSpeed|`. |

### 4.2 Physics body (legacy `Rigidbody` + `CapsuleCollider` on the prefab, baked by Unity Physics)

| Setting | Value | Baked into | Effect |
|---|---|---|---|
| Mass | 1 | `PhysicsMass` | Collision response against traffic/buildings. |
| Drag | **1** | `PhysicsDamping.Linear = 1` | The only "friction" in the model: velocity is multiplied by `(1 - dt)` each step (≈ 3.3 % per tick, half-life 0.69 s). Defines drift length and terminal speed `a / d`. |
| Angular drag | 0 | `PhysicsDamping.Angular = 0` | Irrelevant: rotation is re-asserted every tick. |
| Use Gravity | off | `PhysicsGravityFactor = 0` | Ship hovers; the fake death gravity above is the only fall. |
| Collider | Capsule r = 1.6, h = 6.33 along Z, centre (0,0,-0.1) | `PhysicsCollider` | Hull for building/traffic contact and for being hit by lasers. |
| Layer | 9 `Vehicles` | `CollisionFilter.BelongsTo = 1<<9` | Collision matrix is all-on, so vehicles collide with everything. |
| Custom tags | none | `PhysicsCustomTags` absent (tags = 0) | See `PlayerVehicleCollisionJob` note in §5.6. |
| Physics step (`Common.unity`) | Unity Physics, gravity -9.81, **1 solver iteration**, multithreaded | `PhysicsStep` | Soft contacts; cheap for 150 bodies. |
| Fixed step | 1/30 s | `RateSettings.tickRate = 30`, `TimeManager` fixed timestep 0.0333 | All movement math runs at 30 Hz regardless of frame rate. |

### 4.3 Player control settings (`ControlSettingsAuthoring` → `ControlSettings` singleton, baked from `Common.unity`)

| Field | Authoring value | Effective at runtime | Used in |
|---|---|---|---|
| `TrackpadSensitivity` | Win 1 (default, not set in scene) / **Mac 4** | multiplies mouse look only while `PluginLoader.TouchesCount() > 0` | `PlayerVehicleInputSystemBase` |
| `MouseSensitivity` | 1 | **2.0** after the settings UI syncs the saved `UserPreferences` (`MouseSensitivity: 2` → slider 200 %) | mouse look |
| `AimAssistanceSensitivity` | 1 | **2.0** (saved default) → auto-aim cone `AutoAimDegrees (10) * 2 = 20°` | sent per tick in `PlayerVehicleInput.AimAssistSensitivity` |
| `AccelerationRange` | (-1, 1) | clamps the throttle axis | thrust input |
| `InverseLookHorizontal` / `InverseLookVertical` | false | user toggles in the Controls tab | sign flips on look input |

Persistence: `UIControlSettings` → `PersistentDataManager.SaveControlSettings` → `user_preferences.json`
(`FileManager`). On start the saved values are pushed into the singleton by `UpdateControlSettingsSystem` when
`UIControlSettings.ShouldUpdate` is true (set once in `Awake` and on every Save).

### 4.4 Input bindings (`Gameplay` action map)

| Action | Type | Bindings | Becomes |
|---|---|---|---|
| `LookDelta` | Vector2 | `<Mouse>/delta` with `ScaleVector2(0.05, 0.05)` | `LookVelocity = (-dy, dx) * MouseSensitivity * trackpad / frameDt` |
| `LookConst` | Vector2 | Arrows, A/D (left/right), gamepad left stick (all), right stick left/right | `LookVelocity = (-y, x) * 100` (constant 1 rad/s at full deflection) |
| `Move` | Axis | W (+) / S (−), gamepad South (+) / North (−), right stick up/down | `Acceleration ∈ [-1, 1]` |
| `Roll` | Axis | E (+) / Q (−), right/left shoulder | `Roll ∈ [-1, 1]` |
| `Fire` | Button | Space, left mouse, right trigger | `Shoot` |
| `Cheat_1` | Button | `1` (dev builds) | `Cheat_1` (self damage) |
| Mobile | – | `VirtualJoystick.Delta * 100` for look, `MobileControls.Speed` slider for thrust, fire zone | same fields |

`LookVelocity.x` is **pitch** input (vertical), `.y` is **yaw** input (horizontal). Mouse look wins over keyboard
look when the mouse moved this frame.

### 4.5 Camera (`HybridCameraManager` prefab + `PlayerCameraTargetAuthoring`)

| Field | Prefab | Meaning |
|---|---|---|
| `FollowDistance` | **10** | Camera sits 10 units behind the target along the aim direction. |
| `FollowSharpness` | **18** | `MathUtilities.SmoothFollow` sharpness (time constant 56 ms); includes target-velocity compensation so the camera does not lag at speed. |
| `LookAtSharpness` | **15** | Slerp sharpness toward the aim point (67 ms). |
| `PlayerCameraTarget.VerticalOffset` | **2** | Target = ship position + 2 up. |
| Aim point | laser end point *ignoring the detected target* | `laser.CalculateLaserEndPoint(..., skipDetectedTarget: true)` = nose + 150 forward. The camera therefore looks where the nose points, which is what makes turns feel "led" by the camera. |

`PlayerCameraTarget` is `GhostPrefabType.PredictedClient`, so only the locally owned ship has it.

### 4.6 Netcode-side feel parameters

| Parameter | Where | Value | Feel impact |
|---|---|---|---|
| Tick rate | `RateSettings.tickRate` | 30 Hz | Movement resolution; input sampling cadence. |
| Position quantization | `MegacityMetroLocalTransform` variant | 1000 (mm) | Imperceptible. |
| Position smoothing | variant `InterpolateAndExtrapolate`, `MaxSmoothingDistance = 20` | – | Remote ships extrapolate between snapshots; snap if > 20 units off. |
| Prediction error smoothing | `MegacityMetroSmoothingAction` | correct 0.5 units/frame when error ∈ (0.5, 20), snap beyond | How mispredictions of *your* ship look: a slow slide instead of a pop. |
| Angle replication | `VehicleMovementState` fields, quantization 1000, `Interpolate` | – | Remote ship orientation is rebuilt from 3 angles, not a quaternion. |

---

## 5. The math, step by step (per 30 Hz tick, `dt = 1/30`)

### 5.1 Look smoothing

```
k      = 1 - exp(-LookVelocitySharpness * dt)          // 0.393 at 15 /s and 30 Hz
look   = lerp(state.LastLookVelocity, input.LookVelocity, k)
state.LastLookVelocity = look                           // replicated so prediction can roll back
```

### 5.2 Pitch and yaw

```
rotSpeed = 0.01 * SteeringSpeed                          // rad/s per unit of look velocity
pitch    = clamp(pitch + look.x * rotSpeed * dt, -MaxPitch, +MaxPitch)
yaw      = yaw + look.y * rotSpeed * dt                  // unbounded, never wrapped
rotation = quaternion.Euler(pitch, yaw, roll)            // Unity.Mathematics default order ZXY:
                                                          // roll about body forward, then pitch, then yaw about world up
forward  = rotation * (0,0,1)
```

Worked numbers at the shipped tuning:

- Full keyboard/stick deflection: `100 * 0.01 = 1 rad/s` (57°/s). Pitching from level to the 85° limit takes 1.5 s.
- Mouse: one pixel = `1 * 0.05 * 2.0 (sens) * 0.01 = 0.001 rad = 0.057°`. A 180° turn is ≈ 3150 px.
- Because yaw is always about world up and pitch is clamped, the ship can never roll its *steering* frame: rolling
  with Q/E does not change what "pull up" does. This is an arcade "airplane without loops" model.

### 5.3 Thrust and speed

```
a        = input.Acceleration >= 0 ? Acceleration : Deceleration
v       += forward * input.Acceleration * a * dt
if |v| > MaxSpeed: v = normalize(v) * MaxSpeed
-- physics step --
v       *= clamp(1 - Damping.Linear * dt, 0, 1)          // Rigidbody drag 1 -> *0.9667 per tick
x       += v * dt
```

- Velocity is a free vector: turning the nose does not turn the velocity. Only new thrust (projected along the
  new nose) and damping change it. This is the source of the ship's "drift through corners" character.
- Acceleration from rest: `v(t) = (a/d)(1 - e^{-t})` → 63 units/s after 1 s, 95 after 3 s.
- Top speed: the clamp to 100 happens *before* damping, so the fixed point is `100 * (1 - dt) ≈ 96.7`.
- Coast to stop: speed halves every 0.69 s; from top speed to under 5 units/s takes about 3 s.
- Reverse: holding S applies 75 units/s² backwards along the nose, so it is both brake and reverse thrust.

### 5.4 Death

When `VehicleHealth.IsDead == 1` the whole control block is skipped and `v += -up * 20 * dt`. The ship keeps its
last velocity and falls for the 5 s death timer (server-side `HealthAndImmunityJob`), then is revived in place.

### 5.5 Roll

```
yawSpeed = yawDelta / dt
if yawSpeed != 0:
    rightWorld   = rotationWithoutRoll * (1,0,0) * yawSpeed      // direction the nose is sweeping
    up           = rotation * (0,1,0)
    dot          = dot(normalize(rightWorld), up)                 // 0 when level, ±1 when banked 90°
    turnAmount   = -(1 - dot) * 0.5 * |yawSpeed|
    turnDir      = yawSpeed < 0 ? +1 : -1
    bank         = clamp(bank + turnAmount * turnDir * RollAccumulationSpeed, -1, 1)

baseRoll += -input.Roll * ManualRollSpeed * dt;  wrap to [-pi, pi]
roll      = baseRoll + lerp(0, pi/2, -bank)

if |input.Roll| < 0.001:  baseRoll = lerp(baseRoll, 0, 1 - exp(-RollRestitutionSpeed * dt))
if YawSpeedForMaxRoll != 0:
    restitution = lerp(RollRestitutionSpeed, 0, saturate(|yawSpeed| / YawSpeedForMaxRoll))
    bank        = lerp(bank, 0, 1 - exp(-restitution * dt))
```

- Auto-bank accumulates `0.5 * |yawSpeed| * 0.03` per tick while level, so a steady 1 rad/s turn reaches the full
  90° bank after ≈ 67 ticks (2.2 s). The `(1 - dot)` factor reduces accumulation as the ship approaches 90°.
- `RollAccumulationSpeed` is applied per tick, not per second. It is deterministic because the tick is fixed, but
  it will change feel if you change the tick rate.
- Manual roll (`baseRoll`) wraps at ±180°, so continuing to roll past inverted comes back to level the short way.

### 5.6 Applying rotation and collisions

- `ReconstructVehicleRotationSystem` (first in `BeforePhysicsSystemGroup`) writes
  `LocalTransform.Rotation = Euler(pitch, yaw, roll)` from the *previous* state.
- `MoveJob` updates the state.
- `ApplyVehicleRotationAsVelocitySystem` (last) sets `PhysicsVelocity.Angular` to the value that rotates the body
  from the current rotation to the new target in exactly one step, so the physics solver sees realistic angular
  motion in contacts. Any angular response from a collision is discarded on the next tick's reconstruct.
- `PlayerVehicleCollisionSystem` (after physics) is meant to cut throttle on impact:
  `v *= 1 - |dot(contactNormal, normalize(v))|`, i.e. a head-on hit kills speed, a glancing hit keeps it. It only
  acts on bodies with **custom physics tag bit 0** (`PlayerVehicle` in `CustomPhysicsBodyTagNames`). The shipped
  prefab is baked from a legacy `Rigidbody`, which produces no `PhysicsCustomTags`, so this branch does not fire
  and collisions are resolved by the Unity Physics solver alone (mass 1, default friction, 1 iteration). To get the
  designed behaviour add a `PhysicsBodyAuthoring` with the `PlayerVehicle` tag (or add `PhysicsCustomTags` in the
  baker). See [known-issues.md](known-issues.md).

### 5.7 Level bounds

`UpdateBoundsSystem` computes `PlayerLocationBounds.IsInside` from the signed distance to the baked bounds
collider; `UpdateMessageNearBoundsSystem` shows "You still have unfinished business here". There is no physical
push-back or damage: bounds are informational only.

---

## 6. Presentation coupling (what the player actually perceives)

| Channel | Source | Note |
|---|---|---|
| Camera follow/lead | `HybridCameraManager.CameraUpdate` each frame from `PlayerCameraTargetUpdater` | Position via `SmoothFollow` (velocity-compensated exponential), rotation via slerp toward the nose aim point. Both sharpnesses are per-frame `dt` based (frame-rate independent). |
| Crosshair | `HUD.Crosshair.UpdatePosition(laserEnd)` | Lerps 20 % per frame toward the projected aim point; snaps the aim icon to a detected target. |
| Engine pitch | `VehicleFXSystem`: `pitch = lerp(1, 2, saturate(speed / 30))` | Saturates at 30 units/s, less than a third of top speed. A cheap, high-impact knob. |
| Speed feedback | none other (no FOV change, no speed lines, no motion blur scaling) | Candidates for the profile system below. |
| Roll | visual only | Affects nothing in the simulation except the auto-bank `dot` term. |
| Damage sparks/smoke, shield, explosion | `VehicleFXSystem` thresholds 50 / 20 HP | Pooled GameObject FX follow the entity transform. |

---

## 7. Netcode interaction (what you must keep in mind when changing the model)

- `MoveJob` runs on the server and, for the owner, inside client prediction. It must stay deterministic given
  (`PlayerVehicleInput`, `PlayerVehicleSettings`, `VehicleMovementState`, `PhysicsVelocity`, `VehicleHealth`).
  All of its mutable inputs are either commands or replicated ghost fields, which is why prediction rollback works.
- `PlayerVehicleSettings` is **not replicated**. Both sides get it from baking the same prefab. If you make it
  change at runtime you must either replicate it (ghost fields) or derive it on both sides from a replicated index.
- Remote ships are **interpolated**, not predicted. They receive `Position` and the six `VehicleMovementState`
  floats; `InterpolatedVehicleRotationSystem` rebuilds their rotation. `PhysicsVelocity.Linear` is sent to the owner
  only (`OnlyPredictedClients`), so remote ships have no velocity on clients (extrapolation uses position history).
- Input is sampled once per frame but consumed once per tick. At frame rates above 30 Hz, intermediate mouse deltas
  are dropped and the surviving one is scaled up by `tickDt / frameDt`; the average is right but mouse aim has
  sampling noise. Accumulating mouse delta between ticks is the standard fix (see §8 step 5).
- Health/death is server-only (`VehicleHealthAndImmunitySystem` has `ServerSimulation` filter). The client learns
  `IsDead` from snapshots, so the fall starts one RTT late and is then re-predicted from the corrected state.
- Shooting needs `NetworkTime`, so there is **no shooting in single player**; flight is identical, combat is absent.

---

## 8. Plan: making flight customizable (multiple flight styles)

Goal: ship several selectable "flight styles" (for example *Arcade drift* = today's model, *Space sim* with
velocity-aligned nose, *Racer* with speed floor and strong auto-bank, *Hover* with strafe), switchable per ship
or per player, deterministic across server and clients, and tunable by designers without code.

### Step 1 – Make the profile a data asset

Create `FlightProfile : ScriptableObject` holding every field of §4.1 plus the hidden constants and the physics
values that belong to the model (`LinearDamping`, `Mass`), and the presentation knobs (§6). Bake all profiles into a
single `BlobAssetReference<FlightProfileTable>` singleton (one `BlobArray<FlightProfileBlob>`), and give each ship a
tiny replicated selector:

```csharp
public struct FlightProfileBlob
{
    public float Acceleration, Deceleration, MaxSpeed, MinSpeed, LinearDamping;
    public float SteeringSpeed, LookVelocitySharpness, MaxPitchAngle;
    public float RollAccumulationSpeed, ManualRollSpeed, RollRestitutionSpeed, YawSpeedForMaxRoll, MaxBankAngle;
    public float VelocityAlignSharpness;   // 0 = free velocity (current), >0 = nose drags velocity (space-sim)
    public float StrafeAcceleration;       // 0 = no strafe
    public byte  SteeringModel;            // see Step 2
    public byte  BankModel;                // see Step 4
    public BlobArray<float> LookResponseCurve; // reuse AnimationCurveBlob sampling
}

public struct FlightProfileTable { public BlobArray<FlightProfileBlob> Profiles; }

[GhostComponent] public struct FlightProfileRef : IComponentData { [GhostField] public byte Index; }
```

Replicating one byte instead of fifteen floats keeps bandwidth flat for 150 ships and keeps server and clients
guaranteed-identical. `PlayerVehicleSettings` can then shrink to `VehicleFX` or be removed.

### Step 2 – Split the steering model

Turn the pitch/yaw block into a switch on `SteeringModel`:

| Model | Behaviour | New fields |
|---|---|---|
| `EulerYawPitch` (current) | yaw about world up, clamped pitch, roll cosmetic | – |
| `SixDof` | integrate a quaternion: `rot = rot * fromEuler(pitchRate*dt, yawRate*dt, rollRate*dt)`; roll steers | none, but `MaxPitchAngle` unused; needs `VehicleMovementState.Rotation` quaternion ghost field |
| `AimPoint` ("fly to cursor") | mouse moves a target direction; nose turns toward it with a max rate | `TurnRateMax`, `AimReturnSharpness` |

Keep each model in its own Burst-friendly `static` function and select with `switch` on a byte, or use an
enableable tag per model with three `IJobEntity` jobs. The replicated state struct must be a superset
(add a quaternion field for `SixDof`; the Euler floats stay for the other two).

### Step 3 – Split the thrust model

Add `VelocityAlignSharpness`: each tick rotate `v` toward `forward * |v|` with `1 - exp(-align * dt)`. 0 keeps the
drift feel, 20 gives a rails-like racer, 3 gives a heavy spaceship. Add `MinSpeed` for always-moving styles,
`StrafeAcceleration` with a new `Strafe` float2 in `PlayerVehicleInput`, and optional `BoostMultiplier`. Move
`LinearDamping` into the profile and write `PhysicsDamping` when the profile is applied so a profile can be
"slippery" or "sticky" without touching the prefab.

### Step 4 – Parameterize banking

`BankModel`: `TurnAccumulation` (current), `ProportionalToYawRate` (`bank = clamp(yawSpeed * BankGain)`, no state,
far easier to reason about), `None`. Expose `MaxBankAngle` (currently fixed at 90°) and make the accumulation
dt-scaled (`* dt * 30` to preserve today's numbers).

### Step 5 – Input shaping per profile and per device

- Response curve for look (`LookResponseCurve`, sampled with the existing `AnimationCurveBlob`), per-device
  sensitivity (`MouseScale`, `StickScale`, `TouchScale`), stick deadzone.
- Fix the mouse sampling: accumulate `LookDelta` in `PlayerVehicleInputSystemBase` across frames and reset the
  accumulator when `NetworkTime.ServerTick` advances (or use the `IInputComponentData` accumulation idiom), so
  every pixel counts once regardless of frame rate.
- Make trackpad detection platform-complete (today only macOS works, see known issues) or drop it in favour of a
  user-facing "trackpad mode" toggle.

### Step 6 – Camera and feedback per profile

Move `FollowDistance`, `FollowSharpness`, `LookAtSharpness`, `VerticalOffset` into a `CameraProfile` referenced by
the flight profile, and add `FovAtRest`, `FovAtMaxSpeed`, `EnginePitchSpeedRange` (replace the hard-coded 30),
optional speed-line VFX threshold. These are client-only and can be hot-reloaded freely.

### Step 7 – Runtime switching

- Client-only values (sensitivity, invert, curves, camera) stay in `ControlSettings` / UI and persist via
  `PersistentDataManager` as today.
- Server-authoritative values (the profile index) change through an RPC (`SetFlightProfileRequest : IRpcCommand`)
  that the server validates (allowed list, cooldown) and writes to `FlightProfileRef.Index`. Both sides' `MoveJob`
  read the index on the next tick, so prediction stays consistent. In single player write the component directly.
- Expose the choice in the main menu (per-player default) and optionally per ship visual (bind a profile index to
  each entry of `ShipRandomVisuals` so the three hulls fly differently).

### Step 8 – Determinism and compatibility checklist

- No `Time.deltaTime`/frame dt inside `MoveJob`; only `SystemAPI.Time.DeltaTime` from the fixed step.
- Only `Unity.Mathematics`; no managed calls in predicted code.
- Any new state that `MoveJob` writes and reads next tick must be a `[GhostField]` on the ghost.
- Changing `PlayerVehicleInput` layout changes the command serializer; client and server must be built together.
- Keep `[BakingVersion]` bumped on the bakers when the baked layout changes.

### Step 9 – Tooling to tune by feel

- A debug overlay (dev builds) showing speed, yaw rate, bank, prediction error, current profile.
- Thin clients driven by scripted input sequences (replace the random generator) to record
  `VehicleMovementState` traces and compare profiles numerically.
- A "live tuning" editor window that writes the blob table at runtime in `LocalWorld` (single player) for fast
  iteration, then bakes.

### Quick map from desired feel to knob

| You want… | Change |
|---|---|
| Snappier aim | ↑ `LookVelocitySharpness` (less smoothing), ↑ `SteeringSpeed` |
| Heavier ship | ↓ `Acceleration`, ↓ damping (longer drift), ↓ `LookVelocitySharpness` |
| Racer on rails | `VelocityAlignSharpness` 10–20, `MinSpeed` > 0, `BankModel = ProportionalToYawRate` with big gain |
| Space sim | `SteeringModel = SixDof`, damping 0–0.2, `VelocityAlignSharpness` 0–2 |
| Less nausea on turns | ↓ `MaxBankAngle`, ↑ `LookAtSharpness`, ↓ camera `FollowDistance` |
| Faster feel without more speed | ↑ engine pitch range, FOV widening, ↑ `FollowSharpness` so the camera stays glued |

---

## 9. Flight-specific pitfalls found in the code

- `RollAccumulationSpeed` is per tick, not per second; change the tick rate and the bank timing changes.
- `TargetFollowDamping` / `TargetSqLerpThreshold` are baked but unused.
- `VehicleControlSinglePlayer.prefab` carries stale overrides (`SteeringSharpness`, old `PhysicsShapeAuthoring`
  fields) for components that no longer exist on the base prefab; they do nothing.
- Throttle cut on collision never triggers with the current prefab (no custom physics tag).
- Trackpad sensitivity only works on macOS (`PluginLoader` never loads the Windows DLL in a player build and
  calls a stub in the Windows editor).
- Thin clients steer almost not at all: their `LookVelocity` is in [-1, 1] while real input is scaled by 100.
- `BaseRoll` wraps at ±π and `RollRadians` is replicated as a raw float, so a remote ship rolling through 180° shows
  a brief spin while interpolation crosses the wrap.
- The settings UI slider stores `MouseSensitivity` as `int / 100`, while the authoring default is 1.0; the shipped
  `UserPreferences.asset` makes the effective default 2.0. Tune the asset, not the authoring field.
