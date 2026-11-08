using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Utils.Misc;

/// <summary>
/// Replicated flight state. <see cref="Rotation"/> is the authoritative orientation for every steering model;
/// the Euler fields are the integration state of the Euler/AimPoint models and are kept in sync for SixDof.
/// </summary>
[GhostComponent]
public struct VehicleMovementState : IComponentData
{
    [GhostField(Quantization = 1000, Smoothing = SmoothingAction.Interpolate)] 
    public float PitchRadians;
    [GhostField(Quantization = 1000, Smoothing = SmoothingAction.Interpolate)] 
    public float YawRadians;
    [GhostField(Quantization = 1000, Smoothing = SmoothingAction.Interpolate)] 
    public float RollRadians;
    
    [GhostField(Quantization = 1000, Smoothing = SmoothingAction.Interpolate)] 
    public float BaseRoll;
    [GhostField(Quantization = 1000, Smoothing = SmoothingAction.Interpolate)] 
    public float RollAccumulationFromTurn;
    
    [GhostField(Quantization = 1000, Smoothing = SmoothingAction.Interpolate)] 
    public float2 LastLookVelocity;

    /// <summary>Final orientation written by MoveJob. Interpolated as a quaternion on remote clients (no roll-wrap spin).</summary>
    [GhostField(Quantization = 1000, Smoothing = SmoothingAction.Interpolate)]
    public quaternion Rotation;

    /// <summary>AimPoint steering: direction the player is pointing at, which the nose chases.</summary>
    [GhostField(Quantization = 1000, Smoothing = SmoothingAction.Interpolate)]
    public float AimPitchRadians;
    [GhostField(Quantization = 1000, Smoothing = SmoothingAction.Interpolate)]
    public float AimYawRadians;

    /// <summary>The orientation to apply to the transform this tick.</summary>
    public quaternion CalculateTargetRotation()
    {
        // Guard against an unset quaternion (zero) from older bakes or manual construction.
        if (math.lengthsq(Rotation.value) < 0.5f)
            return CalculateEulerRotation();
        return Rotation;
    }

    /// <summary>Orientation built from the Euler integration state (yaw about world up, then pitch, then roll).</summary>
    public quaternion CalculateEulerRotation()
    {
        return quaternion.Euler(PitchRadians, YawRadians, RollRadians);
    }

    public quaternion CalculateTargetRotationWithoutRoll()
    {
        return quaternion.Euler(PitchRadians, YawRadians, 0f);
    }

    public static void SetRotation(ref VehicleMovementState vehicleMovementState, quaternion targetRotation)
    {
        float3 eulerAngles = MathUtilities.ToEuler(targetRotation);
        vehicleMovementState.PitchRadians = eulerAngles.x;
        vehicleMovementState.YawRadians = eulerAngles.y;
        vehicleMovementState.RollRadians = eulerAngles.z;
        vehicleMovementState.AimPitchRadians = eulerAngles.x;
        vehicleMovementState.AimYawRadians = eulerAngles.y;
        vehicleMovementState.Rotation = math.normalize(targetRotation);
    }
}
