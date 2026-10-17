using System;
using FinitePool.Engine.Building;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Placement;
using FinitePool.Engine.Sizing;
using FinitePool.Tests.Fakes;
using NUnit.Framework;

namespace FinitePool.Tests
{
    public class DealBuilderTests
    {
        private FakeClock m_clock;
        private CapturingLog m_log;

        [SetUp]
        public void SetUp()
        {
            m_clock = new FakeClock();
            m_log = new CapturingLog();
        }

        [Test]
        public void Inner100_BuildsADealThatMatchesTheCalculator()
        {
            DealConfig config = TestConfigs.Placeholder();
            DealSize size = DealSizeCalculator.Calculate(config, GameMode.Inner, TestConfigs.Bet(100)).Size;

            BuildResult result = Builder().Build(config, GameMode.Inner, TestConfigs.Bet(100));

            Assert.That(result.IsSuccess, Is.True, result.Message);
            var deal = result.Deal;
            Assert.That(deal.TicketCount, Is.EqualTo(size.PaidSpins));
            Assert.That(DealInspection.Count(deal, TicketCategory.Regular), Is.EqualTo(size.RegularTickets));
            Assert.That(DealInspection.Count(deal, TicketCategory.Big), Is.EqualTo(size.BigTickets));
            Assert.That(DealInspection.Count(deal, TicketCategory.Loss), Is.EqualTo(size.LosingTickets));
            Assert.That(DealInspection.TotalAwards(deal), Is.EqualTo(size.TotalPayouts));
            Assert.That(deal.ConfigVersion, Is.EqualTo(config.ConfigVersion));
            Assert.That(deal.CreatedUtc, Is.EqualTo(m_clock.UtcNow));
            Assert.That(deal.Mode, Is.EqualTo(GameMode.Inner));
            Assert.That(deal.Bet, Is.EqualTo(TestConfigs.Bet(100)));
        }

        [Test]
        public void EveryCombination_Builds_AndPassesAnIndependentLimitCheck()
        {
            DealConfig config = TestConfigs.Placeholder();
            DealBuilder builder = Builder();

            foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
            {
                foreach (BetLevel bet in config.BetLevels)
                {
                    BuildResult result = builder.Build(config, mode, bet);

                    Assert.That(result.IsSuccess, Is.True, result.Message);
                    Assert.That(DealInspection.FindLimitBreaks(result.Deal, result.Limits), Is.Empty, $"{mode} {bet}");
                    Assert.That(DealInspection.TotalAwards(result.Deal), Is.EqualTo(result.Size.TotalPayouts), $"{mode} {bet}");
                }
            }
        }

        [Test]
        public void ShuffleAndCheck_StillWorks_ForASmallDeal()
        {
            DealConfig config = TestConfigs.PlaceholderWith(root => root["placementLimits"]["layoutStrategy"] = "ShuffleAndCheck");

            BuildResult result = Builder().Build(config, GameMode.Inner, TestConfigs.Bet(100));

            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(DealInspection.FindLimitBreaks(result.Deal, result.Limits), Is.Empty);
        }

        [Test]
        public void FailingEveryAttempt_StopsAtTheConfiguredCap_AndLogsTheRejection()
        {
            DealConfig config = TestConfigs.Placeholder();
            var generator = new AlwaysInvalidLayoutGenerator();

            BuildResult result = Builder(generator).Build(config, GameMode.Inner, TestConfigs.Bet(100));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(BuildFailureReason.AttemptsExhausted));
            Assert.That(generator.Calls, Is.EqualTo(1000), "brief §2: maximum 1,000 attempts per deal");
            Assert.That(result.Attempts, Is.EqualTo(1000));
            Assert.That(m_log.Errors, Has.Count.EqualTo(1));
            Assert.That(m_log.Errors[0], Does.Contain("1000 attempts").And.Contain("consecutive losses"));
        }

        [Test]
        public void AttemptCap_IsConfigurable()
        {
            DealConfig config = TestConfigs.PlaceholderWith(root => root["maxGenerationAttempts"] = 25);
            var generator = new AlwaysInvalidLayoutGenerator();

            BuildResult result = Builder(generator).Build(config, GameMode.Inner, TestConfigs.Bet(100));

            Assert.That(generator.Calls, Is.EqualTo(25));
            Assert.That(result.FailureReason, Is.EqualTo(BuildFailureReason.AttemptsExhausted));
        }

        [Test]
        public void ImpossibleLimits_AreRejected_WithoutSpendingAnAttempt()
        {
            DealConfig config = TestConfigs.PlaceholderWith(root => root["placementLimits"]["maxConsecutiveLossesOverride"] = 5);
            var generator = new AlwaysInvalidLayoutGenerator();

            BuildResult result = Builder(generator).Build(config, GameMode.Inner, TestConfigs.Bet(100));

            Assert.That(result.FailureReason, Is.EqualTo(BuildFailureReason.LimitsImpossible));
            Assert.That(result.Attempts, Is.EqualTo(0));
            Assert.That(generator.Calls, Is.EqualTo(0));
            Assert.That(m_log.Errors, Has.Some.Contains("cannot fit"));
        }

        [Test]
        public void SizeRejection_IsReported_AsABuildFailure()
        {
            DealConfig config = TestConfigs.PlaceholderWith(root =>
                root["modes"]["Inner"]["regularBag"]["tickets"][0]["multiplier"] = "0.5");

            BuildResult result = Builder().Build(config, GameMode.Inner, TestConfigs.Bet(25));

            Assert.That(result.FailureReason, Is.EqualTo(BuildFailureReason.SizeRejected));
            Assert.That(result.Message, Does.Contain(nameof(DealSizeRejection.FractionalCentAward)));
        }

        [Test]
        public void TwoBuilds_GiveDifferentDealIds_AndDifferentOrders()
        {
            DealConfig config = TestConfigs.Placeholder();
            DealBuilder builder = Builder();

            var first = builder.Build(config, GameMode.Outer, TestConfigs.Bet(100)).Deal;
            var second = builder.Build(config, GameMode.Outer, TestConfigs.Bet(100)).Deal;

            Assert.That(first.DealId, Is.Not.EqualTo(second.DealId));
            Assert.That(first.TicketTypeIds, Is.Not.EqualTo(second.TicketTypeIds), "1,360 tickets in the same order twice is practically impossible");
        }

        private DealBuilder Builder(ICategoryLayoutGenerator generator = null) =>
            new DealBuilder(new CryptoRandomSource(), m_clock, m_log, generator);
    }

    public class GapRepairLayoutGeneratorTests
    {
        [Test]
        public void Output_HasExactCounts_AndNeverBreaksALimit_EvenWhenTight()
        {
            // Inner $1.00 with at most 6 consecutive losses: 52 losses into 9 runs (capacity 54) is nearly full.
            var limits = new PlacementLimits(maxConsecutiveLosses: 6, maxRegularGap: 17, maxBigGap: 40, repeatingIntervalMaxPeriod: 4);
            var generator = new GapRepairLayoutGenerator();
            var random = new CryptoRandomSource();
            int produced = 0;

            for (int attempt = 0; attempt < 200; attempt++)
            {
                TicketCategory[] layout = generator.TryGenerate(6, 2, 52, limits, random);
                if (layout == null)
                {
                    continue;
                }

                produced++;
                PlacementReport report = PlacementValidator.Validate(layout, limits);
                Assert.That((report.RegularCount, report.BigCount, report.LossCount), Is.EqualTo((6, 2, 52)));
                Assert.That(report.LongestLossRun, Is.LessThanOrEqualTo(6));
                Assert.That(report.LongestRegularGap, Is.LessThanOrEqualTo(17));
                Assert.That(report.LongestBigGap, Is.LessThanOrEqualTo(40));
            }

            Assert.That(produced, Is.GreaterThan(0));
        }
    }
}
