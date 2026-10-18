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
        public void Scenario01_Inner100_CountsWagersPayouts_AndExactly90PercentAfterTheWholeDeal()
        {
            GoldenDealSize golden = GoldenDealSizes.Inner100;
            DealService service = ReadyToPlay(new InMemoryDealStore(), GameMode.Inner, golden.BetCents);
            string dealId = service.GetActiveDealInfo(GameMode.Inner, TestConfigs.Bet(golden.BetCents)).DealId;
            long prizes = 0, losses = 0;
            Money wagers = Money.Zero, awards = Money.Zero;

            for (long spin = 0; spin < golden.PaidSpins; spin++)
            {
                SpinResult result = SpinAndShow(service, Alice, GameMode.Inner, golden.BetCents);
                Assert.That(result.DealId, Is.EqualTo(dealId));
                if (result.Category == TicketCategory.Loss) losses++; else prizes++;
                wagers += result.Wager;
                awards += result.Award;
            }

            Assert.That(prizes, Is.EqualTo(golden.PrizeTickets));
            Assert.That(losses, Is.EqualTo(golden.LosingTickets));
            Assert.That(wagers, Is.EqualTo(Money.FromCents(golden.WagersCents)));
            Assert.That(awards, Is.EqualTo(Money.FromCents(golden.PayoutsCents)));
            Assert.That(service.GetBalance(Alice), Is.EqualTo(StartingBalance - wagers + awards));

            CompletedDealRecord record = service.GetCompletedDeals().Single(completed => completed.DealId == dealId);
            Assert.That(record.TicketCount, Is.EqualTo(golden.PaidSpins));
            Assert.That(record.WagersUnits, Is.EqualTo(golden.WagersCents));
            Assert.That(record.PayoutsUnits, Is.EqualTo(golden.PayoutsCents));
            Assert.That(record.ActualRtp, Is.EqualTo(new Fraction(9, 10)));
            Assert.That(record.ReachedTargetRtp, Is.True);
        }

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
        public void Scenario04_TooLittleBalance_SpinRejected_NoTicketUsed()
        {
            var store = new InMemoryDealStore();
            DealService service = ReadyToPlay(store, GameMode.Inner, 100);
            var poorPlayer = new PlayerId("poor");
            service.RegisterPlayer(poorPlayer, Money.FromCents(99));

            SpinOutcome outcome = service.Spin(poorPlayer, GameMode.Inner, TestConfigs.Bet(100));

            Assert.That(outcome.IsAccepted, Is.False);
            Assert.That(outcome.Rejection, Is.EqualTo(SpinRejection.InsufficientBalance));
            Assert.That(service.GetBalance(poorPlayer), Is.EqualTo(Money.FromCents(99)));
            Assert.That(service.GetActiveDealInfo(GameMode.Inner, TestConfigs.Bet(100)).SpinsPlayed, Is.EqualTo(0));
            Assert.That(store.ReadSpinRecords(), Is.Empty);
            Assert.That(SpinAndShow(service, Alice, GameMode.Inner, 100).TicketIndex, Is.EqualTo(0), "the next spin still gets ticket 0");
        }

        [Test]
        public void Scenario05_SaveAndReloadMidDeal_ContinuesFromTheExactNextTicket()
        {
            using (var folder = new TempFolder())
            {
                DealService before = ReadyToPlay(new FileDealStore(folder.Path), GameMode.Outer, 100);
                for (int i = 0; i < 7; i++) SpinAndShow(before, i % 2 == 0 ? Alice : Bob, GameMode.Outer, 100);
                Money aliceBefore = before.GetBalance(Alice);
                Money bobBefore = before.GetBalance(Bob);
                string dealId = before.GetActiveDealInfo(GameMode.Outer, TestConfigs.Bet(100)).DealId;

                // The game closes; a new service reads everything back from disk.
                var reopenedStore = new FileDealStore(folder.Path);
                DealService after = Open(reopenedStore);

                Assert.That(after.GetBalance(Alice), Is.EqualTo(aliceBefore));
                Assert.That(after.GetBalance(Bob), Is.EqualTo(bobBefore));
                Assert.That(after.GetActiveDealInfo(GameMode.Outer, TestConfigs.Bet(100)).DealId, Is.EqualTo(dealId));

                SpinResult next = SpinAndShow(after, Alice, GameMode.Outer, 100);
                Assert.That(next.DealId, Is.EqualTo(dealId));
                Assert.That(next.TicketIndex, Is.EqualTo(7));
                Assert.That(next.Award, Is.EqualTo(reopenedStore.ReadDeal(dealId).GetTicket(7).Award));
            }
        }

        [Test]
        public void Scenario06_SpinNotYetShown_IsReplayedAfterReload_WithoutUsingAnotherTicket()
        {
            using (var folder = new TempFolder())
            {
                DealService before = ReadyToPlay(new FileDealStore(folder.Path), GameMode.Inner, 100);
                SpinAndShow(before, Alice, GameMode.Inner, 100);
                SpinResult taken = before.Spin(Alice, GameMode.Inner, TestConfigs.Bet(100)).Result; // game closes mid-animation
                Money balanceAfterTaken = before.GetBalance(Alice);

                DealService after = Open(new FileDealStore(folder.Path));
                SpinResult replayed = after.GetPendingResult(Alice);

                Assert.That(replayed, Is.Not.Null);
                Assert.That((replayed.SpinId, replayed.DealId, replayed.TicketIndex, replayed.Award, replayed.BalanceAfter),
                    Is.EqualTo((taken.SpinId, taken.DealId, taken.TicketIndex, taken.Award, taken.BalanceAfter)));
                Assert.That(after.GetActiveDealInfo(GameMode.Inner, TestConfigs.Bet(100)).SpinsPlayed, Is.EqualTo(2), "reloading used no ticket");
                Assert.That(after.GetBalance(Alice), Is.EqualTo(balanceAfterTaken), "the award was credited once, not twice");

                Assert.That(after.MarkShown(Alice, replayed.SpinId), Is.True);
                Assert.That(after.GetPendingResult(Alice), Is.Null);
                Assert.That(SpinAndShow(after, Alice, GameMode.Inner, 100).TicketIndex, Is.EqualTo(2));
            }
        }

        [Test]
        public void Scenario07_FinalTicket_CompletesTheDeal_AndPlayContinuesOnTheReadySuccessor()
        {
            var store = new InMemoryDealStore();
            DealService service = ReadyToPlay(store, GameMode.Inner, 100);
            string firstId = service.GetActiveDealInfo(GameMode.Inner, TestConfigs.Bet(100)).DealId;
            string successorId = ReadyDeal(store, GameMode.Inner, 100).DealId;

            for (int spin = 0; spin < GoldenDealSizes.Inner100.PaidSpins; spin++) SpinAndShow(service, Alice, GameMode.Inner, 100);

            CompletedDealRecord completed = service.GetCompletedDeals().Single();
            Assert.That(completed.DealId, Is.EqualTo(firstId));
            Assert.That(completed.TicketCount, Is.EqualTo(GoldenDealSizes.Inner100.PaidSpins));
            Assert.That(completed.TargetRtp, Is.EqualTo(new Fraction(9, 10)));
            Assert.That(completed.ActualRtp, Is.EqualTo(new Fraction(9, 10)));

            DealInfo active = service.GetActiveDealInfo(GameMode.Inner, TestConfigs.Bet(100));
            Assert.That(active.DealId, Is.EqualTo(successorId), "the waiting successor took over");
            Assert.That(active.SpinsPlayed, Is.EqualTo(0));
            Assert.That(ReadyDeal(store, GameMode.Inner, 100).DealId, Is.Not.EqualTo(successorId), "a new successor is already waiting");

            SpinResult next = SpinAndShow(service, Bob, GameMode.Inner, 100);
            Assert.That((next.DealId, next.TicketIndex), Is.EqualTo((successorId, 0)));
        }

        [Test]
        public void Scenario08_ConfigChangeMidDeal_LeavesTheActiveDealUnchanged_AndTheNextDealUsesTheNewVersion()
        {
            var store = new InMemoryDealStore();
            DealService service = ReadyToPlay(store, GameMode.Inner, 100);
            string oldVersion = service.Config.ConfigVersion;
            for (int spin = 0; spin < 5; spin++) SpinAndShow(service, Alice, GameMode.Inner, 100);
            string activeId = service.GetActiveDealInfo(GameMode.Inner, TestConfigs.Bet(100)).DealId;
            string staleSuccessorId = ReadyDeal(store, GameMode.Inner, 100).DealId;

            service.ApplyConfig(TestConfigs.PlaceholderWith(root => root["configVersion"] = "v2-test"));

            DealInfo active = service.GetActiveDealInfo(GameMode.Inner, TestConfigs.Bet(100));
            Assert.That((active.DealId, active.ConfigVersion, active.SpinsPlayed), Is.EqualTo((activeId, oldVersion, 5)), "the active deal is untouched");

            RetireRecord superseded = service.GetRetiredDeals().Single();
            Assert.That(superseded.DealId, Is.EqualTo(staleSuccessorId));
            Assert.That(superseded.Reason, Does.Contain("Superseded"));
            Assert.That(superseded.TicketsUsed, Is.EqualTo(0));
            Assert.That(ReadyDeal(store, GameMode.Inner, 100).ConfigVersion, Is.EqualTo("v2-test"), "the successor was rebuilt on v2");

            for (int spin = 5; spin < GoldenDealSizes.Inner100.PaidSpins; spin++) SpinAndShow(service, Alice, GameMode.Inner, 100);

            Assert.That(service.GetCompletedDeals().Single().ConfigVersion, Is.EqualTo(oldVersion));
            Assert.That(service.GetActiveDealInfo(GameMode.Inner, TestConfigs.Bet(100)).ConfigVersion, Is.EqualTo("v2-test"));
        }

        [Test]
        public void Scenario09_Retire_LogsEveryField_AndSwitchesToAFreshVerifiedDeal()
        {
            var store = new InMemoryDealStore();
            var clock = new FakeClock();
            DealService service = ReadyToPlay(store, GameMode.Inner, 100, clock: clock);
            string version = service.Config.ConfigVersion;
            Money payouts = Money.Zero;
            for (int spin = 0; spin < 10; spin++) payouts += SpinAndShow(service, Alice, GameMode.Inner, 100).Award;
            string retiredId = service.GetActiveDealInfo(GameMode.Inner, TestConfigs.Bet(100)).DealId;
            string successorId = ReadyDeal(store, GameMode.Inner, 100).DealId;
            clock.UtcNow = clock.UtcNow.AddMinutes(5);

            RetireRecord record = service.Retire(GameMode.Inner, TestConfigs.Bet(100), "Operator test");

            Assert.That(record.DealId, Is.EqualTo(retiredId));
            Assert.That(record.Reason, Is.EqualTo("Operator test"));
            Assert.That(record.RetiredUtc, Is.EqualTo(clock.UtcNow));
            Assert.That(record.OldConfigVersion, Is.EqualTo(version));
            Assert.That(record.NewConfigVersion, Is.EqualTo(version));
            Assert.That(record.ReplacementDealId, Is.EqualTo(successorId));
            Assert.That(record.TicketsUsed, Is.EqualTo(10));
            Assert.That(record.TicketsRemaining, Is.EqualTo(GoldenDealSizes.Inner100.PaidSpins - 10));
            Assert.That(record.WagersUnits, Is.EqualTo(1000));
            Assert.That(record.PayoutsUnits, Is.EqualTo(payouts.Units));
            Assert.That(record.ActualRtp, Is.EqualTo(new Fraction(payouts.Units, 1000)));
            Assert.That(service.GetRetiredDeals().Single().DealId, Is.EqualTo(retiredId), "the record is saved");

            DealInfo replacement = service.GetActiveDealInfo(GameMode.Inner, TestConfigs.Bet(100));
            Assert.That((replacement.DealId, replacement.SpinsPlayed), Is.EqualTo((successorId, 0)), "fresh: no tickets used");
            Assert.That(DealVerifier.Verify(store.ReadDeal(successorId)).IsVerified, Is.True, "verified");
            Assert.That(store.ReadDeal(retiredId).TicketCount, Is.EqualTo(GoldenDealSizes.Inner100.PaidSpins), "unused tickets kept for audit");
            Assert.That(service.GetCompletedDeals().Any(completed => completed.DealId == retiredId), Is.False, "never reported as completed");
            Assert.That(typeof(RetireRecord).GetProperty("ReachedTargetRtp"), Is.Null, "a retired deal can not claim its target RTP");
            Assert.That(SpinAndShow(service, Alice, GameMode.Inner, 100).DealId, Is.EqualTo(successorId));
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
