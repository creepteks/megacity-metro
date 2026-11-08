using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Unity.MegacityMetro.Tests
{
    /// <summary>
    /// Known issue N10: the player ghost's base Importance (10) sits in the project's own "Medium" band; player
    /// ghosts are suggested at 50–250. Matters now that MinSendImportance is an absolute threshold on the server.
    /// </summary>
    [TestFixture]
    [Category("Netcode")]
    [Category("Assets")]
    public class N10_GhostImportanceTests
    {
        private const string PrefabPath = "Assets/Prefabs/Vehicles/VehicleControlMultiplayer.prefab";

        private static int ReadImportance()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing {PrefabPath}");

            var ghostAuthoring = prefab.GetComponents<MonoBehaviour>().FirstOrDefault(c => c != null && c.GetType().Name == "GhostAuthoringComponent");
            Assert.That(ghostAuthoring, Is.Not.Null, "Player prefab must have a GhostAuthoringComponent");

            var field = ghostAuthoring.GetType().GetField("Importance");
            Assert.That(field, Is.Not.Null, "GhostAuthoringComponent.Importance not found (Netcode API change?)");
            return (int) field.GetValue(ghostAuthoring);
        }

        [Test]
        public void PlayerGhost_HasPositiveImportance()
        {
            Assert.That(ReadImportance(), Is.GreaterThan(0));
        }

        [Test]
        [Ignore("Known open issue N10: Importance is 10. Decide the value together with MinSendImportance (N2) and remove Ignore.")]
        public void PlayerGhost_ImportanceIsInHighBand()
        {
            Assert.That(ReadImportance(), Is.InRange(50, 250));
        }
    }
}
