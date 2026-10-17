using System.Linq;
using FinitePool.Engine.Building;
using FinitePool.Tests.Fakes;
using NUnit.Framework;

namespace FinitePool.Tests
{
    public class FisherYatesTests
    {
        [Test]
        public void Shuffle_AsksForShrinkingRanges_FromTheEnd()
        {
            var items = new[] { 'a', 'b', 'c', 'd' };
            var random = new SequenceRandomSource(0, 0, 0);

            FisherYates.Shuffle(items, random);

            // i = 3, 2, 1 → ranges [0,4), [0,3), [0,2). Position 0 is never drawn for itself.
            Assert.That(random.RequestedRanges, Is.EqualTo(new[] { 4, 3, 2 }));
        }

        [Test]
        public void Shuffle_WithScriptedValues_GivesTheExpectedOrder()
        {
            var items = new[] { 'a', 'b', 'c', 'd' };

            // i=3 swaps with 0 → d b c a; i=2 swaps with 2 → d b c a; i=1 swaps with 0 → b d c a.
            FisherYates.Shuffle(items, new SequenceRandomSource(0, 2, 0));

            Assert.That(new string(items), Is.EqualTo("bdca"));
        }

        [Test]
        public void Shuffle_KeepsExactlyTheSameItems()
        {
            int[] items = Enumerable.Range(0, 1000).Select(i => i % 7).ToArray();
            int[] before = items.OrderBy(i => i).ToArray();

            FisherYates.Shuffle(items, new CryptoRandomSource());

            Assert.That(items.OrderBy(i => i).ToArray(), Is.EqualTo(before));
        }

        [Test]
        public void CryptoRandomSource_StaysInRange_AndHitsEveryValue()
        {
            var random = new CryptoRandomSource();
            var seen = new bool[5];

            for (int i = 0; i < 2000; i++)
            {
                int value = random.NextInt(5);
                Assert.That(value, Is.InRange(0, 4));
                seen[value] = true;
            }

            Assert.That(seen, Is.All.True);
        }

        /// <summary>
        /// Rough uniformity check: each of the 6 orders of 3 items should appear about
        /// 1/6 of the time. Explicit because it is statistical and slower.
        /// </summary>
        [Test, Explicit("Statistical smoke test")]
        public void Shuffle_AllOrdersOfThreeItems_AppearRoughlyEquallyOften()
        {
            const int TRIALS = 60000;
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            var random = new CryptoRandomSource();

            for (int trial = 0; trial < TRIALS; trial++)
            {
                var items = new[] { 'a', 'b', 'c' };
                FisherYates.Shuffle(items, random);
                string key = new string(items);
                counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
            }

            Assert.That(counts.Count, Is.EqualTo(6));
            foreach (int count in counts.Values)
            {
                Assert.That(count, Is.InRange(TRIALS / 6 * 0.95, TRIALS / 6 * 1.05));
            }
        }
    }
}
