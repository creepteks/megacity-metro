using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MegacityMetro.Gameplay
{
    /// <summary>
    /// Server side of flight profile switching: consumes <see cref="SetFlightProfileRequest"/>, finds the sender's
    /// ship by <see cref="GhostOwner"/>, validates the index against the baked table and writes
    /// <see cref="FlightProfileRef"/>. The new index replicates to all clients; the owner's prediction picks it up
    /// on the next tick.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct SetFlightProfileSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SetFlightProfileRequest>();
            state.RequireForUpdate<ReceiveRpcCommandRequest>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (request, rpc, requestEntity) in
                     SystemAPI.Query<RefRO<SetFlightProfileRequest>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                ecb.DestroyEntity(requestEntity);

                var connection = rpc.ValueRO.SourceConnection;
                if (!SystemAPI.HasComponent<NetworkId>(connection))
                    continue;

                var networkId = SystemAPI.GetComponent<NetworkId>(connection).Value;
                var requestedIndex = request.ValueRO.Index;

                foreach (var (owner, table, profileRef) in
                         SystemAPI.Query<RefRO<GhostOwner>, RefRO<FlightProfileTableRef>, RefRW<FlightProfileRef>>())
                {
                    if (owner.ValueRO.NetworkId != networkId)
                        continue;

                    if (requestedIndex < table.ValueRO.Count)
                    {
                        profileRef.ValueRW.Index = requestedIndex;
                    }
                    else
                    {
                        Debug.LogWarning($"[FlightProfile] Connection {networkId} requested profile {requestedIndex} but the table has {table.ValueRO.Count} entries.");
                    }
                    break;
                }
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
