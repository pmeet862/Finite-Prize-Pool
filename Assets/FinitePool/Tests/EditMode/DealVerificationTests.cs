using System;
using System.Collections.Generic;
using System.Linq;
using FinitePool.Engine.Building;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Deals;
using FinitePool.Engine.Infrastructure;
using FinitePool.Engine.Persistence;
using FinitePool.Engine.Placement;
using FinitePool.Engine.Verification;
using FinitePool.Tests.Fakes;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FinitePool.Tests
{
    public class DealVerifierTests
    {
        [Test]
        public void FreshlyBuiltDeals_Verify_ForEveryCombination()
        {
            DealConfig config = TestConfigs.Placeholder();
            DealBuilder builder = NewBuilder();

            foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
            {
                foreach (BetLevel bet in config.BetLevels)
                {
                    BuildResult built = builder.Build(config, mode, bet);
                    VerificationReport report = DealVerifier.Verify(built.Deal);

                    Assert.That(report.IsVerified, Is.True, report.ToString());
                    Assert.That(report.TicketCount, Is.EqualTo(built.Size.PaidSpins));
                    Assert.That(report.TotalWagers, Is.EqualTo(built.Size.TotalWagers));
                    Assert.That(report.TotalPayouts, Is.EqualTo(built.Size.TotalPayouts));
                    Assert.That(report.ActualRtp, Is.EqualTo(new Fraction(9, 10)));
                }
            }
        }

        [Test]
        public void ALossTurnedIntoAPrize_IsCaught()
        {
            Deal deal = BuildDeal(GameMode.Inner, 100);
            ushort[] ids = DealTamper.TicketIds(deal);
            int firstLoss = Array.IndexOf(ids, TicketTypeTable.LOSS_ID);
            ids[firstLoss] = 1; // the first Regular ticket type

            VerificationReport report = DealVerifier.Verify(DealTamper.WithTicketIds(deal, ids));

            Assert.That(report.IsVerified, Is.False);
            Assert.That(report.Failures, Has.Some.Contains("RTP is"));
            Assert.That(report.Failures, Has.Some.Contains("appears"));
        }

        [Test]
        public void APrizeSwappedForADifferentPrize_IsCaught()
        {
            Deal deal = BuildDeal(GameMode.Inner, 100);
            ushort[] ids = DealTamper.TicketIds(deal);
            TicketType x1 = deal.TicketTypes.First(type => type.Category == TicketCategory.Regular && type.Multiplier == 1m);
            TicketType x3 = deal.TicketTypes.First(type => type.Category == TicketCategory.Regular && type.Multiplier == 3m);
            ids[Array.IndexOf(ids, x1.Id)] = x3.Id;

            VerificationReport report = DealVerifier.Verify(DealTamper.WithTicketIds(deal, ids));

            Assert.That(report.IsVerified, Is.False);
            Assert.That(report.Failures, Has.Some.Contains("appears"));
            Assert.That(report.Failures, Has.Some.Contains("RTP is"));
        }

        [Test]
        public void AMissingTicket_IsCaught()
        {
            Deal deal = BuildDeal(GameMode.Inner, 100);
            ushort[] shorter = DealTamper.TicketIds(deal).Take(deal.TicketCount - 1).ToArray();

            VerificationReport report = DealVerifier.Verify(DealTamper.WithTicketIds(deal, shorter));

            Assert.That(report.IsVerified, Is.False);
            Assert.That(report.Failures, Has.Some.Contains("need exactly 60"));
        }

        [Test]
        public void MixedCycles_AreCaught_EvenThoughCountsAndRtpStillMatch()
        {
            Deal deal = BuildDeal(GameMode.Outer, 100); // 9 paired cycles of 10 Regular tickets
            ushort[] ids = DealTamper.TicketIds(deal);
            List<int> regular = DealTamper.PositionsOf(deal, TicketCategory.Regular);

            // Swap the first ticket of Regular cycle 0 with a different ticket from cycle 1.
            int fromCycle0 = regular[0];
            int fromCycle1 = regular.Skip(10).Take(10).First(position => ids[position] != ids[fromCycle0]);
            (ids[fromCycle0], ids[fromCycle1]) = (ids[fromCycle1], ids[fromCycle0]);

            VerificationReport report = DealVerifier.Verify(DealTamper.WithTicketIds(deal, ids));

            Assert.That(report.ActualRtp, Is.EqualTo(new Fraction(9, 10)), "totals are unchanged by a swap");
            Assert.That(report.IsVerified, Is.False);
            Assert.That(report.Failures, Has.Some.Contains("Cycles are mixed"));
        }

        [Test]
        public void ALayoutThatBreaksTheDealsOwnLimits_IsCaught()
        {
            Deal deal = BuildDeal(GameMode.Inner, 100);
            var tooStrict = new PlacementLimits(maxConsecutiveLosses: 1, maxRegularGap: 60, maxBigGap: 60, repeatingIntervalMaxPeriod: 4);

            VerificationReport report = DealVerifier.Verify(DealTamper.WithLimits(deal, tooStrict));

            Assert.That(report.IsVerified, Is.False);
            Assert.That(report.Failures, Has.Some.Contains("consecutive losses"));
        }

        [Test]
        public void ATicketTypeWhoseAwardDoesntMatchItsDefinition_IsCaught()
        {
            Deal deal = BuildDeal(GameMode.Inner, 100);
            List<TicketType> types = deal.TicketTypes.ToList();
            TicketType original = types[1];
            types[1] = new TicketType(original.Id, original.Category, original.Kind, original.Multiplier, original.BonusUnits,
                original.Award + Money.FromCents(1), original.CountPerCycle);

            VerificationReport report = DealVerifier.Verify(DealTamper.WithTicketTypes(deal, types));

            Assert.That(report.IsVerified, Is.False);
            Assert.That(report.Failures, Has.Some.Contains("its definition gives"));
        }

        private static Deal BuildDeal(GameMode mode, long betCents)
        {
            BuildResult result = NewBuilder().Build(TestConfigs.Placeholder(), mode, TestConfigs.Bet(betCents));
            Assert.That(result.IsSuccess, Is.True, result.Message);
            return result.Deal;
        }

        private static DealBuilder NewBuilder() => new DealBuilder(new CryptoRandomSource(), new FakeClock(), NullEngineLog.Instance);
    }

    public class DealFileCodecTests
    {
        [Test]
        public void RoundTrip_GivesAnIdenticalDeal_ThatStillVerifies()
        {
            Deal original = NewBuilder().Build(TestConfigs.Placeholder(), GameMode.Outer, TestConfigs.Bet(175)).Deal;

            Deal loaded = DealFileCodec.Deserialize(DealFileCodec.Serialize(original));

            Assert.That(loaded.DealId, Is.EqualTo(original.DealId));
            Assert.That(loaded.Mode, Is.EqualTo(original.Mode));
            Assert.That(loaded.Bet, Is.EqualTo(original.Bet));
            Assert.That(loaded.ConfigVersion, Is.EqualTo(original.ConfigVersion));
            Assert.That(loaded.CreatedUtc, Is.EqualTo(original.CreatedUtc));
            Assert.That(loaded.CreatedUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(loaded.TargetRtp, Is.EqualTo(original.TargetRtp));
            Assert.That(loaded.PairedCycles, Is.EqualTo(original.PairedCycles));
            Assert.That(loaded.BonusUnitValue, Is.EqualTo(original.BonusUnitValue));
            Assert.That(loaded.Limits.MaxConsecutiveLosses, Is.EqualTo(original.Limits.MaxConsecutiveLosses));
            Assert.That(loaded.Limits.MaxRegularGap, Is.EqualTo(original.Limits.MaxRegularGap));
            Assert.That(loaded.Limits.MaxBigGap, Is.EqualTo(original.Limits.MaxBigGap));
            Assert.That(loaded.TicketTypeIds, Is.EqualTo(original.TicketTypeIds));
            Assert.That(loaded.TicketTypes.Select(type => type.ToString()), Is.EqualTo(original.TicketTypes.Select(type => type.ToString())));
            Assert.That(DealVerifier.Verify(loaded).IsVerified, Is.True);
        }

        [Test]
        public void CorruptedTicketBytes_FailTheHashCheck()
        {
            JObject file = SavedDealJson();
            byte[] bytes = Convert.FromBase64String((string)file["tickets"]);
            bytes[0] ^= 0x01;
            file["tickets"] = Convert.ToBase64String(bytes);

            var exception = Assert.Throws<DealFileException>(() => DealFileCodec.Deserialize(file.ToString()));
            Assert.That(exception.Message, Does.Contain("SHA-256"));
        }

        [Test]
        public void TicketCountThatDisagreesWithTheData_IsRejected()
        {
            JObject file = SavedDealJson();
            file["ticketCount"] = (int)file["ticketCount"] + 1;

            var exception = Assert.Throws<DealFileException>(() => DealFileCodec.Deserialize(file.ToString()));
            Assert.That(exception.Message, Does.Contain("header says"));
        }

        [Test]
        public void UnknownFormatVersion_IsRejected()
        {
            JObject file = SavedDealJson();
            file["formatVersion"] = 99;

            Assert.Throws<DealFileException>(() => DealFileCodec.Deserialize(file.ToString()));
        }

        [Test]
        public void UnknownField_AndNonJson_AreRejected()
        {
            JObject file = SavedDealJson();
            file["extra"] = 1;

            Assert.Throws<DealFileException>(() => DealFileCodec.Deserialize(file.ToString()));
            Assert.Throws<DealFileException>(() => DealFileCodec.Deserialize("not json"));
        }

        [Test]
        public void TicketsAreStoredCompactly_TwoBytesPerTicket()
        {
            Deal deal = NewBuilder().Build(TestConfigs.Placeholder(), GameMode.Outer, TestConfigs.Bet(475)).Deal;

            JObject file = JObject.Parse(DealFileCodec.Serialize(deal));

            Assert.That(Convert.FromBase64String((string)file["tickets"]).Length, Is.EqualTo(deal.TicketCount * 2));
        }

        private static JObject SavedDealJson()
        {
            Deal deal = NewBuilder().Build(TestConfigs.Placeholder(), GameMode.Inner, TestConfigs.Bet(100)).Deal;
            return JObject.Parse(DealFileCodec.Serialize(deal));
        }

        private static DealBuilder NewBuilder() => new DealBuilder(new CryptoRandomSource(), new FakeClock(), NullEngineLog.Instance);
    }
}
