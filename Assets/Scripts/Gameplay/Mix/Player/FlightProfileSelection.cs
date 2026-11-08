using System;
using UnityEngine;

namespace Unity.MegacityMetro.Gameplay
{
    /// <summary>
    /// Entry point for UI/debug code to ask for a different flight profile for the local ship.
    /// <c>FlightProfileRequestSystem</c> (client/local worlds) consumes the request: in multiplayer it sends
    /// <see cref="SetFlightProfileRequest"/> to the server, in single player it writes <see cref="FlightProfileRef"/> directly.
    /// </summary>
    public static class FlightProfileSelection
    {
        /// <summary>Last index the local player successfully requested (not necessarily replicated yet).</summary>
        public static int CurrentIndex { get; set; }

        /// <summary>Raised when a request has been applied or sent.</summary>
        public static event Action<int> Changed;

        private static int s_RequestedIndex = -1;

        public static void Request(int index)
        {
            s_RequestedIndex = index;
        }

        public static bool TryConsumeRequest(out int index)
        {
            index = s_RequestedIndex;
            s_RequestedIndex = -1;
            return index >= 0;
        }

        public static void NotifyChanged(int index)
        {
            CurrentIndex = index;
            Changed?.Invoke(index);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticData()
        {
            s_RequestedIndex = -1;
            CurrentIndex = 0;
            Changed = null;
        }
    }
}
