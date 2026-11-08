using System.IO;
using UnityEngine;

namespace Unity.MegacityMetro.Tests
{
    /// <summary>Helpers for "source contract" tests that assert on files the runtime cannot introspect.</summary>
    internal static class TestPaths
    {
        public static string Script(string relativeToScripts)
        {
            return Path.Combine(Application.dataPath, "Scripts", relativeToScripts);
        }

        public static string ReadScript(string relativeToScripts)
        {
            var path = Script(relativeToScripts);
            NUnit.Framework.Assert.That(File.Exists(path), NUnit.Framework.Is.True, $"Expected source file at {path}");
            return File.ReadAllText(path);
        }
    }
}
