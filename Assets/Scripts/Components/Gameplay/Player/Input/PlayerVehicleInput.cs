using Unity.Mathematics;
using Unity.NetCode;

namespace Unity.MegacityMetro.Gameplay
{
    /// <summary>
    /// Capture the user input and apply them to a component for later uses
    /// </summary>
    [GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
    public struct PlayerVehicleInput : IInputComponentData
    {
        /// <summary>x = pitch input, y = yaw input, in "look velocity" units (see flight-control.md).</summary>
        public float2 LookVelocity;
        public float AimAssistSensitivity;
        public float Acceleration; // acceleration
        public float Roll; // manual roll
        /// <summary>Lateral (x) and vertical (y) thrust in [-1, 1]. Only used by profiles with StrafeAcceleration > 0.</summary>
        public float2 Strafe;
        public bool Shoot; // Shoot laser (X or A)

        // Cheats
        public bool Cheat_1; // Auto-hurt
    }
}
