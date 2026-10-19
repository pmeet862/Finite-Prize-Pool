using System;
using System.Linq;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Persistence;
using FinitePool.Engine.Service;
using FinitePool.Engine.Verification;
using FinitePool.Tests.Fakes;
using NUnit.Framework;
using static FinitePool.Tests.Fakes.Services;

namespace FinitePool.Tests
{
    public class LifecycleTests
    {
        private static readonly BetLevel OneDollar = TestConfigs.Bet(100);

        [Test]
        public void Preparing_LeavesAnActiveDeal_AndAVerifiedReadySuccessor()
        {
            var store = new InMemoryDealStore();
            DealService service = ReadyToPlay(store, GameMode.Inner, 100);

            DealEntry ready = ReadyDeal(store, GameMode.Inner, 100);

            Assert.That(ready, Is.Not.Null);
            Assert.That(ready.DealId, Is.Not.EqualTo(service.GetActiveDealInfo(GameMode.Inner, OneDollar).DealId));
            Assert.That(DealVerifier.Verify(store.ReadDeal(ready.DealId)).IsVerified, Is.True);
        }

        [Test]
        public void Retire_WithoutAReadySuccessorYet_BuildsAFreshVerifiedReplacement()
        {
            var store = new InMemoryDealStore();
            var scheduler = new ManualScheduler();
            DealService service = Open(store, scheduler: scheduler);
            service.RegisterPlayer(Alice, StartingBalance);
            service.EnsurePrepared(GameMode.Inner, OneDollar);
            scheduler.RunNext(); // the first deal becomes active; its successor is queued but not built
            string oldDealId = service.GetActiveDealInfo(GameMode.Inner, OneDollar).DealId;
            Assert.That(ReadyDeal(store, GameMode.Inner, 100), Is.Null);

            RetireRecord record = service.Retire(GameMode.Inner, OneDollar, "test");

            string newDealId = service.GetActiveDealInfo(GameMode.Inner, OneDollar).DealId;
            Assert.That(record.ReplacementDealId, Is.EqualTo(newDealId));
            Assert.That(newDealId, Is.Not.EqualTo(oldDealId));
            Assert.That(DealVerifier.Verify(store.ReadDeal(newDealId)).IsVerified, Is.True);
        }

        [Test]
        public void Retire_WithNoActiveDeal_Throws_AndChangesNothing()
        {
            DealService service = Open(new InMemoryDealStore());

            Assert.Throws<InvalidOperationException>(() => service.Retire(GameMode.Inner, OneDollar, "test"));
            Assert.That(service.GetRetiredDeals(), Is.Empty);
        }

        [Test]
        public void ANewConfigShippedSinceTheLastRun_CountsAsAConfigChange()
        {
            var store = new InMemoryDealStore();
            DealService before = ReadyToPlay(store, GameMode.Inner, 100);
            string activeId = before.GetActiveDealInfo(GameMode.Inner, OneDollar).DealId;
            string staleReadyId = ReadyDeal(store, GameMode.Inner, 100).DealId;
            DealConfig v2 = TestConfigs.ShippedWith(root => root["configVersion"] = "v2-test");

            DealService after = Open(store, v2); // the game restarts with a new config file

            Assert.That(after.GetActiveDealInfo(GameMode.Inner, OneDollar).DealId, Is.EqualTo(activeId));
            Assert.That(after.GetRetiredDeals().Single().DealId, Is.EqualTo(staleReadyId));
            Assert.That(ReadyDeal(store, GameMode.Inner, 100).ConfigVersion, Is.EqualTo("v2-test"));
        }

        [Test]
        public void AFailedBuild_IsRecordedOnce_AndOnlyRetriedWhenAskedAgain()
        {
            DealConfig impossible = TestConfigs.ShippedWith(root => root["placementLimits"]["maxConsecutiveLossesOverride"] = 5);
            DealService service = Open(new InMemoryDealStore(), impossible);
            service.RegisterPlayer(Alice, StartingBalance);

            service.EnsurePrepared(GameMode.Inner, OneDollar);
            service.Spin(Alice, GameMode.Inner, OneDollar);
            service.Spin(Alice, GameMode.Inner, OneDollar);
            Assert.That(service.GetBuildFailures(), Has.Count.EqualTo(1), "spins don't trigger rebuild attempts");

            service.EnsurePrepared(GameMode.Inner, OneDollar);
            Assert.That(service.GetBuildFailures(), Has.Count.EqualTo(2));
            Assert.That(service.GetBuildFailures()[0].Message, Does.Contain("LimitsImpossible"));
        }
    }
}
