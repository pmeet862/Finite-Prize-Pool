using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FinitePool.Engine;
using NUnit.Framework;
using UnityEngine;

namespace FinitePool.Tests
{
    /// <summary>
    /// Guards the brief's structural rules (§3.1, §3.2): the engine is plain C#
    /// with no UnityEngine dependency, and it never uses a non-cryptographic RNG.
    /// </summary>
    public class EngineHygieneTests
    {
        private static readonly Regex ForbiddenSourcePattern = new Regex(
            @"UnityEngine|System\.Random|new\s+Random\s*\(",
            RegexOptions.Compiled);

        private static string EngineSourceFolder => Path.Combine(Application.dataPath, "FinitePool", "Engine");

        [Test]
        public void EngineAssembly_HasNoUnityReferences()
        {
            var references = typeof(EngineAssembly).Assembly.GetReferencedAssemblies();

            var unityReferences = references
                .Where(reference => reference.Name.StartsWith("UnityEngine") || reference.Name.StartsWith("UnityEditor"))
                .Select(reference => reference.Name)
                .ToList();

            Assert.That(unityReferences, Is.Empty, "The engine must stay plain C# so it can move to a server.");
        }

        [Test]
        public void TicketTypes_AreNotPublic_SoTheGameCanNotReadTheTicketList()
        {
            // Brief §3.1 and §5: the UI must never read the ticket list. Keeping these types internal
            // makes that a compile error in the Game, debug panel and Editor assemblies.
            Assert.That(typeof(FinitePool.Engine.Deals.Deal).IsPublic, Is.False);
            Assert.That(typeof(FinitePool.Engine.Deals.TicketType).IsPublic, Is.False);

            var debugPanelFacts = typeof(FinitePool.Engine.Service.DealInfo).GetProperties().Select(property => property.Name);
            Assert.That(debugPanelFacts, Is.EquivalentTo(new[] { "DealId", "State", "ConfigVersion", "SpinsPlayed" }),
                "the debug panel may show these four facts and nothing about upcoming tickets");
        }

        [Test]
        public void EngineSource_UsesNoUnityApisOrNonCryptoRandom()
        {
            Assert.That(Directory.Exists(EngineSourceFolder), Is.True, $"Engine folder not found: {EngineSourceFolder}");

            var offenders = Directory.GetFiles(EngineSourceFolder, "*.cs", SearchOption.AllDirectories)
                .SelectMany(file => File.ReadAllLines(file)
                    .Select((line, index) => (file, line, lineNumber: index + 1)))
                .Where(entry => ForbiddenSourcePattern.IsMatch(entry.line))
                .Select(entry => $"{Path.GetFileName(entry.file)}:{entry.lineNumber}: {entry.line.Trim()}")
                .ToList();

            Assert.That(offenders, Is.Empty,
                "Engine code must not reference UnityEngine or System.Random. Shuffles use RandomNumberGenerator.GetInt32.");
        }
    }
}
