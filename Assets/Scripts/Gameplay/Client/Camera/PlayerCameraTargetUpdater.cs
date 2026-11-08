using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MegacityMetro.Gameplay;
using Unity.MegacityMetro.UI;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

namespace Unity.MegacityMetro.CameraManagement
{
    /// <summary>
    /// Updates player camera target position and rotation, and applies the active flight profile's camera settings.
    /// </summary>
    [BurstCompile]
    [UpdateAfter(typeof(TransformSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial struct PlayerCameraTargetUpdater : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerCameraTarget>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            ComponentLookup<LocalToWorld> localToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true);
            var profileTableLookup = SystemAPI.GetComponentLookup<FlightProfileTableRef>(true);
            var profileRefLookup = SystemAPI.GetComponentLookup<FlightProfileRef>(true);
            var velocityLookup = SystemAPI.GetComponentLookup<PhysicsVelocity>(true);

            foreach (var (laser, localTransform, ltw, camTarget, entity) in SystemAPI.Query<RefRO<VehicleLaser>, RefRO<LocalTransform>, RefRO<LocalToWorld>, RefRO<PlayerCameraTarget>>().WithAll<GhostOwnerIsLocal>().WithEntityAccess())
            {
                var camera = HybridCameraManager.Instance;
                if (!camera.WasInitialized)
                {
                    camera.PlaceCamera(localTransform.ValueRO.Position, localTransform.ValueRO.Rotation);
                    camera.WasInitialized = true;
                }

                // Flight profile camera overrides
                float verticalOffset = camTarget.ValueRO.VerticalOffset;
                if (profileTableLookup.TryGetComponent(entity, out var table) && profileRefLookup.TryGetComponent(entity, out var profileRef))
                {
                    var profile = table.GetProfile(profileRef.Index);
                    if (profile.OverrideCamera != 0)
                    {
                        camera.ApplyFlightCamera(profile.FollowDistance, profile.FollowSharpness, profile.LookAtSharpness);
                        verticalOffset = profile.CameraVerticalOffset;

                        float speed = velocityLookup.TryGetComponent(entity, out var velocity) ? math.length(velocity.Linear) : 0f;
                        float speed01 = math.saturate(speed / math.max(1f, profile.MaxSpeed));
                        camera.SetFieldOfView(math.lerp(profile.FovAtRest, profile.FovAtMaxSpeed, speed01));
                    }
                    else
                    {
                        camera.ResetFlightCamera();
                    }
                }

                camera.SetPlayerTargetPosition(ltw.ValueRO.Position + (math.up() * verticalOffset));
                camera.UpdateAimCameraTargetPosition(laser.ValueRO.CalculateLaserEndPoint(ltw.ValueRO.Position, ltw.ValueRO.Rotation, ref localToWorldLookup, true));

                camera.CameraUpdate(deltaTime);
                
                if (HUD.Instance != null && HUD.Instance.Crosshair.IsVisible)
                {
                    HUD.Instance.Crosshair.SetTarget(laser.ValueRO.DetectedTarget != Entity.Null);
                    HUD.Instance.Crosshair.UpdatePosition(laser.ValueRO.CalculateLaserEndPoint(ltw.ValueRO.Position, ltw.ValueRO.Rotation, ref localToWorldLookup));
                }
            }
        }
    }
}
