using FinitePool.Engine.Building;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Infrastructure;
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
        private static readonly (GameMode Mode, long BetCents)[] SampleCombinations =
        {
            (GameMode.Inner, 25), (GameMode.Inner, 100), (GameMode.Inner, 475),
            (GameMode.Outer, 100), (GameMode.Outer, 175), (GameMode.Outer, 475),
        };

        [Test]
        public void Scenario02_CycleIsolation_Regular()
        {
            AssertNoCycleMixing(TicketCategory.Regular);
        }

        [Test]
        public void Scenario02_CycleIsolation_Big()
        {
            AssertNoCycleMixing(TicketCategory.Big);
        }

        [Test]
        public void Scenario03a_GeneratedDeals_SatisfyPlacementLimits()
        {
            DealConfig config = TestConfigs.Placeholder();
            DealBuilder builder = NewBuilder();

            foreach ((GameMode mode, long betCents) in SampleCombinations)
            {
                for (int build = 0; build < 5; build++)
                {
                    BuildResult result = builder.Build(config, mode, TestConfigs.Bet(betCents));

                    Assert.That(result.IsSuccess, Is.True, result.Message);
                    Assert.That(result.Placement.IsValid, Is.True, result.Placement.ToString());
                    Assert.That(DealInspection.FindLimitBreaks(result.Deal, result.Limits), Is.Empty, $"{mode} {betCents}c build {build}");
                }
            }
        }

        [Test]
        public void Scenario03b_Inner100_MaxConsecutiveLosses5_RejectedAsImpossible()
        {
            DealConfig config = TestConfigs.PlaceholderWith(root =>
                root["placementLimits"]["maxConsecutiveLossesOverride"] = GoldenDealSizes.INNER_100_IMPOSSIBLE_MAX_LOSS_RUN);

            BuildResult result = NewBuilder().Build(config, GameMode.Inner, TestConfigs.Bet(100));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(BuildFailureReason.LimitsImpossible));
            Assert.That(result.Attempts, Is.EqualTo(0), "rejected before generating, not after 1,000 failed attempts");
        }

        [Test]
        public void Scenario10_Outer175_OnePairedCycle_RejectedNotRounded()
        {
            DealConfig config = TestConfigs.Placeholder();

            DealSizeResult result = DealSizeCalculator.CalculateForCycles(config, GameMode.Outer, TestConfigs.Bet(175), 1);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Rejection, Is.EqualTo(DealSizeRejection.FractionalTickets));
            Assert.That(result.Message, Does.Contain(GoldenDealSizes.OUTER_175_ONE_CYCLE_SPINS));
        }

        private static void AssertNoCycleMixing(TicketCategory category)
        {
            DealConfig config = TestConfigs.Placeholder();
            DealBuilder builder = NewBuilder();

            foreach ((GameMode mode, long betCents) in SampleCombinations)
            {
                for (int build = 0; build < 5; build++)
                {
                    BuildResult result = builder.Build(config, mode, TestConfigs.Bet(betCents));
                    Assert.That(result.IsSuccess, Is.True, result.Message);

                    string mixing = DealInspection.FindCycleMixing(result.Deal, category);
                    Assert.That(mixing, Is.Null, $"{mode} {betCents}c build {build}: {mixing}");
                }
            }
        }

        private static DealBuilder NewBuilder() =>
            new DealBuilder(new CryptoRandomSource(), new FakeClock(), NullEngineLog.Instance);
    }
}
