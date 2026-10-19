using System;
using System.Collections.Generic;
using System.Linq;
using FinitePool.Engine.Building;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Infrastructure;
using FinitePool.Engine.Persistence;
using FinitePool.Engine.Service;
using FinitePool.Engine.Sizing;
using FinitePool.Engine.Verification;
using FinitePool.Tests.Fakes;
using NUnit.Framework;
using static FinitePool.Tests.Fakes.Services;

namespace FinitePool.Tests
{
    /// <summary>
    /// Tolerance mode (brief §6 stretch): RTP may land anywhere in 90% ± 0.1 points, using as few
    /// paired cycles as possible. Expected sizes were worked out independently in python.
    /// </summary>
    public class ToleranceModeTests
    {
        private static readonly Fraction Target = new Fraction(9, 10);
        private static readonly Fraction Tolerance = new Fraction(1, 1000);

        /// <summary>Mode, bet in cents, paired cycles, paid spins.</summary>
        public static readonly object[][] ExpectedSizes =
        {
            new object[] { GameMode.Inner, 25, 3L, 430L }, new object[] { GameMode.Inner, 50, 4L, 351L },
            new object[] { GameMode.Inner, 75, 3L, 208L }, new object[] { GameMode.Inner, 100, 1L, 60L },
            new object[] { GameMode.Inner, 125, 2L, 109L }, new object[] { GameMode.Inner, 150, 4L, 203L },
            new object[] { GameMode.Inner, 175, 7L, 337L }, new object[] { GameMode.Inner, 200, 7L, 323L },
            new object[] { GameMode.Inner, 225, 5L, 223L }, new object[] { GameMode.Inner, 250, 3L, 130L },
            new object[] { GameMode.Inner, 275, 3L, 127L }, new object[] { GameMode.Inner, 300, 2L, 83L },
            new object[] { GameMode.Inner, 325, 4L, 163L }, new object[] { GameMode.Inner, 350, 5L, 201L },
            new object[] { GameMode.Inner, 375, 3L, 119L }, new object[] { GameMode.Inner, 400, 5L, 196L },
            new object[] { GameMode.Inner, 425, 4L, 155L }, new object[] { GameMode.Inner, 450, 5L, 192L },
            new object[] { GameMode.Inner, 475, 9L, 343L }, new object[] { GameMode.Inner, 500, 4L, 151L },
            new object[] { GameMode.Outer, 25, 2L, 609L }, new object[] { GameMode.Outer, 50, 1L, 202L },
            new object[] { GameMode.Outer, 75, 1L, 168L }, new object[] { GameMode.Outer, 100, 1L, 151L },
            new object[] { GameMode.Outer, 125, 1L, 141L }, new object[] { GameMode.Outer, 150, 1L, 134L },
            new object[] { GameMode.Outer, 175, 3L, 388L }, new object[] { GameMode.Outer, 200, 2L, 251L },
            new object[] { GameMode.Outer, 225, 3L, 368L }, new object[] { GameMode.Outer, 250, 2L, 241L },
            new object[] { GameMode.Outer, 275, 2L, 237L }, new object[] { GameMode.Outer, 300, 1L, 117L },
            new object[] { GameMode.Outer, 325, 3L, 347L }, new object[] { GameMode.Outer, 350, 2L, 229L },
            new object[] { GameMode.Outer, 375, 3L, 341L }, new object[] { GameMode.Outer, 400, 3L, 338L },
            new object[] { GameMode.Outer, 425, 1L, 112L }, new object[] { GameMode.Outer, 450, 3L, 334L },
            new object[] { GameMode.Outer, 475, 3L, 332L }, new object[] { GameMode.Outer, 500, 3L, 331L },
        };

        private static DealConfig ToleranceConfig() => TestConfigs.PlaceholderWith(root => root["rtpTolerance"] = "0.001");

        // ---------------------------------------------------------------- config and calculator

        [Test]
        public void Config_WithoutTolerance_IsExactMode()
        {
            Assert.That(TestConfigs.Placeholder().IsToleranceMode, Is.False);
            Assert.That(ToleranceConfig().IsToleranceMode, Is.True);
            Assert.That(ToleranceConfig().RtpTolerance, Is.EqualTo(Tolerance));
        }

        [Test]
        public void Config_ToleranceThatIsNegativeOrAtLeastTheTarget_IsRejected()
        {
            Assert.Throws<DealConfigException>(() => TestConfigs.PlaceholderWith(root => root["rtpTolerance"] = "-0.001"));
            Assert.Throws<DealConfigException>(() => TestConfigs.PlaceholderWith(root => root["rtpTolerance"] = "0.9"));
        }

        [TestCaseSource(nameof(ExpectedSizes))]
        public void Calculator_FindsTheSmallestDealInsideTheBand(GameMode mode, int betCents, long pairedCycles, long paidSpins)
        {
            DealSizeResult result = DealSizeCalculator.Calculate(ToleranceConfig(), mode, TestConfigs.Bet(betCents));

            Assert.That(result.IsAccepted, Is.True, result.Message);
            Assert.That((result.Size.PairedCycles, result.Size.PaidSpins), Is.EqualTo((pairedCycles, paidSpins)));
            Assert.That(result.Size.ActualRtp.IsWithin(Target, Tolerance), Is.True, result.Size.ActualRtp.ToPercentString());
        }

        [Test]
        public void Calculator_UsesOnePairedCycle_WhereExactModeNeedsNine()
        {
            DealSize exact = DealSizeCalculator.Calculate(TestConfigs.Placeholder(), GameMode.Outer, TestConfigs.Bet(100)).Size;
            DealSize tolerant = DealSizeCalculator.Calculate(ToleranceConfig(), GameMode.Outer, TestConfigs.Bet(100)).Size;

            Assert.That(exact.PairedCycles, Is.EqualTo(9));
            Assert.That(tolerant.PairedCycles, Is.EqualTo(1));
            Assert.That(tolerant.ActualRtp, Is.EqualTo(new Fraction(13600, 15100)), "$136 paid over 151 spins of $1 = 90.066%");
        }

        [Test]
        public void Calculator_StaysExact_WhenTheExactDealAlreadyUsesOneCycle()
        {
            DealSize size = DealSizeCalculator.Calculate(ToleranceConfig(), GameMode.Inner, TestConfigs.Bet(100)).Size;

            Assert.That((size.PairedCycles, size.PaidSpins), Is.EqualTo((1L, 60L)));
            Assert.That(size.ActualRtp, Is.EqualTo(Target));
        }

        // ---------------------------------------------------------------- built, verified and played deals

        [Test]
        public void EveryCombination_BuildsAndVerifies_InToleranceMode()
        {
            DealConfig config = ToleranceConfig();
            var builder = new DealBuilder(new CryptoRandomSource(), new FakeClock(), NullEngineLog.Instance);

            foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
            {
                foreach (BetLevel bet in config.BetLevels)
                {
                    BuildResult built = builder.Build(config, mode, bet);
                    Assert.That(built.IsSuccess, Is.True, built.Message);

                    VerificationReport report = DealVerifier.Verify(DealFileCodec.Deserialize(DealFileCodec.Serialize(built.Deal)));
                    Assert.That(report.IsVerified, Is.True, report.ToString());
                    Assert.That(report.ActualRtp, Is.EqualTo(built.Size.ActualRtp));
                }
            }
        }

        [Test]
        public void ADealWhosePlannedRtpIsOutsideItsBand_FailsVerification()
        {
            var builder = new DealBuilder(new CryptoRandomSource(), new FakeClock(), NullEngineLog.Instance);
            var deal = builder.Build(ToleranceConfig(), GameMode.Outer, TestConfigs.Bet(100)).Deal; // planned 90.066%
            Assert.That(DealVerifier.Verify(deal).IsVerified, Is.True, "inside ±0.1 points");

            VerificationReport report = DealVerifier.Verify(DealTamper.WithRtpTolerance(deal, new Fraction(1, 10000)));

            Assert.That(report.IsVerified, Is.False, "90.066% is outside ±0.01 points");
            Assert.That(report.Failures, Has.Some.Contains("is outside"));
        }

        [Test]
        public void AFullToleranceDeal_CompletesAtItsPlannedRtp_AndTheRecordSaysSo()
        {
            DealService service = Open(new InMemoryDealStore(), ToleranceConfig());
            service.RegisterPlayer(Alice, Money.FromCents(100000));
            service.EnsurePrepared(GameMode.Outer, TestConfigs.Bet(100));
            Assert.That(service.GetAvailability(GameMode.Outer, TestConfigs.Bet(100)).Status, Is.EqualTo(AvailabilityStatus.Available),
                service.GetAvailability(GameMode.Outer, TestConfigs.Bet(100)).ToString());

            for (int spin = 0; spin < 151; spin++) SpinAndShow(service, Alice, GameMode.Outer, 100);

            CompletedDealRecord record = service.GetCompletedDeals().Single();
            Assert.That(record.TicketCount, Is.EqualTo(151));
            Assert.That(record.TargetRtp, Is.EqualTo(Target));
            Assert.That(record.PlannedRtp, Is.EqualTo(new Fraction(13600, 15100)), "brief §6: record the planned RTP");
            Assert.That(record.ActualRtp, Is.EqualTo(record.PlannedRtp), "and the actual RTP, which matches it");
            Assert.That(record.ReachedTargetRtp, Is.True, "reached its planned RTP, which is inside the band");
        }

        [Test]
        public void Simulator_InToleranceMode_ReportsEveryDealOnTarget()
        {
            IReadOnlyList<CompletedDealRecord> deals = DealSimulator.PlayCompleteDeals(ToleranceConfig(), GameMode.Outer, TestConfigs.Bet(100), 2);

            Assert.That(deals.All(deal => deal.ReachedTargetRtp), Is.True);
        }
    }
}
