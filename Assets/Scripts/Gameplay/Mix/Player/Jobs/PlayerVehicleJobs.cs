using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Unity.NetCode;
using Utils.Misc;


namespace Unity.MegacityMetro.Gameplay
{
    [BurstCompile]
    [WithAll(typeof(Simulate))]
    internal partial struct ReconstructVehicleRotationJob : IJobEntity
    {
        public void Execute(in VehicleMovementState vehicleMovementState, ref LocalTransform localTransform)
        {
            localTransform.Rotation = vehicleMovementState.CalculateTargetRotation();
        }
    }
    
    [BurstCompile]
    [WithDisabled(typeof(GhostOwnerIsLocal))]
    internal partial struct InterpolatedVehicleRotationJob : IJobEntity
    {
        public void Execute(Entity entity, in VehicleMovementState vehicleMovementState, ref LocalTransform localTransform)
        {
            localTransform.Rotation = vehicleMovementState.CalculateTargetRotation();
        }
    }

    /// <summary>
    /// Writes the per-profile physics values (linear damping) every tick so a profile switch takes effect
    /// deterministically on server and predicting client.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(Simulate))]
    internal partial struct ApplyFlightProfilePhysicsJob : IJobEntity
    {
        public void Execute(in FlightProfileTableRef profileTable, in FlightProfileRef profileRef, ref PhysicsDamping damping)
        {
            damping.Linear = profileTable.GetProfile(profileRef.Index).LinearDamping;
        }
    }

    /// <summary>
    /// The flight model. Runs on the server and in owner prediction at the fixed tick (1/30 s).
    /// Everything it reads is a command, a ghost field or baked data, so rollback is exact.
    /// See Documentation/flight-control.md for the math and the profile fields.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(Simulate))]
    internal partial struct MoveJob : IJobEntity
    {
        public float DeltaTime;

        public void Execute(
            in PlayerVehicleInput controlInput,
            in FlightProfileTableRef profileTable,
            in FlightProfileRef profileRef,
            ref PhysicsVelocity velocity,
            in VehicleHealth health,
            ref VehicleMovementState state)
        {
            FlightProfileBlob profile = profileTable.GetProfile(profileRef.Index);
            float dt = DeltaTime;

            if (health.IsDead == 1)
            {
                // Dead: no control, fake gravity (the body has gravity factor 0).
                velocity.Linear += -math.up() * profile.DeathGravity * dt;
                return;
            }

            // Look smoothing (shared by all steering models)
            float2 look = math.lerp(state.LastLookVelocity, controlInput.LookVelocity,
                MathUtilities.GetSharpnessInterpolant(profile.LookVelocitySharpness, dt));
            state.LastLookVelocity = look;

            float rotationSpeed = FlightProfileBlob.BaseSteeringSpeedMultiplier * profile.SteeringSpeed;
            float maxPitch = math.radians(profile.MaxPitchAngle);
            float yawSpeed = 0f;          // rad/s, drives banking
            bool eulerRoll = true;        // false for SixDof where roll is real steering
            quaternion steeringRotation;  // orientation used for this tick's thrust direction

            switch ((SteeringModel) profile.SteeringModel)
            {
                case SteeringModel.SixDof:
                {
                    eulerRoll = false;
                    var bodyRates = new float3(
                        look.x * rotationSpeed,
                        look.y * rotationSpeed,
                        -controlInput.Roll * profile.ManualRollSpeed);
                    quaternion current = state.CalculateTargetRotation();
                    quaternion delta = quaternion.Euler(bodyRates * dt);
                    state.Rotation = math.normalize(math.mul(current, delta));

                    // Keep the Euler state readable for HUD/debug and for a later switch back to an Euler model.
                    float3 euler = MathUtilities.ToEuler(state.Rotation);
                    state.PitchRadians = euler.x;
                    state.YawRadians = euler.y;
                    state.RollRadians = euler.z;
                    state.BaseRoll = 0f;
                    state.RollAccumulationFromTurn = 0f;
                    state.AimPitchRadians = euler.x;
                    state.AimYawRadians = euler.y;

                    yawSpeed = bodyRates.y;
                    steeringRotation = state.Rotation;
                    break;
                }
                case SteeringModel.AimPoint:
                {
                    // Input moves the aim direction
                    state.AimPitchRadians = math.clamp(state.AimPitchRadians + look.x * rotationSpeed * dt, -maxPitch, maxPitch);
                    state.AimYawRadians += look.y * rotationSpeed * dt;

                    // No input: the aim drifts back onto the nose
                    if (math.lengthsq(controlInput.LookVelocity) < 1e-4f && profile.AimReturnSharpness > 0f)
                    {
                        float k = MathUtilities.GetSharpnessInterpolant(profile.AimReturnSharpness, dt);
                        state.AimPitchRadians = math.lerp(state.AimPitchRadians, state.PitchRadians, k);
                        state.AimYawRadians = state.YawRadians + math.lerp(WrapPi(state.AimYawRadians - state.YawRadians), 0f, k);
                    }

                    // The nose chases the aim at a limited rate
                    float maxStep = profile.TurnRateMax * dt;
                    float pitchStep = math.clamp(state.AimPitchRadians - state.PitchRadians, -maxStep, maxStep);
                    float yawStep = math.clamp(WrapPi(state.AimYawRadians - state.YawRadians), -maxStep, maxStep);
                    state.PitchRadians = math.clamp(state.PitchRadians + pitchStep, -maxPitch, maxPitch);
                    state.YawRadians += yawStep;

                    yawSpeed = dt > 0f ? yawStep / dt : 0f;
                    steeringRotation = state.CalculateEulerRotation();
                    break;
                }
                default: // SteeringModel.EulerYawPitch (original Megacity behaviour)
                {
                    float yawDelta = look.y * rotationSpeed * dt;
                    state.PitchRadians = math.clamp(state.PitchRadians + look.x * rotationSpeed * dt, -maxPitch, maxPitch);
                    state.YawRadians += yawDelta;

                    yawSpeed = dt > 0f ? yawDelta / dt : 0f;
                    steeringRotation = state.CalculateEulerRotation();
                    break;
                }
            }

            // Thrust
            float3 forward = math.mul(steeringRotation, math.forward());
            {
                float chosenAcceleration = controlInput.Acceleration < 0f ? profile.Deceleration : profile.Acceleration;
                velocity.Linear += forward * controlInput.Acceleration * chosenAcceleration * dt;

                if (profile.StrafeAcceleration > 0f)
                {
                    float3 right = math.mul(steeringRotation, math.right());
                    float3 up = math.mul(steeringRotation, math.up());
                    float2 strafe = math.clamp(controlInput.Strafe, -1f, 1f);
                    velocity.Linear += (right * strafe.x + up * strafe.y) * profile.StrafeAcceleration * dt;
                }

                float speed = math.length(velocity.Linear);

                // Space-sim / racer feel: rotate the velocity toward the nose
                if (profile.VelocityAlignSharpness > 0f && speed > 1e-3f)
                {
                    float k = MathUtilities.GetSharpnessInterpolant(profile.VelocityAlignSharpness, dt);
                    velocity.Linear = math.lerp(velocity.Linear, forward * speed, k);
                    speed = math.length(velocity.Linear);
                }

                if (profile.MinSpeed > 0f && speed < profile.MinSpeed)
                {
                    float3 direction = speed > 1e-3f ? velocity.Linear / speed : forward;
                    velocity.Linear = direction * profile.MinSpeed;
                    speed = profile.MinSpeed;
                }

                if (speed > profile.MaxSpeed)
                {
                    velocity.Linear = math.normalizesafe(velocity.Linear) * profile.MaxSpeed;
                }
            }

            if (!eulerRoll)
                return;

            // Roll and bank (Euler and AimPoint models)
            {
                switch ((BankModel) profile.BankModel)
                {
                    case BankModel.ProportionalToYawRate:
                    {
                        float target = math.clamp(yawSpeed * profile.BankGain, -1f, 1f);
                        state.RollAccumulationFromTurn = math.lerp(state.RollAccumulationFromTurn, target,
                            MathUtilities.GetSharpnessInterpolant(profile.BankSharpness, dt));
                        break;
                    }
                    case BankModel.None:
                    {
                        state.RollAccumulationFromTurn = 0f;
                        break;
                    }
                    default: // BankModel.TurnAccumulation
                    {
                        if (yawSpeed != 0f)
                        {
                            quaternion rotationWithoutRoll = state.CalculateTargetRotationWithoutRoll();
                            float3 velChangeFromTurningDirection = math.mul(rotationWithoutRoll, math.right() * yawSpeed);
                            float3 newUp = math.mul(steeringRotation, math.up());
                            float dotUpWithVelChangeDirection = math.dot(math.normalizesafe(velChangeFromTurningDirection), newUp);

                            float turnAmount = -(1f - dotUpWithVelChangeDirection) * 0.5f * math.abs(yawSpeed);
                            float turnDirection = yawSpeed < 0f ? 1f : -1f;
                            // Originally a per-tick gain at 30 Hz; scaled by dt so the tick rate can change.
                            float accumulation = profile.RollAccumulationSpeed * dt * FlightProfileBlob.RollAccumulationReferenceTickRate;
                            state.RollAccumulationFromTurn = math.clamp(
                                state.RollAccumulationFromTurn + turnAmount * turnDirection * accumulation, -1f, 1f);
                        }
                        break;
                    }
                }

                // Manual roll (from input), wrapped to [-pi, pi]
                state.BaseRoll = WrapPi(state.BaseRoll + (-controlInput.Roll * profile.ManualRollSpeed * dt));

                // Final roll for this tick
                state.RollRadians = state.BaseRoll + math.lerp(0f, math.radians(profile.MaxBankAngle), -state.RollAccumulationFromTurn);

                // Restitution of manual roll
                if (math.abs(controlInput.Roll) < 0.001f)
                {
                    state.BaseRoll = math.lerp(state.BaseRoll, 0f, MathUtilities.GetSharpnessInterpolant(profile.RollRestitutionSpeed, dt));
                }

                // Restitution of turning bank (TurnAccumulation only; Proportional is self-correcting)
                if ((BankModel) profile.BankModel == BankModel.TurnAccumulation && profile.YawSpeedForMaxRoll != 0f)
                {
                    float finalRollRestitutionSpeed = math.lerp(profile.RollRestitutionSpeed, 0f,
                        math.saturate(math.abs(yawSpeed) / profile.YawSpeedForMaxRoll));
                    state.RollAccumulationFromTurn = math.lerp(state.RollAccumulationFromTurn, 0f,
                        MathUtilities.GetSharpnessInterpolant(finalRollRestitutionSpeed, dt));
                }

                state.Rotation = state.CalculateEulerRotation();
            }
        }

        /// <summary>Wraps an angle to [-pi, pi].</summary>
        private static float WrapPi(float angle)
        {
            const float twoPi = 2f * math.PI;
            angle = math.fmod(angle + math.PI, twoPi);
            if (angle < 0f)
                angle += twoPi;
            return angle - math.PI;
        }
    }

    [BurstCompile]
    [WithAll(typeof(Simulate))]
    internal partial struct HealthAndImmunityJob : IJobEntity
    {
        public float DeltaTime;

        public void Execute(
            ref VehicleHealth health,
            ref Immunity immunity)
        {
            if (immunity.Counter > 0f)
            {
                immunity.Counter -= DeltaTime;
            }
            
            // Death
            // Add a threshold to avoid the death UI
            if (math.abs(health.Value) < 0.01f && health.IsDead == 0)
            {
                health.IsDead = 1;
                health._deathTimer = 5f;
            }

            if (health.IsDead == 1)
            {
                health._deathTimer -= DeltaTime;

                // Revive
                if (health._deathTimer <= 0)
                {
                    health.Value = 100;
                    health.IsDead = 0;

                    immunity.Counter = immunity.Duration;
                }
            }
        }
    }

    [BurstCompile]
    [WithAll(typeof(Simulate))]
    internal partial struct ApplyVehicleRotationAsVelocityJob : IJobEntity
    {
        public float DeltaTime;

        public void Execute(
            ref PhysicsVelocity physicsVelocity,
            in VehicleMovementState vehicleMovementState,
            in PhysicsMass mass,
            in LocalTransform localTransform)
        {
            physicsVelocity.Angular = PhysicsVelocity.CalculateVelocityToTarget(in mass, localTransform.Position, localTransform.Rotation, new RigidTransform(vehicleMovementState.CalculateTargetRotation(), localTransform.Position), 1f / DeltaTime).Angular;
        }
    }
}
