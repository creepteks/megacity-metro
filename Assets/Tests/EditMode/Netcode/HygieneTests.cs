using System;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Unity.MegacityMetro.Tests
{
    /// <summary>Known issues N11 (dead matchmaker code) and N12 (UnityEditor using in a runtime file).</summary>
    [TestFixture]
    [Category("Netcode")]
    [Category("SourceContract")]
    public class HygieneTests
    {
        [Test]
        [Ignore("Known open issue N11: ClientMatchmaker is dead code since the move to sessions. Remove Ignore after deleting it.")]
        public void N11_LegacyClientMatchmaker_IsRemoved()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.Name == "ClientMatchmaker");
            Assert.That(type, Is.Null, "ClientMatchmaker should be deleted (N11).");
        }

        [Test]
        [Ignore("Known open issue N12: PlayModeContext.cs has 'using UnityEditor;' outside #if UNITY_EDITOR. Remove Ignore when guarded.")]
        public void N12_PlayModeContext_HasNoUnguardedEditorUsing()
        {
            var source = TestPaths.ReadScript("Gameplay/Mix/Multiplayer/SDK/Utils/PlayModeContext.cs");
            // Accept the using only when it is wrapped in an editor define.
            var unguarded = Regex.IsMatch(source, @"^\s*using\s+UnityEditor\s*;", RegexOptions.Multiline)
                            && !Regex.IsMatch(source, @"#if\s+UNITY_EDITOR[\s\S]*?using\s+UnityEditor\s*;[\s\S]*?#endif");
            Assert.That(unguarded, Is.False, "Runtime assemblies must not reference UnityEditor unguarded (N12).");
        }
    }
}
