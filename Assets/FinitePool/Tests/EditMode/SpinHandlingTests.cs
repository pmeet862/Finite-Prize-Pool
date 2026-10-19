using System.Linq;
using FinitePool.Engine.Core;
using FinitePool.Engine.Deals;
using FinitePool.Engine.Persistence;
using FinitePool.Engine.Service;
using FinitePool.Tests.Fakes;
using NUnit.Framework;
using static FinitePool.Tests.Fakes.Services;

namespace FinitePool.Tests
{
    public class SpinHandlingTests
    {
        private static readonly BetLevel OneDollar = TestConfigs.Bet(100);

        [Test]
        public void AnAcceptedSpin_ChargesTheBet_CreditsTheAward_AndUsesTheNextTicket()
        {
            var store = new InMemoryDealStore();
            DealService service = ReadyToPlay(store, GameMode.Inner, 100);
            Deal deal = store.ReadDeal(service.GetActiveDealInfo(GameMode.Inner, OneDollar).DealId);

            SpinOutcome outcome = service.Spin(Alice, GameMode.Inner, OneDollar);

            Assert.That(outcome.IsAccepted, Is.True, outcome.Message);
            SpinResult result = outcome.Result;
            Assert.That(result.TicketIndex, Is.EqualTo(0));
            Assert.That(result.Award, Is.EqualTo(deal.GetTicket(0).Award));
            Assert.That(result.Category, Is.EqualTo(deal.GetTicket(0).Category));
            Assert.That(result.BalanceAfter, Is.EqualTo(StartingBalance - OneDollar.Amount + result.Award));
            Assert.That(service.GetBalance(Alice), Is.EqualTo(result.BalanceAfter));
            Assert.That(service.GetActiveDealInfo(GameMode.Inner, OneDollar).SpinsPlayed, Is.EqualTo(1));

            SpinRecord logged = store.ReadSpinRecords().Single();
            Assert.That((logged.SpinId, logged.DealId, logged.TicketIndex, logged.WagerUnits, logged.AwardUnits),
                Is.EqualTo((result.SpinId, result.DealId, 0, 100L, result.Award.Units)));
        }

        [Test]
        public void EveryTicket_IsUsedExactlyOnce_InOrder_UntilTheDealCompletes()
        {
            var store = new InMemoryDealStore();
            DealService service = ReadyToPlay(store, GameMode.Inner, 100);
            string dealId = service.GetActiveDealInfo(GameMode.Inner, OneDollar).DealId;
            Deal deal = store.ReadDeal(dealId);
            Money totalAwards = Money.Zero;

            for (int spin = 0; spin < deal.TicketCount; spin++)
            {
                SpinResult result = SpinAndShow(service, Alice, GameMode.Inner, 100);
                Assert.That(result.TicketIndex, Is.EqualTo(spin));
                Assert.That(result.Award, Is.EqualTo(deal.GetTicket(spin).Award));
                totalAwards += result.Award;
            }

            Assert.That(totalAwards, Is.EqualTo(Money.FromCents(GoldenDealSizes.Inner100.PayoutsCents)));
            Assert.That(store.ReadState().FindDeal(dealId).State, Is.EqualTo(DealState.Completed));
            Assert.That(store.ReadSpinRecords().Select(record => record.TicketIndex), Is.EqualTo(Enumerable.Range(0, deal.TicketCount)));
        }

        [Test]
        public void AllPlayers_ShareTheSameDeal_AndSwitchingPlayerDoesNotResetIt()
        {
            DealService service = ReadyToPlay(new InMemoryDealStore(), GameMode.Inner, 100);

            for (int i = 0; i < 3; i++) SpinAndShow(service, Alice, GameMode.Inner, 100);
            Money aliceBalance = service.GetBalance(Alice);
            SpinResult bobSpin = SpinAndShow(service, Bob, GameMode.Inner, 100);

            Assert.That(bobSpin.TicketIndex, Is.EqualTo(3), "Bob continues the shared deal");
            Assert.That(service.GetBalance(Alice), Is.EqualTo(aliceBalance), "Bob's spin doesn't touch Alice's balance");
            Assert.That(SpinAndShow(service, Alice, GameMode.Inner, 100).TicketIndex, Is.EqualTo(4));
        }

        [Test]
        public void ASpin_IsRejected_WhileThePreviousResultHasNotBeenShown()
        {
            DealService service = ReadyToPlay(new InMemoryDealStore(), GameMode.Inner, 100);
            SpinResult first = service.Spin(Alice, GameMode.Inner, OneDollar).Result;

            SpinOutcome second = service.Spin(Alice, GameMode.Inner, OneDollar);

            Assert.That(second.Rejection, Is.EqualTo(SpinRejection.PreviousResultNotShown));
            Assert.That(service.GetActiveDealInfo(GameMode.Inner, OneDollar).SpinsPlayed, Is.EqualTo(1));
            Assert.That(service.MarkShown(Alice, first.SpinId + 99), Is.False, "only the pending spin can be marked shown");
            Assert.That(service.MarkShown(Alice, first.SpinId), Is.True);
            Assert.That(service.Spin(Alice, GameMode.Inner, OneDollar).Result.TicketIndex, Is.EqualTo(1));
        }

        [Test]
        public void UnknownPlayer_UnknownBet_AndUnpreparedDeal_AreRejected()
        {
            DealService service = Open(new InMemoryDealStore());
            service.RegisterPlayer(Alice, StartingBalance);

            Assert.That(service.Spin(new PlayerId("nobody"), GameMode.Inner, OneDollar).Rejection, Is.EqualTo(SpinRejection.UnknownPlayer));
            Assert.That(service.Spin(Alice, GameMode.Inner, TestConfigs.Bet(30)).Rejection, Is.EqualTo(SpinRejection.UnknownBetLevel));
            Assert.That(service.Spin(Alice, GameMode.Inner, OneDollar).Rejection, Is.EqualTo(SpinRejection.NoActiveDeal));
            Assert.That(service.GetAvailability(GameMode.Inner, OneDollar).Status, Is.EqualTo(AvailabilityStatus.NotPrepared));
        }

        [Test]
        public void ADealThatCannotBeBuilt_MakesTheComboUnavailable_WithTheReason()
        {
            var config = TestConfigs.ShippedWith(root => root["placementLimits"]["maxConsecutiveLossesOverride"] = 5);
            DealService service = Open(new InMemoryDealStore(), config);
            service.RegisterPlayer(Alice, StartingBalance);

            service.EnsurePrepared(GameMode.Inner, OneDollar);

            DealAvailability availability = service.GetAvailability(GameMode.Inner, OneDollar);
            Assert.That(availability.Status, Is.EqualTo(AvailabilityStatus.Unavailable));
            Assert.That(availability.Reason, Does.Contain("LimitsImpossible"));
            Assert.That(service.Spin(Alice, GameMode.Inner, OneDollar).Rejection, Is.EqualTo(SpinRejection.NoActiveDeal));
        }

        [Test]
        public void AFailedSave_MeansTheSpinDidNotHappen()
        {
            var store = new FaultInjectingStore(new InMemoryDealStore());
            DealService service = ReadyToPlay(store, GameMode.Inner, 100);

            store.FailNextStateWrite = true;
            SpinOutcome failed = service.Spin(Alice, GameMode.Inner, OneDollar);

            Assert.That(failed.Rejection, Is.EqualTo(SpinRejection.StorageError));
            Assert.That(service.GetBalance(Alice), Is.EqualTo(StartingBalance));
            Assert.That(service.GetActiveDealInfo(GameMode.Inner, OneDollar).SpinsPlayed, Is.EqualTo(0));
            Assert.That(service.GetPendingResult(Alice), Is.Null);
            Assert.That(service.Spin(Alice, GameMode.Inner, OneDollar).Result.TicketIndex, Is.EqualTo(0), "the ticket was not used");
        }

        [Test]
        public void ACrashBetweenSavingAndLogging_IsRepairedOnTheNextStart()
        {
            var inner = new InMemoryDealStore();
            var store = new FaultInjectingStore(inner);
            DealService service = ReadyToPlay(store, GameMode.Inner, 100);

            store.FailNextSpinAppend = true;
            SpinOutcome outcome = service.Spin(Alice, GameMode.Inner, OneDollar);
            Assert.That(outcome.IsAccepted, Is.True, "state was committed, so the spin counts");
            Assert.That(inner.ReadSpinRecords(), Is.Empty);

            Open(inner); // the game restarts

            SpinRecord repaired = inner.ReadSpinRecords().Single();
            Assert.That(repaired.SpinId, Is.EqualTo(outcome.Result.SpinId));
            Assert.That(repaired.TicketIndex, Is.EqualTo(0));
        }

        [Test]
        public void Spins_NeverDrawRandomNumbers()
        {
            var random = new CountingRandomSource();
            DealService service = ReadyToPlay(new InMemoryDealStore(), GameMode.Outer, 100, random);
            int drawsAfterBuilding = random.Draws;

            for (int i = 0; i < 25; i++) SpinAndShow(service, Alice, GameMode.Outer, 100);

            Assert.That(drawsAfterBuilding, Is.GreaterThan(0), "building the deal shuffles");
            Assert.That(random.Draws, Is.EqualTo(drawsAfterBuilding), "brief §2: a spin never generates a random outcome");
        }

        [Test]
        public void Players_CanBeRegisteredOnce_AndBalancesCanNotGoNegative()
        {
            DealService service = Open(new InMemoryDealStore());

            Assert.That(service.RegisterPlayer(Alice, StartingBalance), Is.True);
            Assert.That(service.RegisterPlayer(Alice, Money.FromCents(1)), Is.False);
            Assert.That(service.GetBalance(Alice), Is.EqualTo(StartingBalance));

            service.AdjustBalance(Alice, Money.FromCents(-2500), "test");
            Assert.That(service.GetBalance(Alice), Is.EqualTo(Money.FromCents(7500)));
            Assert.Throws<System.InvalidOperationException>(() => service.AdjustBalance(Alice, Money.FromCents(-7501), "test"));
        }
    }
}
