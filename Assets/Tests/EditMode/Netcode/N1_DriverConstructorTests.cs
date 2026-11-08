using Gameplay;
using NUnit.Framework;
using Unity.MegacityMetro;
using Unity.NetCode;
using Unity.Networking.Transport;

namespace Unity.MegacityMetro.Tests
{
    /// <summary>
    /// Known issue N1: the 150-player driver constructor must be registered for every Netcode world and must
    /// raise the transport queue capacity. Fixed in commit 8811cef; these tests guard the fix.
    /// </summary>
    [TestFixture]
    [Category("Netcode")]
    public class N1_DriverConstructorTests
    {
        private INetworkStreamDriverConstructor m_Previous;

        [SetUp]
        public void SetUp() => m_Previous = NetworkStreamReceiveSystem.DriverConstructor;

        [TearDown]
        public void TearDown() => NetworkStreamReceiveSystem.DriverConstructor = m_Previous;

        [Test]
        public void RegisterDriverConstructor_InstallsProjectConstructor()
        {
            NetworkStreamReceiveSystem.DriverConstructor = null;

            NetCodeBootstrap.RegisterDriverConstructor();

            Assert.That(NetworkStreamReceiveSystem.DriverConstructor, Is.InstanceOf<MegacityMetroDriverConstructor>(),
                "NetCodeBootstrap.Initialize must register MegacityMetroDriverConstructor before worlds are created (N1).");
        }

        [Test]
        public void RegisterDriverConstructor_IsIdempotent()
        {
            NetCodeBootstrap.RegisterDriverConstructor();
            var first = NetworkStreamReceiveSystem.DriverConstructor;
            NetCodeBootstrap.RegisterDriverConstructor();

            Assert.That(NetworkStreamReceiveSystem.DriverConstructor, Is.SameAs(first));
        }

        [Test]
        public void ServerQueueCapacity_IsFivePacketsPerPlayerAtCap()
        {
            Assert.That(MegacityMetroDriverConstructor.ServerSendReceiveQueueCapacity, Is.EqualTo(NetCodeBootstrap.MaxPlayerCount * 5));
            Assert.That(NetCodeBootstrap.MaxPlayerCount, Is.EqualTo(150));
        }

        [Test]
        public void FixSettings_RaisesSendAndReceiveQueueCapacity()
        {
            var settings = DefaultDriverBuilder.GetNetworkServerSettings();
            try
            {
                Assert.That(settings.TryGet(out NetworkConfigParameter before), Is.True);

                MegacityMetroDriverConstructor.FixSettingsForMegacityMetro(settings, MegacityMetroDriverConstructor.ServerSendReceiveQueueCapacity);

                Assert.That(settings.TryGet(out NetworkConfigParameter after), Is.True);
                Assert.That(after.sendQueueCapacity, Is.EqualTo(MegacityMetroDriverConstructor.ServerSendReceiveQueueCapacity));
                Assert.That(after.receiveQueueCapacity, Is.EqualTo(MegacityMetroDriverConstructor.ServerSendReceiveQueueCapacity));
                Assert.That(after.sendQueueCapacity, Is.GreaterThanOrEqualTo(before.sendQueueCapacity),
                    "The project capacity must not be lower than the transport default.");
            }
            finally
            {
                settings.Dispose();
            }
        }
    }
}
