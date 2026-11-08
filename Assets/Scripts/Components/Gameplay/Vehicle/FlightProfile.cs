using UnityEngine;

namespace Unity.MegacityMetro.Gameplay
{
    /// <summary>
    /// How look input turns the ship.
    /// </summary>
    public enum SteeringModel : byte
    {
        /// <summary>Yaw about world up, clamped pitch, cosmetic roll (the original Megacity model).</summary>
        EulerYawPitch = 0,
        /// <summary>Full quaternion integration: pitch/yaw/roll about body axes, no pitch limit, roll steers.</summary>
        SixDof = 1,
        /// <summary>Look input moves an aim direction; the nose chases it at a limited turn rate.</summary>
        AimPoint = 2,
    }

    /// <summary>
    /// How the ship banks when it turns (ignored by <see cref="SteeringModel.SixDof"/>, where roll is real).
    /// </summary>
    public enum BankModel : byte
    {
        /// <summary>Bank accumulates with yaw rate and relaxes when the turn slows (original behaviour, dt-scaled).</summary>
        TurnAccumulation = 0,
        /// <summary>Bank is proportional to the current yaw rate (stateless, easy to tune).</summary>
        ProportionalToYawRate = 1,
        /// <summary>No automatic bank; only manual roll.</summary>
        None = 2,
    }

    /// <summary>
    /// Designer-facing flight tuning. Profiles are baked into a <see cref="FlightProfileTable"/> blob on the vehicle prefab
    /// (see <c>PlayerVehicleControllerAuthoring</c>) and selected at runtime through <see cref="FlightProfileRef"/>.
    /// Server and clients bake the same table, so only the one-byte index is replicated.
    /// </summary>
    [CreateAssetMenu(fileName = "FlightProfile", menuName = "Gameplay/Flight Profile", order = 2)]
    public class FlightProfile : ScriptableObject
    {
        [Header("Steering")]
        public SteeringModel SteeringModel = SteeringModel.EulerYawPitch;
        [Tooltip("Global look-to-rotation gain. 1 = full stick turns at 1 rad/s.")]
        public float SteeringSpeed = 1f;
        [Tooltip("Exponential smoothing of look input (1/s). Higher = twitchier.")]
        public float LookVelocitySharpness = 15f;
        [Tooltip("Pitch limit in degrees (EulerYawPitch and AimPoint).")]
        public float MaxPitchAngle = 85f;
        [Tooltip("AimPoint only: maximum nose turn rate toward the aim direction (rad/s).")]
        public float TurnRateMax = 2f;
        [Tooltip("AimPoint only: how fast the aim direction returns to the nose when there is no look input (1/s). 0 = never.")]
        public float AimReturnSharpness = 2f;

        [Header("Thrust")]
        [Tooltip("Forward thrust (units/s^2).")]
        public float Acceleration = 100f;
        [Tooltip("Reverse thrust / brake (units/s^2).")]
        public float Deceleration = 75f;
        public float MaxSpeed = 100f;
        [Tooltip("Speed floor for always-moving styles. 0 = can stop.")]
        public float MinSpeed = 0f;
        [Tooltip("Written to PhysicsDamping.Linear every tick. 1 = original Rigidbody drag.")]
        public float LinearDamping = 1f;
        [Tooltip("0 = free velocity (drift). >0 = velocity is rotated toward the nose at this sharpness (1/s).")]
        public float VelocityAlignSharpness = 0f;
        [Tooltip("Lateral/vertical thrust for PlayerVehicleInput.Strafe (units/s^2). 0 disables strafing.")]
        public float StrafeAcceleration = 0f;
        [Tooltip("Downward acceleration applied while dead (units/s^2).")]
        public float DeathGravity = 20f;

        [Header("Roll and banking")]
        public BankModel BankModel = BankModel.TurnAccumulation;
        [Tooltip("TurnAccumulation: bank gain per tick at 30 Hz (dt-scaled).")]
        public float RollAccumulationSpeed = 0.03f;
        [Tooltip("ProportionalToYawRate: bank fraction per rad/s of yaw rate.")]
        public float BankGain = 0.6f;
        [Tooltip("ProportionalToYawRate: how fast the bank follows the target (1/s).")]
        public float BankSharpness = 4f;
        [Tooltip("Manual roll rate (rad/s). Also the roll rate for SixDof.")]
        public float ManualRollSpeed = 5f;
        [Tooltip("Relaxation of manual roll and bank back to level (1/s).")]
        public float RollRestitutionSpeed = 2f;
        [Tooltip("TurnAccumulation: at or above this yaw rate (rad/s) the bank is held. 0 disables bank restitution.")]
        public float YawSpeedForMaxRoll = 1.8f;
        [Tooltip("Maximum automatic bank in degrees.")]
        public float MaxBankAngle = 90f;

        [Header("Input shaping")]
        public float MouseLookScale = 1f;
        public float StickLookScale = 1f;
        public float TouchLookScale = 1f;
        [Range(0f, 0.9f)] public float StickDeadzone = 0.1f;
        [Tooltip("Response curve exponent for sticks/keys/touch. 1 = linear, 2 = fine control near centre.")]
        public float StickResponseExponent = 1f;

        [Header("Camera and feedback")]
        [Tooltip("When off, the HybridCameraManager prefab values are used.")]
        public bool OverrideCamera = false;
        public float FollowDistance = 10f;
        public float FollowSharpness = 18f;
        public float LookAtSharpness = 15f;
        public float CameraVerticalOffset = 2f;
        public float FovAtRest = 60f;
        public float FovAtMaxSpeed = 60f;
        [Tooltip("Speed at which the engine sound reaches maximum pitch (units/s).")]
        public float EnginePitchMaxSpeed = 30f;

        /// <summary>Copy every tuning value into a blob record (name is written separately by the baker).</summary>
        public void WriteTo(ref FlightProfileBlob blob)
        {
            blob.SteeringModel = (byte)SteeringModel;
            blob.SteeringSpeed = SteeringSpeed;
            blob.LookVelocitySharpness = LookVelocitySharpness;
            blob.MaxPitchAngle = MaxPitchAngle;
            blob.TurnRateMax = TurnRateMax;
            blob.AimReturnSharpness = AimReturnSharpness;

            blob.Acceleration = Acceleration;
            blob.Deceleration = Deceleration;
            blob.MaxSpeed = MaxSpeed;
            blob.MinSpeed = MinSpeed;
            blob.LinearDamping = LinearDamping;
            blob.VelocityAlignSharpness = VelocityAlignSharpness;
            blob.StrafeAcceleration = StrafeAcceleration;
            blob.DeathGravity = DeathGravity;

            blob.BankModel = (byte)BankModel;
            blob.RollAccumulationSpeed = RollAccumulationSpeed;
            blob.BankGain = BankGain;
            blob.BankSharpness = BankSharpness;
            blob.ManualRollSpeed = ManualRollSpeed;
            blob.RollRestitutionSpeed = RollRestitutionSpeed;
            blob.YawSpeedForMaxRoll = YawSpeedForMaxRoll;
            blob.MaxBankAngle = MaxBankAngle;

            blob.MouseLookScale = MouseLookScale;
            blob.StickLookScale = StickLookScale;
            blob.TouchLookScale = TouchLookScale;
            blob.StickDeadzone = StickDeadzone;
            blob.StickResponseExponent = StickResponseExponent;

            blob.OverrideCamera = (byte)(OverrideCamera ? 1 : 0);
            blob.FollowDistance = FollowDistance;
            blob.FollowSharpness = FollowSharpness;
            blob.LookAtSharpness = LookAtSharpness;
            blob.CameraVerticalOffset = CameraVerticalOffset;
            blob.FovAtRest = FovAtRest;
            blob.FovAtMaxSpeed = FovAtMaxSpeed;
            blob.EnginePitchMaxSpeed = EnginePitchMaxSpeed;
        }
    }
}
