using Unity.Entities;
using Unity.MegacityMetro.Utils;
using Unity.NetCode;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace Unity.MegacityMetro.Gameplay
{
    /// <summary>
    /// Applies flight profile requests made through <see cref="FlightProfileSelection"/>:
    /// multiplayer → <see cref="SetFlightProfileRequest"/> RPC (server validates and replicates the index back),
    /// single player → writes <see cref="FlightProfileRef"/> on the local ship.
    /// Restores the saved selection on spawn and persists new selections to user preferences.
    /// Dev builds: F1–F4 select profiles 0–3.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
    public partial class FlightProfileRequestSystem : SystemBase
    {
        private bool m_RestoredSavedSelection;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<FlightProfileTableRef>();
        }

        protected override void OnUpdate()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.f1Key.wasPressedThisFrame) FlightProfileSelection.Request(0);
                else if (keyboard.f2Key.wasPressedThisFrame) FlightProfileSelection.Request(1);
                else if (keyboard.f3Key.wasPressedThisFrame) FlightProfileSelection.Request(2);
                else if (keyboard.f4Key.wasPressedThisFrame) FlightProfileSelection.Request(3);
            }
#endif

            // Find the local ship (the only entity with GhostOwnerIsLocal enabled)
            var localShip = Entity.Null;
            var profileCount = 0;
            var currentIndex = 0;
            foreach (var (table, profileRef, entity) in SystemAPI.Query<RefRO<FlightProfileTableRef>, RefRO<FlightProfileRef>>()
                         .WithAll<GhostOwnerIsLocal>().WithEntityAccess())
            {
                localShip = entity;
                profileCount = table.ValueRO.Count;
                currentIndex = profileRef.ValueRO.Index;
                break;
            }

            if (localShip == Entity.Null)
                return;

            if (!m_RestoredSavedSelection)
            {
                m_RestoredSavedSelection = true;
                FlightProfileSelection.CurrentIndex = currentIndex;
#if !UNITY_SERVER
                if (PersistentDataManager.Instance != null)
                {
                    var saved = PersistentDataManager.Instance.GetControlSettings().FlightProfileIndex;
                    if (saved != currentIndex)
                        FlightProfileSelection.Request(saved);
                }
#endif
            }

            if (!FlightProfileSelection.TryConsumeRequest(out var requested))
                return;

            if (requested < 0 || requested >= profileCount)
            {
                Debug.LogWarning($"[FlightProfile] Ignoring request for profile {requested}; table has {profileCount} entries.");
                return;
            }

            if (World.IsClient())
            {
                var request = EntityManager.CreateEntity();
                EntityManager.AddComponent<SendRpcCommandRequest>(request);
                EntityManager.AddComponentData(request, new SetFlightProfileRequest { Index = (byte) requested });
            }
            else
            {
                // Single player: no server, write the component directly.
                EntityManager.SetComponentData(localShip, new FlightProfileRef { Index = (byte) requested });
            }

            FlightProfileSelection.NotifyChanged(requested);

#if !UNITY_SERVER
            if (PersistentDataManager.Instance != null)
            {
                var data = PersistentDataManager.Instance.GetControlSettings();
                if (data.FlightProfileIndex != requested)
                {
                    data.FlightProfileIndex = requested;
                    PersistentDataManager.Instance.SaveControlSettings(data);
                }
            }
#endif
        }
    }
}
