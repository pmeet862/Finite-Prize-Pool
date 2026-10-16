using System;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Sizing;
using FinitePool.Tests.Fakes;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FinitePool.Tests
{
    public class DealSizeCalculatorTests
    {
        private DealConfig m_config;

        [SetUp]
        public void SetUp()
        {
            m_config = TestConfigs.Placeholder();
        }

        [Test]
        public void Inner100_MatchesGoldenCounts()
        {
            AssertMatchesGolden(GoldenDealSizes.Inner100);
        }

        [Test]
        public void Outer100_MatchesGoldenCounts()
        {
            AssertMatchesGolden(GoldenDealSizes.Outer100);
        }

        [Test]
        public void Outer175_MatchesGoldenCounts()
        {
            AssertMatchesGolden(GoldenDealSizes.Outer175);
        }

        [Test]
        public void Inner100_PayoutPerPairedCycle_IsMultiplierTotalTimesBetPlusBonusUnits()
        {
            DealSize size = Calculate(GameMode.Inner, 100);

            // Placeholder Inner: Σmult = 29, Σbonus = 25 → 29 × $1 + 25 × $1 = $54.
            Assert.That(size.MultiplierTotalPerPairedCycle, Is.EqualTo(29m));
            Assert.That(size.BonusUnitsPerPairedCycle, Is.EqualTo(25));
            Assert.That(size.PayoutPerPairedCycle, Is.EqualTo(Money.FromCents(5400)));
        }

        [TestCaseSource(typeof(GoldenDealSizes), nameof(GoldenDealSizes.AllCombinations))]
        public void AllCombinations_MatchIndependentlyComputedTable(GameMode mode, int betCents, long pairedCycles, long paidSpins, long prizeTickets)
        {
            DealSize size = Calculate(mode, betCents);

            Assert.That(size.PairedCycles, Is.EqualTo(pairedCycles), "paired cycles");
            Assert.That(size.PaidSpins, Is.EqualTo(paidSpins), "paid spins");
            Assert.That(size.PrizeTickets, Is.EqualTo(prizeTickets), "prize tickets");
        }

        [Test]
        public void AllCombinations_HitTargetRtpExactly_WithWholeCounts()
        {
            foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
            {
                foreach (BetLevel bet in m_config.BetLevels)
                {
                    DealSizeResult result = DealSizeCalculator.Calculate(m_config, mode, bet);
                    Assert.That(result.IsAccepted, Is.True, result.Message);

                    DealSize size = result.Size;
                    Assert.That(size.TotalWagers, Is.EqualTo(bet.Amount * size.PaidSpins), $"{size}: wagers");
                    Assert.That(size.TotalPayouts, Is.EqualTo(size.PayoutPerPairedCycle * size.PairedCycles), $"{size}: payouts");
                    Assert.That(size.TotalPayouts.Units * 10, Is.EqualTo(size.TotalWagers.Units * 9), $"{size}: exact 90%");
                    Assert.That(size.LosingTickets, Is.GreaterThanOrEqualTo(0), $"{size}: losses");
                    Assert.That(size.RegularTickets, Is.EqualTo(size.PairedCycles * size.RegularTicketsPerCycle), $"{size}: regular");
                    Assert.That(size.BigTickets, Is.EqualTo(size.PairedCycles * size.BigTicketsPerCycle), $"{size}: big");
                }
            }
        }

        [Test]
        public void AllCombinations_UseTheSmallestPossibleNumberOfPairedCycles()
        {
            foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
            {
                foreach (BetLevel bet in m_config.BetLevels)
                {
                    DealSize size = DealSizeCalculator.Calculate(m_config, mode, bet).Size;

                    for (long fewer = 1; fewer < size.PairedCycles; fewer++)
                    {
                        DealSizeResult smaller = DealSizeCalculator.CalculateForCycles(m_config, mode, bet, fewer);
                        Assert.That(smaller.Rejection, Is.EqualTo(DealSizeRejection.FractionalTickets),
                            $"{mode} {bet}: {fewer} cycle(s) should not fit when the minimum is {size.PairedCycles}");
                    }
                }
            }
        }

        [Test]
        public void MultiplesOfTheMinimum_AreAlsoExact()
        {
            DealSize minimum = Calculate(GameMode.Outer, 100);

            DealSizeResult doubled = DealSizeCalculator.CalculateForCycles(m_config, GameMode.Outer, TestConfigs.Bet(100), minimum.PairedCycles * 2);

            Assert.That(doubled.IsAccepted, Is.True, doubled.Message);
            Assert.That(doubled.Size.PaidSpins, Is.EqualTo(minimum.PaidSpins * 2));
            Assert.That(doubled.Size.IsExactlyTargetRtp, Is.True);
        }

        [Test]
        public void FractionalCentAward_IsRejected_NotRounded()
        {
            DealConfig config = TestConfigs.PlaceholderWith(root =>
                root["modes"]["Inner"]["regularBag"]["tickets"][0]["multiplier"] = "0.5");

            DealSizeResult at25Cents = DealSizeCalculator.Calculate(config, GameMode.Inner, TestConfigs.Bet(25));
            DealSizeResult atOneDollar = DealSizeCalculator.Calculate(config, GameMode.Inner, TestConfigs.Bet(100));

            Assert.That(at25Cents.Rejection, Is.EqualTo(DealSizeRejection.FractionalCentAward), "x0.5 × $0.25 = 12.5 cents");
            Assert.That(atOneDollar.IsAccepted, Is.True, "x0.5 × $1.00 = 50 cents is fine");
        }

        [Test]
        public void FewerPaidSpinsThanPrizeTickets_IsRejected()
        {
            // 11 tickets of x0.5 per paired cycle pay $5.50 at $1. 9 cycles → 55 spins for 99 prizes.
            DealConfig config = TestConfigs.PlaceholderWith(root =>
            {
                root["modes"]["Inner"]["regularBag"]["tickets"] = JArray.Parse("[{ 'kind': 'Multiplier', 'multiplier': '0.5', 'count': 10 }]");
                root["modes"]["Inner"]["bigBag"]["tickets"] = JArray.Parse("[{ 'kind': 'Multiplier', 'multiplier': '0.5', 'count': 1 }]");
            });

            DealSizeResult result = DealSizeCalculator.Calculate(config, GameMode.Inner, TestConfigs.Bet(100));

            Assert.That(result.Rejection, Is.EqualTo(DealSizeRejection.PaidSpinsFewerThanPrizeTickets), result.Message);
        }

        [Test]
        public void DealLargerThanMaxDealTickets_IsRejected()
        {
            long inner100Spins = GoldenDealSizes.Inner100.PaidSpins;
            DealConfig tooSmall = TestConfigs.PlaceholderWith(root => root["maxDealTickets"] = inner100Spins - 1);
            DealConfig justEnough = TestConfigs.PlaceholderWith(root => root["maxDealTickets"] = inner100Spins);

            Assert.That(DealSizeCalculator.Calculate(tooSmall, GameMode.Inner, TestConfigs.Bet(100)).Rejection,
                Is.EqualTo(DealSizeRejection.ExceedsMaxDealTickets));
            Assert.That(DealSizeCalculator.Calculate(justEnough, GameMode.Inner, TestConfigs.Bet(100)).IsAccepted, Is.True);
        }

        [Test]
        public void BetNotInConfig_IsRejected()
        {
            DealSizeResult result = DealSizeCalculator.Calculate(m_config, GameMode.Inner, TestConfigs.Bet(30));

            Assert.That(result.Rejection, Is.EqualTo(DealSizeRejection.UnknownBetLevel));
        }

        [Test]
        public void ZeroOrNegativePairedCycles_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DealSizeCalculator.CalculateForCycles(m_config, GameMode.Inner, TestConfigs.Bet(100), 0));
        }

        private DealSize Calculate(GameMode mode, long betCents)
        {
            DealSizeResult result = DealSizeCalculator.Calculate(m_config, mode, TestConfigs.Bet(betCents));
            Assert.That(result.IsAccepted, Is.True, result.Message);
            return result.Size;
        }

        private void AssertMatchesGolden(GoldenDealSize golden)
        {
            DealSize size = Calculate(golden.Mode, golden.BetCents);

            Assert.That(size.PairedCycles, Is.EqualTo(golden.PairedCycles), "paired cycles");
            Assert.That(size.PaidSpins, Is.EqualTo(golden.PaidSpins), "paid spins");
            Assert.That(size.PrizeTickets, Is.EqualTo(golden.PrizeTickets), "prize tickets");
            Assert.That(size.LosingTickets, Is.EqualTo(golden.LosingTickets), "losing tickets");
            Assert.That(size.TotalWagers, Is.EqualTo(Money.FromCents(golden.WagersCents)), "wagers");
            Assert.That(size.TotalPayouts, Is.EqualTo(Money.FromCents(golden.PayoutsCents)), "payouts");
            Assert.That(size.ActualRtp, Is.EqualTo(new Fraction(9, 10)), "RTP");
        }
    }
}
