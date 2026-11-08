using Unity.NetCode;

namespace Unity.MegacityMetro.Gameplay
{
    /// <summary>
    /// Client → server request to switch the sender's ship to another entry of the baked flight profile table.
    /// Handled by <c>SetFlightProfileSystem</c> on the server, which validates the index.
    /// </summary>
    public struct SetFlightProfileRequest : IRpcCommand
    {
        public byte Index;
    }
}
