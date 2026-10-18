using System.IO;
using FinitePool.Engine.Building;
using FinitePool.Engine.Core;
using FinitePool.Engine.Deals;
using FinitePool.Engine.Infrastructure;
using FinitePool.Engine.Persistence;
using FinitePool.Tests.Fakes;
using NUnit.Framework;

namespace FinitePool.Tests
{
    public class AtomicFileTests
    {
        private TempFolder m_folder;
        private string m_path;

        [SetUp]
        public void SetUp()
        {
            m_folder = new TempFolder();
            m_path = Path.Combine(m_folder.Path, "state.json");
        }

        [TearDown]
        public void TearDown() => m_folder.Dispose();

        [Test]
        public void Write_ReplacesTheFile_AndKeepsThePreviousVersionAsBackup()
        {
            AtomicFile.WriteAllText(m_path, "v1");
            AtomicFile.WriteAllText(m_path, "v2");

            Assert.That(AtomicFile.ReadAllText(m_path), Is.EqualTo("v2"));
            Assert.That(File.ReadAllText(AtomicFile.BackupPath(m_path)), Is.EqualTo("v1"));
            Assert.That(File.Exists(AtomicFile.TempPath(m_path)), Is.False);
        }

        [Test]
        public void ACrashDuringTheSwap_RestoresTheLastCompleteVersion()
        {
            AtomicFile.WriteAllText(m_path, "v1");
            AtomicFile.WriteAllText(m_path, "v2");
            File.Delete(m_path); // as if the swap stopped after moving the old file aside

            Assert.That(AtomicFile.ReadAllText(m_path), Is.EqualTo("v1"));
        }

        [Test]
        public void ACrashBeforeTheSwap_LeavesTheOldFile_AndDropsTheUnfinishedTemp()
        {
            AtomicFile.WriteAllText(m_path, "v1");
            File.WriteAllText(AtomicFile.TempPath(m_path), "half-writ"); // crash while writing v2

            Assert.That(AtomicFile.ReadAllText(m_path), Is.EqualTo("v1"));
            Assert.That(File.Exists(AtomicFile.TempPath(m_path)), Is.False);
        }

        [Test]
        public void MissingFile_ReadsAsNull()
        {
            Assert.That(AtomicFile.ReadAllText(m_path), Is.Null);
        }
    }

    public class FileDealStoreTests
    {
        private TempFolder m_folder;

        [SetUp]
        public void SetUp() => m_folder = new TempFolder();

        [TearDown]
        public void TearDown() => m_folder.Dispose();

        [Test]
        public void ADeal_IsWrittenOnce_AndReadsBackIdentically()
        {
            var store = new FileDealStore(m_folder.Path);
            Deal deal = new DealBuilder(new CryptoRandomSource(), new FakeClock(), NullEngineLog.Instance)
                .Build(TestConfigs.Placeholder(), GameMode.Inner, TestConfigs.Bet(100)).Deal;

            store.WriteDeal(deal);

            Assert.That(store.ReadDeal(deal.DealId).TicketTypeIds, Is.EqualTo(deal.TicketTypeIds));
            Assert.Throws<System.InvalidOperationException>(() => store.WriteDeal(deal), "deals are never rewritten");
        }

        [Test]
        public void State_RoundTrips()
        {
            var store = new FileDealStore(m_folder.Path);
            EngineState state = EngineState.CreateNew("v-test");
            state.Balances["alice"] = 1234;
            state.NextSpinId = 42;

            store.WriteState(state);
            EngineState loaded = new FileDealStore(m_folder.Path).ReadState();

            Assert.That(loaded.ConfigVersion, Is.EqualTo("v-test"));
            Assert.That(loaded.Balances["alice"], Is.EqualTo(1234));
            Assert.That(loaded.NextSpinId, Is.EqualTo(42));
        }

        [Test]
        public void SavedRecords_CanBeReadWithoutTheEngine_AndReadingChangesNoFile()
        {
            Assert.That(SavedDealRecords.Read(m_folder.Path).CompletedDeals, Is.Empty, "nothing saved yet");

            var store = new FileDealStore(m_folder.Path);
            EngineState state = EngineState.CreateNew("v-test");
            state.Deals.Add(new DealEntry { DealId = "active-1", State = DealState.Active });
            state.CompletedDeals.Add(new FinitePool.Engine.Service.CompletedDealRecord { DealId = "done-1" });
            state.RetiredDeals.Add(new FinitePool.Engine.Service.RetireRecord { DealId = "retired-1" });
            store.WriteState(state);
            store.WriteState(state); // leaves a .bak next to state.json
            string statePath = Path.Combine(m_folder.Path, FileDealStore.STATE_FILE_NAME);
            File.Delete(statePath); // as if a save stopped halfway: only the backup is left

            SavedDealRecords records = SavedDealRecords.Read(m_folder.Path);

            Assert.That((records.Deals[0].DealId, records.Deals[0].State), Is.EqualTo(("active-1", DealState.Active)));
            Assert.That(records.CompletedDeals[0].DealId, Is.EqualTo("done-1"));
            Assert.That(records.RetiredDeals[0].DealId, Is.EqualTo("retired-1"));
            Assert.That(File.Exists(statePath), Is.False, "reading does not run crash recovery");
            Assert.That(File.Exists(AtomicFile.BackupPath(statePath)), Is.True);
        }

        [Test]
        public void AHalfWrittenLastSpinRecord_IsDroppedOnOpen()
        {
            var store = new FileDealStore(m_folder.Path);
            store.AppendSpinRecord(new SpinRecord { SpinId = 1, PlayerId = "alice", DealId = "d" });
            store.AppendSpinRecord(new SpinRecord { SpinId = 2, PlayerId = "alice", DealId = "d" });
            File.AppendAllText(Path.Combine(m_folder.Path, "spins.log.jsonl"), "{\"spinId\":3,\"playerId\":\"al");

            var reopened = new FileDealStore(m_folder.Path);

            Assert.That(reopened.ReadSpinRecords(), Has.Count.EqualTo(2));
        }
    }
}
