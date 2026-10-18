using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FinitePool.Engine.Deals;
using Newtonsoft.Json;

namespace FinitePool.Engine.Persistence
{
    /// <summary>
    /// Where the engine keeps its data. Tests and the simulator use memory; the game
    /// uses files. A server version would put a database behind the same interface.
    /// Implementations must allow deals to be written from a background thread while
    /// the main thread saves state.
    /// </summary>
    internal interface IDealStore
    {
        /// <summary>Saves a deal once. Throws if a deal with that id already exists: deals are never rewritten.</summary>
        void WriteDeal(Deal deal);

        /// <summary>Loads a deal. Throws <see cref="DealFileException"/> if it is missing or corrupt.</summary>
        Deal ReadDeal(string dealId);

        /// <summary>The saved state, or null on first run.</summary>
        EngineState ReadState();

        /// <summary>Replaces the saved state atomically. This is the commit point for every change.</summary>
        void WriteState(EngineState state);

        void AppendSpinRecord(SpinRecord record);

        /// <summary>The spin history, oldest first.</summary>
        IReadOnlyList<SpinRecord> ReadSpinRecords();
    }

    /// <summary>
    /// Keeps everything in memory, but stores it as serialized text, exactly as the
    /// file store would. So a "reload" in a test really does read back saved data,
    /// with no shared object references to hide bugs.
    /// </summary>
    internal sealed class InMemoryDealStore : IDealStore
    {
        private readonly object m_sync = new object();
        private readonly Dictionary<string, string> m_deals = new Dictionary<string, string>();
        private readonly List<string> m_spinLines = new List<string>();
        private string m_state;

        public void WriteDeal(Deal deal)
        {
            string json = DealFileCodec.Serialize(deal);
            lock (m_sync)
            {
                if (m_deals.ContainsKey(deal.DealId))
                {
                    throw new InvalidOperationException($"Deal {deal.DealId} already exists; deals are written once.");
                }

                m_deals[deal.DealId] = json;
            }
        }

        public Deal ReadDeal(string dealId)
        {
            string json;
            lock (m_sync)
            {
                if (!m_deals.TryGetValue(dealId, out json))
                {
                    throw new DealFileException($"Deal {dealId} not found.");
                }
            }

            return DealFileCodec.Deserialize(json);
        }

        public EngineState ReadState()
        {
            lock (m_sync)
            {
                return m_state == null ? null : EngineStateJson.Deserialize(m_state);
            }
        }

        public void WriteState(EngineState state)
        {
            string json = EngineStateJson.Serialize(state);
            lock (m_sync)
            {
                m_state = json;
            }
        }

        public void AppendSpinRecord(SpinRecord record)
        {
            string line = EngineStateJson.SerializeLine(record);
            lock (m_sync)
            {
                m_spinLines.Add(line);
            }
        }

        public IReadOnlyList<SpinRecord> ReadSpinRecords()
        {
            lock (m_sync)
            {
                return m_spinLines.Select(EngineStateJson.DeserializeLine).ToList();
            }
        }
    }

    /// <summary>
    /// Saves under one root folder (the game passes Application.persistentDataPath + "/FinitePool"):
    ///   deals/{dealId}.deal.json  write-once deal files
    ///   state.json                all mutable state, replaced atomically
    ///   spins.log.jsonl           append-only spin history, one JSON object per line
    /// </summary>
    internal sealed class FileDealStore : IDealStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly string m_dealsFolder;
        private readonly string m_statePath;
        private readonly string m_spinLogPath;

        public FileDealStore(string rootFolder)
        {
            if (string.IsNullOrWhiteSpace(rootFolder)) throw new ArgumentException("Root folder is required.", nameof(rootFolder));

            m_dealsFolder = Path.Combine(rootFolder, "deals");
            m_statePath = Path.Combine(rootFolder, "state.json");
            m_spinLogPath = Path.Combine(rootFolder, "spins.log.jsonl");
            Directory.CreateDirectory(m_dealsFolder);
            DropIncompleteLastSpinRecord();
        }

        public void WriteDeal(Deal deal)
        {
            string path = DealPath(deal.DealId);
            AtomicFile.Recover(path);
            if (File.Exists(path))
            {
                throw new InvalidOperationException($"Deal {deal.DealId} already exists; deals are written once.");
            }

            AtomicFile.WriteAllText(path, DealFileCodec.Serialize(deal));
        }

        public Deal ReadDeal(string dealId)
        {
            string json = AtomicFile.ReadAllText(DealPath(dealId));
            if (json == null)
            {
                throw new DealFileException($"Deal file for {dealId} not found.");
            }

            return DealFileCodec.Deserialize(json);
        }

        public EngineState ReadState()
        {
            string json = AtomicFile.ReadAllText(m_statePath);
            return json == null ? null : EngineStateJson.Deserialize(json);
        }

        public void WriteState(EngineState state) => AtomicFile.WriteAllText(m_statePath, EngineStateJson.Serialize(state));

        public void AppendSpinRecord(SpinRecord record)
        {
            byte[] line = Utf8NoBom.GetBytes(EngineStateJson.SerializeLine(record) + "\n");
            using (var stream = new FileStream(m_spinLogPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                stream.Write(line, 0, line.Length);
                stream.Flush(flushToDisk: true);
            }
        }

        public IReadOnlyList<SpinRecord> ReadSpinRecords()
        {
            if (!File.Exists(m_spinLogPath))
            {
                return new List<SpinRecord>();
            }

            return File.ReadAllLines(m_spinLogPath, Utf8NoBom)
                .Where(line => line.Length > 0)
                .Select(EngineStateJson.DeserializeLine)
                .ToList();
        }

        private string DealPath(string dealId) => Path.Combine(m_dealsFolder, dealId + ".deal.json");

        /// <summary>
        /// A crash in the middle of an append can leave half a line at the end of the
        /// log. That spin's commit (state.json) already happened or never did, so the
        /// half line carries nothing state.json doesn't. It is cut off here, and the
        /// service re-appends the last committed spin if the log is behind.
        /// </summary>
        private void DropIncompleteLastSpinRecord()
        {
            if (!File.Exists(m_spinLogPath))
            {
                return;
            }

            List<string> lines = File.ReadAllLines(m_spinLogPath, Utf8NoBom).Where(line => line.Length > 0).ToList();
            if (lines.Count == 0 || IsCompleteRecord(lines[lines.Count - 1]))
            {
                return;
            }

            lines.RemoveAt(lines.Count - 1);
            AtomicFile.WriteAllText(m_spinLogPath, lines.Count == 0 ? string.Empty : string.Join("\n", lines) + "\n");
        }

        private static bool IsCompleteRecord(string line)
        {
            try
            {
                return EngineStateJson.DeserializeLine(line) != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
