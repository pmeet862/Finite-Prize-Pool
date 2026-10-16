using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Sizing;
using FinitePool.Tests.Fakes;
using NUnit.Framework;

namespace FinitePool.Tests
{
    /// <summary>
    /// The ten acceptance scenarios from brief §8.1, one test (or pair) each,
    /// named so the mapping is obvious. Scenarios are added as their phase lands:
    ///   10 → Phase 1 (calculator)
    ///   2, 3 → Phase 2 (builder + placement)
    ///   4, 5, 6 → Phase 4 (persistence + spins)
    ///   1, 7, 8, 9 → Phase 5 (lifecycle + retire)
    /// Numbers come from <see cref="GoldenDealSizes"/> (PLACEHOLDER until the math PDF arrives).
    /// </summary>
    public class ScenarioTests
    {
        [Test]
        public void Scenario10_Outer175_OnePairedCycle_RejectedNotRounded()
        {
            DealConfig config = TestConfigs.Placeholder();

            DealSizeResult result = DealSizeCalculator.CalculateForCycles(config, GameMode.Outer, TestConfigs.Bet(175), 1);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Rejection, Is.EqualTo(DealSizeRejection.FractionalTickets));
            Assert.That(result.Message, Does.Contain(GoldenDealSizes.OUTER_175_ONE_CYCLE_SPINS));
        }
    }
}
