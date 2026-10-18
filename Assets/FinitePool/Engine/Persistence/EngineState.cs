using System;
using System.Collections.Generic;
using System.Linq;
using FinitePool.Engine.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace FinitePool.Engine.Persistence
{
    /// <summary>
    /// Everything about the engine that changes, in one small object saved as
    /// state.json (A-23). Replacing that file atomically is the single commit point
    /// for every spin, so the deal cursor, the player's balance and the pending
    /// result can never disagree after a crash.
    ///
    /// Big or unchanging data lives elsewhere: deals in their own write-once files,
    /// spin history in an append-only log.
    /// </summary>
    internal sealed class EngineState
    {
        public const int CURRENT_FORMAT_VERSION = 1;

        public int FormatVersion { get; set; } = CURRENT_FORMAT_VERSION;

        /// <summary>The config version in use the last time state was saved.</summary>
        public string ConfigVersion { get; set; }

        /// <summary>The id the next spin will get. Device-wide and always increasing (A-22).</summary>
        public long NextSpinId { get; set; } = 1;

        /// <summary>Every deal the engine knows about, whatever its state.</summary>
        public List<DealEntry> Deals { get; set; } = new List<DealEntry>();

        /// <summary>Player id → balance in money units.</summary>
        public Dictionary<string, long> Balances { get; set; } = new Dictionary<string, long>();

        /// <summary>Player id → their last spin, saved but not yet shown on screen (A-17).</summary>
        public Dictionary<string, SpinRecord> PendingSpins { get; set; } = new Dictionary<string, SpinRecord>();

        /// <summary>The most recent committed spin. Used to repair the spin log after a crash.</summary>
        public SpinRecord LastCommittedSpin { get; set; }

        public static EngineState CreateNew(string configVersion) => new EngineState { ConfigVersion = configVersion };

        public DealEntry FindDeal(string dealId) => Deals.FirstOrDefault(entry => entry.DealId == dealId);

        public DealEntry FindDeal(GameMode mode, BetLevel bet, DealState state) =>
            Deals.FirstOrDefault(entry => entry.Mode == mode && entry.BetUnits == bet.Amount.Units && entry.State == state);
    }

    /// <summary>The engine's bookkeeping for one deal. The tickets themselves are in the deal file.</summary>
    internal sealed class DealEntry
    {
        public string DealId { get; set; }
        public GameMode Mode { get; set; }
        public long BetUnits { get; set; }
        public string ConfigVersion { get; set; }
        public DealState State { get; set; }
        public int TicketCount { get; set; }

        /// <summary>Position of the next ticket to use. Only ever moves forward.</summary>
        public int Cursor { get; set; }

        public long WagersUnits { get; set; }
        public long PayoutsUnits { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? ActivatedUtc { get; set; }
        public DateTime? ClosedUtc { get; set; }
    }

    /// <summary>
    /// One spin, saved before any animation plays (brief §4.5). It holds absolute
    /// "after" values (balance, cursor, deal totals) as well as the change, so
    /// applying it twice gives the same result. That makes crash repair safe.
    /// </summary>
    internal sealed class SpinRecord
    {
        public long SpinId { get; set; }
        public string PlayerId { get; set; }
        public GameMode Mode { get; set; }
        public long BetUnits { get; set; }
        public string DealId { get; set; }

        /// <summary>0-based position of the ticket in the deal.</summary>
        public int TicketIndex { get; set; }

        public TicketCategory Category { get; set; }
        public AwardKind AwardKind { get; set; }
        public long WagerUnits { get; set; }
        public long AwardUnits { get; set; }
        public long BalanceBeforeUnits { get; set; }
        public long BalanceAfterUnits { get; set; }
        public int CursorAfter { get; set; }
        public long DealWagersAfterUnits { get; set; }
        public long DealPayoutsAfterUnits { get; set; }
        public DateTime TimestampUtc { get; set; }
    }

    /// <summary>JSON for state and spin records: camelCase keys, enums as names, UTC dates.</summary>
    internal static class EngineStateJson
    {
        private static readonly JsonSerializerSettings Indented = CreateSettings(Formatting.Indented);
        private static readonly JsonSerializerSettings SingleLine = CreateSettings(Formatting.None);

        public static string Serialize(EngineState state) => JsonConvert.SerializeObject(state, Indented);

        public static EngineState Deserialize(string json)
        {
            EngineState state = JsonConvert.DeserializeObject<EngineState>(json, Indented);
            if (state == null || state.FormatVersion != EngineState.CURRENT_FORMAT_VERSION)
            {
                throw new InvalidOperationException($"Unsupported state format {state?.FormatVersion}.");
            }

            return state;
        }

        /// <summary>A deep copy. Changes are made on a copy and only become live once saved.</summary>
        public static EngineState Clone(EngineState state) => Deserialize(Serialize(state));

        /// <summary>One line, for the append-only spin log.</summary>
        public static string SerializeLine(SpinRecord record) => JsonConvert.SerializeObject(record, SingleLine);

        public static SpinRecord DeserializeLine(string line) => JsonConvert.DeserializeObject<SpinRecord>(line, SingleLine);

        private static JsonSerializerSettings CreateSettings(Formatting formatting) => new JsonSerializerSettings
        {
            Formatting = formatting,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter() },
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            FloatParseHandling = FloatParseHandling.Decimal
        };
    }
}
