using Unity.Entities;
using Unity.Mathematics;
using Unity.MegacityMetro.CameraManagement;
using UnityEngine.UIElements;
using System.Runtime.InteropServices;
#if UNITY_ANDROID || UNITY_IPHONE || ENABLED_VIRTUAL_JOYSTICK
using UnityEngine;
using Unity.MegacityMetro.UI;
#else
using UnityEngine;
#endif
using Unity.NetCode;

namespace Unity.MegacityMetro.Gameplay
{
    /// <summary>
    /// System to collect the player input and send it to the player vehicle.
    /// Mouse deltas are accumulated across the frames that map to one simulation tick so every pixel counts once
    /// regardless of frame rate; sticks/keys/touch are shaped by the active flight profile.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    public partial class PlayerVehicleInputSystemBase : SystemBase
    {
        private GameInput m_GameInput;

        // Mouse accumulation for the current tick
        private float2 m_AccumulatedMouseDelta;
        private float m_AccumulatedMouseTime;
        private uint m_LastTickIndex = uint.MaxValue;

        private bool IsTrackpadTouched => PluginLoader.TouchesCount() > 0;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<ControlSettings>();
            PluginLoader.LoadPlugin();
        }

        protected override void OnStopRunning()
        {
            base.OnStopRunning();
            m_GameInput?.Disable();
            PluginLoader.UnloadPlugin();
        }

        protected override void OnUpdate()
        {
            const float ConstLookVelocityMultiplier = 100f;
            
            if (HybridCameraManager.Instance == null)
                return;

            float deltaTime = SystemAPI.Time.DeltaTime;
            
            if (m_GameInput == null)
            {
                m_GameInput = new GameInput();
                m_GameInput.Enable();
                m_GameInput.Gameplay.Enable();
            }

            var controlSettings = SystemAPI.GetSingleton<ControlSettings>();
            var invertHorizontal = controlSettings.InverseLookHorizontal ? -1 : 1;
            var invertVertical = controlSettings.InverseLookVertical ? -1 : 1;
            var accelerationRange = controlSettings.AccelerationRange;

            // Input shaping from the local ship's flight profile (defaults when no ship exists yet)
            float mouseScale = 1f, stickScale = 1f, touchScale = 1f, stickDeadzone = 0f, stickExponent = 1f;
            foreach (var (table, profileRef) in SystemAPI.Query<RefRO<FlightProfileTableRef>, RefRO<FlightProfileRef>>().WithAll<GhostOwnerIsLocal>())
            {
                var profile = table.ValueRO.GetProfile(profileRef.ValueRO.Index);
                mouseScale = profile.MouseLookScale;
                stickScale = profile.StickLookScale;
                touchScale = profile.TouchLookScale;
                stickDeadzone = profile.StickDeadzone;
                stickExponent = profile.StickResponseExponent;
                break;
            }

#if UNITY_ANDROID || UNITY_IPHONE || ENABLED_VIRTUAL_JOYSTICK
            float2 joystick = ShapeStick(MobileControls.Instance.JoystickLeft.Delta, stickDeadzone, stickExponent);
            var input = new PlayerVehicleInput
            {
                Acceleration = math.clamp(MobileControls.Instance.Speed, accelerationRange.x, accelerationRange.y),
                LookVelocity = new float2(
                    joystick.y * invertVertical,
                    joystick.x * invertHorizontal) * ConstLookVelocityMultiplier * touchScale,
                Shoot = MobileControls.Instance.Shoot,
                AimAssistSensitivity = controlSettings.AimAssistanceSensitivity,
            };
#else
            
            var gameplayInputActions = m_GameInput.Gameplay;

            // Reset the mouse accumulator when the simulation tick advances
            uint tickIndex = CurrentTickIndex();
            if (tickIndex != m_LastTickIndex)
            {
                m_LastTickIndex = tickIndex;
                m_AccumulatedMouseDelta = float2.zero;
                m_AccumulatedMouseTime = 0f;
            }
            m_AccumulatedMouseDelta += (float2) gameplayInputActions.LookDelta.ReadValue<Vector2>();
            m_AccumulatedMouseTime += deltaTime;
            
            float2 lookInput = default; 
            if (math.any(m_AccumulatedMouseDelta != float2.zero) && m_AccumulatedMouseTime > 0f)
            {
                float sensitivity = IsTrackpadTouched ? controlSettings.TrackpadSensitivity : 1.0f;
                lookInput = MouseLookVelocity(m_AccumulatedMouseDelta, m_AccumulatedMouseTime,
                    invertHorizontal, invertVertical, controlSettings.MouseSensitivity * sensitivity * mouseScale);
            }
            else
            {
                float2 stick = ShapeStick(gameplayInputActions.LookConst.ReadValue<Vector2>(), stickDeadzone, stickExponent);
                lookInput = new float2
                {
                    x = -stick.y * invertVertical,
                    y = stick.x * invertHorizontal,
                };
                lookInput *= ConstLookVelocityMultiplier * stickScale;
            }
            
            var input = new PlayerVehicleInput
            {
                Acceleration = math.clamp(gameplayInputActions.Move.ReadValue<float>(), accelerationRange.x, accelerationRange.y), 
                Roll = gameplayInputActions.Roll.ReadValue<float>(),
                LookVelocity = lookInput,
                Shoot = gameplayInputActions.Fire.IsPressed(),
                AimAssistSensitivity = controlSettings.AimAssistanceSensitivity,
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Cheat_1 = gameplayInputActions.Cheat_1.IsPressed()
#endif
            };
#endif

            var job = new PlayerVehicleInputJob { CollectedInput = input};
            job.Schedule();
        }

        /// <summary>
        /// Index of the simulation tick the current frame's input will be consumed by: the predicted server tick
        /// in netcode worlds, a fixed-step counter in the local (single-player) world.
        /// </summary>
        private uint CurrentTickIndex()
        {
            if (SystemAPI.TryGetSingleton<NetworkTime>(out var networkTime) && networkTime.ServerTick.IsValid)
                return networkTime.ServerTick.TickIndexForValidTick;
            return (uint) (SystemAPI.Time.ElapsedTime * RateSettings.tickRate);
        }

        /// <summary>
        /// Converts the mouse displacement accumulated over the frames of one simulation tick into the look
        /// velocity the command carries (x = pitch, y = yaw). Pure function for tests (known issue F3).
        /// </summary>
        internal static float2 MouseLookVelocity(float2 accumulatedDelta, float accumulatedTime, float invertHorizontal, float invertVertical, float sensitivity)
        {
            if (accumulatedTime <= 0f)
                return float2.zero;
            var look = new float2(-accumulatedDelta.y * invertVertical, accumulatedDelta.x * invertHorizontal);
            return look * sensitivity / accumulatedTime; // displacement accumulated this tick -> velocity
        }

        /// <summary>Deadzone + response-exponent shaping for a normalized 2D axis (sticks, key composites, touch).</summary>
        internal static float2 ShapeStick(float2 value, float deadzone, float exponent)
        {
            float magnitude = math.length(value);
            if (magnitude < 1e-5f || magnitude <= deadzone)
                return float2.zero;

            float normalized = math.saturate((magnitude - deadzone) / math.max(1e-5f, 1f - deadzone));
            float shaped = exponent == 1f ? normalized : math.pow(normalized, math.max(0.1f, exponent));
            return value / magnitude * shaped;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class PlayerVehicleInputSystemSinglePlayer : PlayerVehicleInputSystemBase
    { 
    }
}
