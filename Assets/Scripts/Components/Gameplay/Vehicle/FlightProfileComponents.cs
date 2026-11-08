using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Unity.MegacityMetro.Gameplay
{
    /// <summary>
    /// One baked flight profile. Mirrors <see cref="FlightProfile"/>; enums and bools are stored as bytes so the
    /// struct is blittable, Burst-friendly and cheap to copy out of the blob.
    /// </summary>
    public struct FlightProfileBlob
    {
        /// <summary>Converts look velocity to rad/s. All sensitivity numbers are relative to this.</summary>
        public const float BaseSteeringSpeedMultiplier = 0.01f;
        /// <summary>The tick rate the original per-tick roll accumulation constants were tuned at.</summary>
        public const float RollAccumulationReferenceTickRate = 30f;

        public byte SteeringModel;
        public float SteeringSpeed;
        public float LookVelocitySharpness;
        public float MaxPitchAngle;
        public float TurnRateMax;
        public float AimReturnSharpness;

        public float Acceleration;
        public float Deceleration;
        public float MaxSpeed;
        public float MinSpeed;
        public float LinearDamping;
        public float VelocityAlignSharpness;
        public float StrafeAcceleration;
        public float DeathGravity;

        public byte BankModel;
        public float RollAccumulationSpeed;
        public float BankGain;
        public float BankSharpness;
        public float ManualRollSpeed;
        public float RollRestitutionSpeed;
        public float YawSpeedForMaxRoll;
        public float MaxBankAngle;

        public float MouseLookScale;
        public float StickLookScale;
        public float TouchLookScale;
        public float StickDeadzone;
        public float StickResponseExponent;

        public byte OverrideCamera;
        public float FollowDistance;
        public float FollowSharpness;
        public float LookAtSharpness;
        public float CameraVerticalOffset;
        public float FovAtRest;
        public float FovAtMaxSpeed;
        public float EnginePitchMaxSpeed;
    }

    /// <summary>All selectable profiles, baked once from the vehicle prefab. Names are kept apart so a profile can be copied by value.</summary>
    public struct FlightProfileTable
    {
        public BlobArray<FlightProfileBlob> Profiles;
        public BlobArray<BlobString> Names;
    }

    /// <summary>
    /// Reference to the baked table. Lives on every vehicle entity (server and clients bake the same prefab).
    /// Not replicated: blob assets cannot be ghost fields and do not need to be.
    /// </summary>
    public struct FlightProfileTableRef : IComponentData
    {
        public BlobAssetReference<FlightProfileTable> Table;

        public readonly int Count => Table.IsCreated ? Table.Value.Profiles.Length : 0;

        /// <summary>Returns a copy of the profile at <paramref name="index"/>, clamped to the table bounds.</summary>
        public readonly FlightProfileBlob GetProfile(int index)
        {
            ref var table = ref Table.Value;
            index = math.clamp(index, 0, table.Profiles.Length - 1);
            return table.Profiles[index];
        }

        /// <summary>Display name of a profile (debug/UI; managed string allocation).</summary>
        public readonly string GetName(int index)
        {
            ref var table = ref Table.Value;
            if (table.Names.Length == 0)
                return string.Empty;
            index = math.clamp(index, 0, table.Names.Length - 1);
            return table.Names[index].ToString();
        }
    }

    /// <summary>
    /// The active profile of a vehicle. Server authoritative: clients change it through
    /// <see cref="SetFlightProfileRequest"/>; the server validates and the value replicates back.
    /// </summary>
    public struct FlightProfileRef : IComponentData
    {
        [GhostField] public byte Index;
    }
}
