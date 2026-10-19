using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Tests.Fakes;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FinitePool.Tests
{
    public class DealConfigJsonTests
    {
        [Test]
        public void ShippedConfig_ParsesWithExpectedValues()
        {
            DealConfig config = TestConfigs.Shipped();

            Assert.That(config.ConfigVersion, Is.Not.Empty);
            Assert.That(config.AssumedBagValues, Is.EqualTo(GoldenDealSizes.ASSUMED_BAG_VALUES));
            Assert.That(config.TargetRtp, Is.EqualTo(new Fraction(9, 10)));
            Assert.That(config.BonusUnitValue, Is.EqualTo(Money.FromCents(100)));
            Assert.That(config.MaxGenerationAttempts, Is.EqualTo(1000));
            Assert.That(config.PlacementLimits.LayoutStrategy, Is.EqualTo(LayoutStrategy.GapRepair));
        }

        [Test]
        public void ShippedConfig_Has20BetLevels_From25CentsTo5DollarsIn25CentSteps()
        {
            DealConfig config = TestConfigs.Shipped();

            Assert.That(config.BetLevels, Has.Count.EqualTo(20));
            for (int i = 0; i < 20; i++)
            {
                Assert.That(config.BetLevels[i], Is.EqualTo(TestConfigs.Bet(25 * (i + 1))));
            }
        }

        [Test]
        public void ShippedConfig_BagTotals_MatchTheAssumedDesign()
        {
            DealConfig config = TestConfigs.Shipped();
            ModeConfig inner = config.GetMode(GameMode.Inner);
            ModeConfig outer = config.GetMode(GameMode.Outer);

            AssertBag(inner.RegularBag, tickets: 6, multiplierTotal: 9m, bonusUnits: 5);
            AssertBag(inner.BigBag, tickets: 2, multiplierTotal: 20m, bonusUnits: 20);
            AssertBag(outer.RegularBag, tickets: 10, multiplierTotal: 15m, bonusUnits: 6);
            AssertBag(outer.BigBag, tickets: 3, multiplierTotal: 75m, bonusUnits: 40);
        }

        [Test]
        public void NumericDecimal_IsAccepted_AsExactDecimal()
        {
            DealConfig config = TestConfigs.ShippedWith(root => root["targetRtp"] = 0.9m);

            Assert.That(config.TargetRtp, Is.EqualTo(new Fraction(9, 10)));
        }

        [Test]
        public void UnknownField_IsRejected()
        {
            var exception = Assert.Throws<DealConfigException>(() =>
                TestConfigs.ShippedWith(root => root["regularBagTypo"] = 1));

            Assert.That(exception.Message, Does.Contain("JSON error"));
        }

        [Test]
        public void MissingMode_IsRejected()
        {
            var exception = Assert.Throws<DealConfigException>(() =>
                TestConfigs.ShippedWith(root => ((JObject)root["modes"]).Remove("Outer")));

            Assert.That(exception.Errors, Has.Some.Contains("modes.Outer is missing"));
        }

        [Test]
        public void LossTicketInBag_IsRejected()
        {
            var exception = Assert.Throws<DealConfigException>(() =>
                TestConfigs.ShippedWith(root => root["modes"]["Inner"]["regularBag"]["tickets"][0]["kind"] = "Loss"));

            Assert.That(exception.Errors, Has.Some.Contains("must be 'Multiplier' or 'Bonus'"));
        }

        [Test]
        public void TicketWithBothAwards_IsRejected()
        {
            var exception = Assert.Throws<DealConfigException>(() =>
                TestConfigs.ShippedWith(root => root["modes"]["Inner"]["bigBag"]["tickets"][1]["multiplier"] = "2"));

            Assert.That(exception.Errors, Has.Some.Contains("must not set multiplier"));
        }

        [Test]
        public void InvalidValues_AreAllReportedTogether()
        {
            var exception = Assert.Throws<DealConfigException>(() => TestConfigs.ShippedWith(root =>
            {
                root["configVersion"] = "";
                root["maxGenerationAttempts"] = 0;
                root["modes"]["Outer"]["regularBag"]["tickets"][0]["multiplier"] = "-1";
            }));

            Assert.That(exception.Errors, Has.Some.Contains("configVersion"));
            Assert.That(exception.Errors, Has.Some.Contains("maxGenerationAttempts"));
            Assert.That(exception.Errors, Has.Some.Contains("multiplier must be positive"));
        }

        private static void AssertBag(BagDefinition bag, int tickets, decimal multiplierTotal, long bonusUnits)
        {
            Assert.That(bag.TicketsPerCycle, Is.EqualTo(tickets), $"{bag.Category} tickets per cycle");
            Assert.That(bag.MultiplierTotal, Is.EqualTo(multiplierTotal), $"{bag.Category} multiplier total");
            Assert.That(bag.BonusUnitsTotal, Is.EqualTo(bonusUnits), $"{bag.Category} bonus units");
        }
    }
}
