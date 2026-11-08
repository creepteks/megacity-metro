using NUnit.Framework;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MegacityMetro.Tests
{
    /// <summary>
    /// Known issue N2: <see cref="GhostSendSystemData"/> must exist in the server world (where GhostSendSystem
    /// runs) and not be created in the client world. Fixed in commit 8811cef.
    /// Creates real Netcode worlds through the bootstrap helpers; no scene is loaded and the worlds are not updated.
    /// </summary>
    [TestFixture]
    [Category("Netcode")]
    [Category("NetcodeWorld")]
    public class N2_GhostSendSettingsTests
    {
        private World m_Server;
        private World m_Client;

        [TearDown]
        public void TearDown()
        {
            if (m_Server != null && m_Server.IsCreated) m_Server.Dispose();
            if (m_Client != null && m_Client.IsCreated) m_Client.Dispose();
            m_Server = null;
            m_Client = null;
        }

        [Test]
        public void ServerWorld_HasGhostSendSystemData_WithMinSendImportance2()
        {
            m_Server = ClientServerBootstrap.CreateServerWorld("N2TestServer");

            using var query = m_Server.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<GhostSendSystemData>());
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(1), "ServerInGame.OnCreate must create exactly one GhostSendSystemData singleton.");
            Assert.That(query.GetSingleton<GhostSendSystemData>().MinSendImportance, Is.EqualTo(2));
        }

        [Test]
        public void ClientWorld_DoesNotCreateGhostSendSystemData()
        {
            m_Client = ClientServerBootstrap.CreateClientWorld("N2TestClient");

            using var query = m_Client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<GhostSendSystemData>());
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(0), "GhostSendSystem never runs on clients; the singleton there is inert (N2).");
        }

        [Test]
        public void ServerWorld_HasTickRateAndImportanceGrid()
        {
            m_Server = ClientServerBootstrap.CreateServerWorld("N2TestServerRates");

            using var tickRate = m_Server.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ClientServerTickRate>());
            Assert.That(tickRate.CalculateEntityCount(), Is.EqualTo(1));
            var rate = tickRate.GetSingleton<ClientServerTickRate>();
            Assert.That(rate.SimulationTickRate, Is.EqualTo(RateSettings.tickRate));
            Assert.That(rate.NetworkTickRate, Is.EqualTo(RateSettings.tickRate));

            using var importance = m_Server.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<GhostImportance>(), ComponentType.ReadOnly<GhostDistanceData>());
            Assert.That(importance.CalculateEntityCount(), Is.EqualTo(1), "Distance importance scaling must be configured on the server (netcode.md §11).");
        }
    }
}
