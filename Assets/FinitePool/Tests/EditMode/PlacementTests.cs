using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Placement;
using FinitePool.Engine.Sizing;
using FinitePool.Tests.Fakes;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FinitePool.Tests
{
    public class PlacementLimitsTests
    {
        [Test]
        public void Inner100_DefaultLimits_FollowTheBriefFormulas()
        {
            DealConfig config = TestConfigs.Shipped();
            DealSize size = DealSizeCalculator.Calculate(config, GameMode.Inner, TestConfigs.Bet(100)).Size;

            PlacementLimits limits = PlacementLimits.For(size, config.PlacementLimits);

            // N = 60, P = 8 (R = 6, B = 2), L = 52.
            Assert.That(limits.MaxConsecutiveLosses, Is.EqualTo(11), "floor(2 × 52 ÷ 9) = floor(11.56)");
            Assert.That(limits.MaxRegularGap, Is.EqualTo(17), "floor(2 × 60 ÷ 7) = floor(17.14)");
            Assert.That(limits.MaxBigGap, Is.EqualTo(40), "floor(2 × 60 ÷ 3) = 40");
        }

        [Test]
        public void Overrides_ReplaceTheFormula()
        {
            DealConfig config = TestConfigs.ShippedWith(root =>
            {
                root["placementLimits"]["maxConsecutiveLossesOverride"] = 7;
                root["placementLimits"]["maxRegularGapOverride"] = 12;
                root["placementLimits"]["maxBigGapOverride"] = 30;
            });
            DealSize size = DealSizeCalculator.Calculate(config, GameMode.Inner, TestConfigs.Bet(100)).Size;

            PlacementLimits limits = PlacementLimits.For(size, config.PlacementLimits);

            Assert.That(limits.MaxConsecutiveLosses, Is.EqualTo(7));
            Assert.That(limits.MaxRegularGap, Is.EqualTo(12));
            Assert.That(limits.MaxBigGap, Is.EqualTo(30));
        }
    }

    public class PlacementValidatorTests
    {
        private static readonly PlacementLimits Limits = new PlacementLimits(
            maxConsecutiveLosses: 2, maxRegularGap: 3, maxBigGap: 5, repeatingIntervalMaxPeriod: 4);

        [Test]
        public void LayoutWithinAllLimits_IsValid()
        {
            PlacementReport report = Validate("L R L B R LL R L");

            Assert.That(report.IsValid, Is.True, report.ToString());
            Assert.That(report.RegularCount, Is.EqualTo(3));
            Assert.That(report.BigCount, Is.EqualTo(1));
            Assert.That(report.LossCount, Is.EqualTo(5));
            Assert.That(report.LongestLossRun, Is.EqualTo(2));
            Assert.That(report.LongestRegularGap, Is.EqualTo(2));
            Assert.That(report.LongestBigGap, Is.EqualTo(5), "trailing Big gap at exactly the limit is allowed");
        }

        [Test]
        public void LossRun_OverTheLimit_IsReported()
        {
            PlacementReport report = Validate("L R LLL B R");

            Assert.That(report.LongestLossRun, Is.EqualTo(3));
            Assert.That(report.IsValid, Is.False);
            Assert.That(report.Violations, Has.Some.Contains("consecutive losses"));
        }

        [Test]
        public void LeadingAndTrailingRuns_Count()
        {
            Assert.That(Validate("LLL R B").LongestLossRun, Is.EqualTo(3), "leading run");
            Assert.That(Validate("R B LLL").LongestLossRun, Is.EqualTo(3), "trailing run");
            Assert.That(Validate("B LLLL R").LongestRegularGap, Is.EqualTo(5), "leading Regular gap includes the Big slot");
            Assert.That(Validate("R LL B LL").LongestRegularGap, Is.EqualTo(5), "trailing Regular gap");
        }

        [Test]
        public void RegularGap_CountsBigSlotsAndLosses_BetweenRegularSlots()
        {
            PlacementReport report = Validate("R L B L L R");

            Assert.That(report.LongestRegularGap, Is.EqualTo(4));
            Assert.That(report.Violations, Has.Some.Contains("Regular gap"));
        }

        [Test]
        public void BigGap_CountsRegularSlotsAndLosses_BetweenBigSlots()
        {
            PlacementReport report = Validate("B R L R L R B");

            Assert.That(report.LongestBigGap, Is.EqualTo(5));
            Assert.That(report.LongestRegularGap, Is.EqualTo(1));
            Assert.That(Validate("B R L R L R L B").Violations, Has.Some.Contains("Big gap"));
        }

        [Test]
        public void EvenlySpacedPrizes_AreAFixedRepeatingInterval()
        {
            PlacementReport report = Validate("L R L R L B L R L");

            Assert.That(report.RepeatingPeriod, Is.EqualTo(1));
            Assert.That(report.IsValid, Is.False);
        }

        private static PlacementReport Validate(string layout) => PlacementValidator.Validate(Layouts.Parse(layout), Limits);
    }

    public class RepeatingIntervalDetectorTests
    {
        [TestCase(new[] { 5, 10, 15 }, 4, 1, TestName = "Equal spacing 5,5 → period 1")]
        [TestCase(new[] { 0, 3, 10, 13, 20, 23 }, 4, 2, TestName = "Spacing 3,7,3,7,3 → period 2")]
        [TestCase(new[] { 0, 1, 3, 6, 7, 9, 12 }, 4, 3, TestName = "Spacing 1,2,3,1,2,3 → period 3")]
        [TestCase(new[] { 0, 2, 7, 8, 15, 16 }, 4, 0, TestName = "Irregular spacing → none")]
        [TestCase(new[] { 0, 4 }, 4, 0, TestName = "Two prizes can't show a pattern")]
        [TestCase(new[] { 0, 1, 3, 6, 10, 11, 13, 16, 20, 21, 23, 26 }, 3, 0, TestName = "Period 4 pattern ignored when max period is 3")]
        [TestCase(new[] { 0, 1, 3, 6, 10, 11, 13, 16, 20, 21, 23, 26 }, 4, 4, TestName = "Period 4 pattern found when max period is 4")]
        [TestCase(new[] { 0, 1, 3, 6, 7, 9 }, 4, 0, TestName = "Pattern seen less than twice → none")]
        public void FindRepeatingPeriod(int[] positions, int maxPeriod, int expectedPeriod)
        {
            Assert.That(RepeatingIntervalDetector.FindRepeatingPeriod(positions, maxPeriod), Is.EqualTo(expectedPeriod));
        }
    }

    public class PlacementFeasibilityTests
    {
        [Test]
        public void Inner100_DefaultLimits_AreFeasible()
        {
            (DealSize size, PlacementLimits limits) = Inner100With(null);

            Assert.That(PlacementFeasibility.FindImpossibility(size, limits), Is.Null);
        }

        [Test]
        public void TooManyLossesForTheRunLimit_IsImpossible()
        {
            // L = 52 losses, P + 1 = 9 runs of at most 5 → capacity 45.
            (DealSize size, PlacementLimits limits) = Inner100With(5);

            Assert.That(PlacementFeasibility.FindImpossibility(size, limits), Does.Contain("cannot fit"));
        }

        [Test]
        public void FullLossRuns_ForceAFixedInterval_AndAreImpossible()
        {
            // One x3 Regular + one x3 Big pays $6 per paired cycle at $1 → 3 cycles: N = 20, P = 6, L = 14.
            DealSize size = SizeFor(regularTickets: "[{ 'kind': 'Multiplier', 'multiplier': '3', 'count': 1 }]",
                                    bigTickets: "[{ 'kind': 'Multiplier', 'multiplier': '3', 'count': 1 }]");
            Assert.That((size.PaidSpins, size.PrizeTickets, size.LosingTickets), Is.EqualTo((20L, 6L, 14L)));

            // 14 losses in 7 runs of at most 2: every run must be exactly 2, so prizes land every 3rd slot.
            var full = new PlacementLimits(maxConsecutiveLosses: 2, maxRegularGap: 20, maxBigGap: 20, repeatingIntervalMaxPeriod: 4);
            var slack = new PlacementLimits(maxConsecutiveLosses: 3, maxRegularGap: 20, maxBigGap: 20, repeatingIntervalMaxPeriod: 4);

            Assert.That(PlacementFeasibility.FindImpossibility(size, full), Does.Contain("fixed repeating interval"));
            Assert.That(PlacementFeasibility.FindImpossibility(size, slack), Is.Null);
        }

        [Test]
        public void NoLossesAtAll_IsAForcedFixedInterval()
        {
            // Ten x0.9 tickets pay $9 at $1 → N = 10 = P, so every slot is a prize (interval 1).
            DealSize size = SizeFor(regularTickets: "[{ 'kind': 'Multiplier', 'multiplier': '0.9', 'count': 9 }]",
                                    bigTickets: "[{ 'kind': 'Multiplier', 'multiplier': '0.9', 'count': 1 }]");
            Assert.That(size.LosingTickets, Is.EqualTo(0));

            DealConfig config = TestConfigs.Shipped();
            PlacementLimits limits = PlacementLimits.For(size, config.PlacementLimits);

            Assert.That(PlacementFeasibility.FindImpossibility(size, limits), Does.Contain("fixed repeating interval"));
        }

        [Test]
        public void TooFewRegularSlotsForTheGapLimit_IsImpossible()
        {
            DealConfig config = TestConfigs.ShippedWith(root => root["placementLimits"]["maxRegularGapOverride"] = 7);
            DealSize size = DealSizeCalculator.Calculate(config, GameMode.Inner, TestConfigs.Bet(100)).Size;
            PlacementLimits limits = PlacementLimits.For(size, config.PlacementLimits);

            // 54 non-Regular slots into 7 gaps of at most 7 → capacity 49.
            Assert.That(PlacementFeasibility.FindImpossibility(size, limits), Does.Contain("non-Regular"));
        }

        private static DealSize SizeFor(string regularTickets, string bigTickets)
        {
            DealConfig config = TestConfigs.ShippedWith(root =>
            {
                root["modes"]["Inner"]["regularBag"]["tickets"] = JArray.Parse(regularTickets);
                root["modes"]["Inner"]["bigBag"]["tickets"] = JArray.Parse(bigTickets);
            });
            DealSizeResult result = DealSizeCalculator.Calculate(config, GameMode.Inner, TestConfigs.Bet(100));
            Assert.That(result.IsAccepted, Is.True, result.Message);
            return result.Size;
        }

        private static (DealSize, PlacementLimits) Inner100With(int? maxConsecutiveLosses)
        {
            DealConfig config = TestConfigs.ShippedWith(root =>
                root["placementLimits"]["maxConsecutiveLossesOverride"] = maxConsecutiveLosses);
            DealSize size = DealSizeCalculator.Calculate(config, GameMode.Inner, TestConfigs.Bet(100)).Size;
            return (size, PlacementLimits.For(size, config.PlacementLimits));
        }
    }
}
