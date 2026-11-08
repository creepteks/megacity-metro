using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MegacityMetro.Gameplay;
using UnityEngine;
using Utils.Misc;

namespace Unity.MegacityMetro.Authoring
{
    /// <summary>
    /// Create all the required player vehicle components
    /// </summary>
    public class PlayerVehicleControllerAuthoring : MonoBehaviour
    {
        [Header("Flight profiles")]
        [Tooltip("Selectable flight styles, baked into a blob table on the vehicle. Leave empty to build a single profile from the legacy fields below.")]
        public FlightProfile[] Profiles;
        [Tooltip("Index into Profiles used when the ship spawns.")]
        public int DefaultProfileIndex = 0;

        [Header("Legacy speed (used only when Profiles is empty)")]
        public float Acceleration = 5.0f;
        public float Deceleration = 10.0f;
        public float MaxSpeed = 50f;

        [Header("Legacy rotation (used only when Profiles is empty)")]
        public float SteeringSpeed = 5.0f;
        public float LookVelocitySharpness = 10.0f;
        public bool InvertPitch;
        public float MaxPitchAngle = 89f;
        public float RollAccumulationSpeed = 0.03f;
        public float ManualRollSpeed = 5f;
        public float RollRestitutionSpeed = 2f;
        public float YawSpeedForMaxRoll = 1.8f;

        [Header("Camera (legacy, unused)")]
        public float TargetFollowDamping = 2.0f;
        public float TargetSqLerpThreshold = 2500;

        [Header("Laser")]
        public LayerMask VehiclesPhysicsCategory;
        public LayerMask LaserObstructionsPhysicsCategory;
        public float DamagePerSecond = 50f;
        public float InitialEnergy = 100f;
        public float ChargeSpeed = 2.5f;
        public float DrainSpeed = 3f;
        public float AutoAimDegrees = 20f;
        public float AutoAimMinRadius = 3f;
        public float LaserRange = 150f;

        [Header("Immunity")]
        public float ImmunityDuration = 6.0f;

        [BakingVersion("megacity-metro", 6)]
        public class PlayerVehicleControllerBaker : Baker<PlayerVehicleControllerAuthoring>
        {
            public override void Bake(PlayerVehicleControllerAuthoring authoring)
            {
                var entity = GetEntity(authoring.gameObject, TransformUsageFlags.Dynamic);

                AddComponent(entity, new VehicleLaser
                {
                    DamagePerSecond = authoring.DamagePerSecond,
                    Energy = authoring.InitialEnergy,
                    ChargerSpeed = authoring.ChargeSpeed,
                    DrainSpeed = authoring.DrainSpeed,
                    AutoAimDegrees = authoring.AutoAimDegrees,
                    AutoAimMinRadius = authoring.AutoAimMinRadius,
                    Range = authoring.LaserRange,

                    VehiclesFilter = PhysicsUtilities.ProduceCollisionFilter(authoring.VehiclesPhysicsCategory.value),
                    LaserObstructionsFilter = PhysicsUtilities.ProduceCollisionFilter(authoring.LaserObstructionsPhysicsCategory.value),
                });


                var ignoreLaser = AddBuffer<WeaponShotIgnoredEntity>(entity);
                ignoreLaser.Add(new WeaponShotIgnoredEntity { Value = entity });

                AddComponent(entity, new VehicleHealth { Value = 100 });
                AddComponent(entity, new PlayerScore { Value = 0 });
                AddComponent(entity, new PlayerPlatform { Value = (int) RuntimePlatform.WindowsPlayer });
                AddComponent(entity, new Immunity { Duration = authoring.ImmunityDuration });
                // Add player inputs component
                var input = new PlayerVehicleInput
                {
                    LookVelocity = float2.zero,
                    Acceleration = 0,
                    Roll = 0
                };
                AddComponent(entity, input);

                // Legacy settings component: still carries InvertPitch (input job) and VehicleFX (visuals system).
                var settings = new PlayerVehicleSettings
                {
                    Acceleration = authoring.Acceleration,
                    Deceleration = authoring.Deceleration,
                    MaxSpeed = authoring.MaxSpeed,
                    MaxPitchAngle = authoring.MaxPitchAngle,
                    SteeringSpeed = authoring.SteeringSpeed,
                    LookVelocitySharpness = authoring.LookVelocitySharpness,

                    RollAccumulationSpeed = authoring.RollAccumulationSpeed,
                    ManualRollSpeed = authoring.ManualRollSpeed,
                    RollRestitutionSpeed = authoring.RollRestitutionSpeed,
                    YawSpeedForMaxRoll = authoring.YawSpeedForMaxRoll,

                    InvertPitch = authoring.InvertPitch,
                    TargetFollowDamping = authoring.TargetFollowDamping,
                    TargetSqLerpThreshold = authoring.TargetSqLerpThreshold,
                };
                AddComponent(entity, settings);

                // Flight profile table + selector
                var table = BuildProfileTable(authoring, out var profileCount);
                AddBlobAsset(ref table, out _);
                AddComponent(entity, new FlightProfileTableRef { Table = table });
                AddComponent(entity, new FlightProfileRef
                {
                    Index = (byte) math.clamp(authoring.DefaultProfileIndex, 0, profileCount - 1)
                });

                VehicleMovementState vehicleState = new VehicleMovementState();
                VehicleMovementState.SetRotation(ref vehicleState, authoring.transform.rotation);
                AddComponent(entity, vehicleState);
            }

            private BlobAssetReference<FlightProfileTable> BuildProfileTable(PlayerVehicleControllerAuthoring authoring, out int count)
            {
                count = 0;
                if (authoring.Profiles != null)
                {
                    foreach (var profile in authoring.Profiles)
                    {
                        if (profile != null)
                            count++;
                    }
                }

                using var builder = new BlobBuilder(Allocator.Temp);
                ref var root = ref builder.ConstructRoot<FlightProfileTable>();

                if (count == 0)
                {
                    // Backward compatibility: one profile reproducing the legacy authoring fields exactly.
                    count = 1;
                    var profiles = builder.Allocate(ref root.Profiles, 1);
                    var names = builder.Allocate(ref root.Names, 1);
                    WriteLegacyProfile(ref profiles[0], authoring);
                    builder.AllocateString(ref names[0], "Legacy");
                }
                else
                {
                    var profiles = builder.Allocate(ref root.Profiles, count);
                    var names = builder.Allocate(ref root.Names, count);
                    int i = 0;
                    foreach (var profile in authoring.Profiles)
                    {
                        if (profile == null)
                            continue;
                        DependsOn(profile);
                        profile.WriteTo(ref profiles[i]);
                        builder.AllocateString(ref names[i], profile.name);
                        i++;
                    }
                }

                return builder.CreateBlobAssetReference<FlightProfileTable>(Allocator.Persistent);
            }

            private void WriteLegacyProfile(ref FlightProfileBlob blob, PlayerVehicleControllerAuthoring authoring)
            {
                var body = GetComponent<Rigidbody>();

                blob.SteeringModel = (byte) SteeringModel.EulerYawPitch;
                blob.SteeringSpeed = authoring.SteeringSpeed;
                blob.LookVelocitySharpness = authoring.LookVelocitySharpness;
                blob.MaxPitchAngle = authoring.MaxPitchAngle;
                blob.TurnRateMax = 2f;
                blob.AimReturnSharpness = 2f;

                blob.Acceleration = authoring.Acceleration;
                blob.Deceleration = authoring.Deceleration;
                blob.MaxSpeed = authoring.MaxSpeed;
                blob.MinSpeed = 0f;
                blob.LinearDamping = body != null ? body.linearDamping : 1f;
                blob.VelocityAlignSharpness = 0f;
                blob.StrafeAcceleration = 0f;
                blob.DeathGravity = 20f;

                blob.BankModel = (byte) BankModel.TurnAccumulation;
                blob.RollAccumulationSpeed = authoring.RollAccumulationSpeed;
                blob.BankGain = 0.6f;
                blob.BankSharpness = 4f;
                blob.ManualRollSpeed = authoring.ManualRollSpeed;
                blob.RollRestitutionSpeed = authoring.RollRestitutionSpeed;
                blob.YawSpeedForMaxRoll = authoring.YawSpeedForMaxRoll;
                blob.MaxBankAngle = 90f;

                blob.MouseLookScale = 1f;
                blob.StickLookScale = 1f;
                blob.TouchLookScale = 1f;
                blob.StickDeadzone = 0f;
                blob.StickResponseExponent = 1f;

                blob.OverrideCamera = 0;
                blob.FollowDistance = 10f;
                blob.FollowSharpness = 18f;
                blob.LookAtSharpness = 15f;
                blob.CameraVerticalOffset = 2f;
                blob.FovAtRest = 60f;
                blob.FovAtMaxSpeed = 60f;
                blob.EnginePitchMaxSpeed = 30f;
            }
        }
    }
}
