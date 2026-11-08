using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Entities;
using Unity.MegacityMetro.Gameplay;
using Unity.MegacityMetro.Traffic;
using Unity.NetCode;

namespace Unity.MegacityMetro.Tests
{
    /// <summary>
    /// Which worlds a system runs in. Covers N8 (visual spawning on the server), G1 (traffic is client-only by
    /// design and must stay out of the server unless made authoritative) and N13 (input job must target the
    /// local ship only).
    /// </summary>
    [TestFixture]
    [Category("Netcode")]
    public class SystemWorldFilterTests
    {
        private static IReadOnlyList<Type> ServerSystems => DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.ServerSimulation);
        private static IReadOnlyList<Type> ClientSystems => DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.ClientSimulation);

        [Test]
        public void G1_TrafficSystems_RunOnClientOnly()
        {
            Assert.That(ClientSystems, Does.Contain(typeof(TrafficSystem)));
            Assert.That(ClientSystems, Does.Contain(typeof(TrafficSetupSystem)));
            Assert.That(ServerSystems.Contains(typeof(TrafficSystem)), Is.False,
                "Traffic is cosmetic and client-only (G1). If this fails, traffic became server-side: re-evaluate the prediction-correction analysis.");
            Assert.That(ServerSystems.Contains(typeof(TrafficSetupSystem)), Is.False);
        }

        [Test]
        public void MovementAndShooting_RunOnBothServerAndClient()
        {
            foreach (var type in new[] { typeof(PlayerVehicleControlSystem), typeof(ReconstructVehicleRotationSystem), typeof(ApplyVehicleRotationAsVelocitySystem), typeof(ShootingSystem) })
            {
                Assert.That(ServerSystems, Does.Contain(type), $"{type.Name} must run on the server");
                Assert.That(ClientSystems, Does.Contain(type), $"{type.Name} must run in client prediction");
            }
        }

        [Test]
        public void ServerOnlySystems_AreNotCreatedOnClients()
        {
            foreach (var type in new[] { typeof(VehicleHealthAndImmunitySystem), typeof(ShootingServerSystem), typeof(SetPlayerInfoSystem), typeof(SetFlightProfileSystem) })
            {
                Assert.That(ServerSystems, Does.Contain(type), $"{type.Name} must run on the server");
                Assert.That(ClientSystems.Contains(type), Is.False, $"{type.Name} is authoritative and must not run on clients");
            }
        }

        [Test]
        [Ignore("Known open issue N8: RandomShipVisualsSystem has no WorldSystemFilter and instantiates visual prefabs on the server. Remove Ignore when the system is split/filtered.")]
        public void N8_RandomShipVisualsSystem_DoesNotRunOnServer()
        {
            Assert.That(ServerSystems.Contains(typeof(RandomShipVisualsSystem)), Is.False);
            Assert.That(ClientSystems, Does.Contain(typeof(RandomShipVisualsSystem)));
        }

        private static IEnumerable<Type> WithAllTypes(Type jobType)
        {
            foreach (var attribute in jobType.GetCustomAttributesData())
            {
                if (attribute.AttributeType.Name != "WithAllAttribute")
                    continue;
                foreach (var argument in attribute.ConstructorArguments)
                {
                    if (argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> array)
                    {
                        foreach (var element in array)
                            yield return (Type) element.Value;
                    }
                    else if (argument.Value is Type single)
                    {
                        yield return single;
                    }
                }
            }
        }

        [Test]
        [Ignore("Known open issue N13: PlayerVehicleInputJob writes the local input to every entity with PlayerVehicleInput. Remove Ignore when [WithAll(typeof(GhostOwnerIsLocal))] is added.")]
        public void N13_PlayerVehicleInputJob_TargetsOnlyTheLocalShip()
        {
            Assert.That(WithAllTypes(typeof(PlayerVehicleInputJob)).ToList(), Does.Contain(typeof(GhostOwnerIsLocal)),
                "Without the filter, switching remote ghosts to predicted mode injects the local input into them (N13).");
        }
    }
}
