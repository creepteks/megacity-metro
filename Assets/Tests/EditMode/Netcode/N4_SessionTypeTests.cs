using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Unity.MegacityMetro.Tests
{
    /// <summary>
    /// Known issue N4: client and server must agree on the session Type string. ServerManager only compiles for
    /// server builds, so this is a source contract read from both files.
    /// </summary>
    [TestFixture]
    [Category("Netcode")]
    [Category("SourceContract")]
    public class N4_SessionTypeTests
    {
        private static string ExtractSessionType(string source, string file)
        {
            var match = Regex.Match(source, @"k_SessionType\s*=\s*""([^""]+)""");
            Assert.That(match.Success, Is.True, $"Could not find k_SessionType in {file}");
            return match.Groups[1].Value;
        }

        [Test]
        [Ignore("Known open issue N4: client uses \"MegacityMetroSession\", server uses \"NFESession\". Remove Ignore when a shared constant is introduced.")]
        public void ClientAndServer_UseTheSameSessionType()
        {
            var client = ExtractSessionType(TestPaths.ReadScript("Gameplay/Mix/Multiplayer/SDK/Client/ClientManager.cs"), "ClientManager.cs");
            var server = ExtractSessionType(TestPaths.ReadScript("Gameplay/Mix/Multiplayer/SDK/Server/ServerManager.cs"), "ServerManager.cs");

            Assert.That(client, Is.EqualTo(server), "Session Type must be a single shared constant (N4).");
        }
    }
}
