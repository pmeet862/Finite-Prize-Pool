using System.Collections.Generic;
using System.IO;
using FinitePool.Engine.Service;

namespace FinitePool.Engine.Persistence
{
    /// <summary>The audit records saved on disk, read without starting the engine.</summary>
    public sealed class SavedDealRecords
    {
        public string ConfigVersion { get; }
        public IReadOnlyList<DealEntry> Deals { get; }
        public IReadOnlyList<CompletedDealRecord> CompletedDeals { get; }
        public IReadOnlyList<RetireRecord> RetiredDeals { get; }
        public IReadOnlyList<BuildFailureRecord> BuildFailures { get; }

        public SavedDealRecords(string configVersion, IReadOnlyList<DealEntry> deals, IReadOnlyList<CompletedDealRecord> completedDeals,
            IReadOnlyList<RetireRecord> retiredDeals, IReadOnlyList<BuildFailureRecord> buildFailures)
        {
            ConfigVersion = configVersion;
            Deals = deals;
            CompletedDeals = completedDeals;
            RetiredDeals = retiredDeals;
            BuildFailures = buildFailures;
        }

        /// <summary>
        /// Reads the records under <paramref name="rootFolder"/> without changing any file.
        /// Unlike opening the engine, it never repairs, builds or saves anything, so the Editor
        /// can show records while the game isn't running. Empty when nothing has been saved yet.
        /// </summary>
        public static SavedDealRecords Read(string rootFolder)
        {
            string path = Path.Combine(rootFolder, FileDealStore.STATE_FILE_NAME);
            if (!File.Exists(path))
            {
                path = AtomicFile.BackupPath(path); // an interrupted save leaves only the backup
            }

            if (!File.Exists(path))
            {
                return new SavedDealRecords(null, new List<DealEntry>(), new List<CompletedDealRecord>(), new List<RetireRecord>(), new List<BuildFailureRecord>());
            }

            EngineState state = EngineStateJson.Deserialize(File.ReadAllText(path));
            return new SavedDealRecords(state.ConfigVersion, state.Deals, state.CompletedDeals, state.RetiredDeals, state.BuildFailures);
        }
    }
}
