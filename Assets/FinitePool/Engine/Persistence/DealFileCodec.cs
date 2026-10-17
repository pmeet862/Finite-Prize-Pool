using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FinitePool.Engine.Core;
using FinitePool.Engine.Deals;
using FinitePool.Engine.Placement;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace FinitePool.Engine.Persistence
{
    /// <summary>Thrown when a saved deal file can't be read back exactly as written.</summary>
    public sealed class DealFileException : Exception
    {
        public DealFileException(string message) : base(message) { }
    }

    /// <summary>
    /// Turns a deal into JSON text and back. A deal file is written once and never
    /// changed (brief §4.3 step 4).
    ///
    /// Tickets are stored compactly: each ticket is a 2-byte id into the deal's
    /// ticket-type table, little-endian, then base64. A 2-million-ticket deal is
    /// about 5 MB instead of hundreds of MB of JSON objects. A SHA-256 of those
    /// bytes is stored alongside, so any corruption is caught on load.
    /// </summary>
    internal static class DealFileCodec
    {
        public const int FORMAT_VERSION = 1;

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            FloatParseHandling = FloatParseHandling.Decimal,
            DateParseHandling = DateParseHandling.None,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented
        };

        public static string Serialize(Deal deal)
        {
            byte[] ticketBytes = ToBytes(deal.TicketTypeIds);
            var document = new DealDocument
            {
                FormatVersion = FORMAT_VERSION,
                DealId = deal.DealId,
                Mode = deal.Mode.ToString(),
                BetCents = deal.Bet.Amount.Units,
                ConfigVersion = deal.ConfigVersion,
                CreatedUtc = deal.CreatedUtc.ToString("o", CultureInfo.InvariantCulture),
                TargetRtpNumerator = deal.TargetRtp.Numerator,
                TargetRtpDenominator = deal.TargetRtp.Denominator,
                PairedCycles = deal.PairedCycles,
                BonusUnitValueUnits = deal.BonusUnitValue.Units,
                Limits = new LimitsDocument
                {
                    MaxConsecutiveLosses = deal.Limits.MaxConsecutiveLosses,
                    MaxRegularGap = deal.Limits.MaxRegularGap,
                    MaxBigGap = deal.Limits.MaxBigGap,
                    RepeatingIntervalMaxPeriod = deal.Limits.RepeatingIntervalMaxPeriod
                },
                TicketTypes = new List<TicketTypeDocument>(),
                TicketCount = deal.TicketCount,
                Tickets = Convert.ToBase64String(ticketBytes),
                TicketsSha256 = Sha256Hex(ticketBytes)
            };

            foreach (TicketType type in deal.TicketTypes)
            {
                document.TicketTypes.Add(new TicketTypeDocument
                {
                    Id = type.Id,
                    Category = type.Category.ToString(),
                    Kind = type.Kind.ToString(),
                    Multiplier = type.Multiplier.ToString(CultureInfo.InvariantCulture),
                    BonusUnits = type.BonusUnits,
                    AwardUnits = type.Award.Units,
                    CountPerCycle = type.CountPerCycle
                });
            }

            return JsonConvert.SerializeObject(document, Settings);
        }

        /// <summary>Reads a deal back. Throws <see cref="DealFileException"/> on any mismatch, including a bad hash.</summary>
        public static Deal Deserialize(string json)
        {
            DealDocument document;
            try
            {
                document = JsonConvert.DeserializeObject<DealDocument>(json, Settings);
            }
            catch (JsonException exception)
            {
                throw new DealFileException("Deal file is not valid JSON: " + exception.Message);
            }

            if (document == null)
            {
                throw new DealFileException("Deal file is empty.");
            }

            if (document.FormatVersion != FORMAT_VERSION)
            {
                throw new DealFileException($"Unsupported deal file format {document.FormatVersion} (expected {FORMAT_VERSION}).");
            }

            byte[] ticketBytes;
            try
            {
                ticketBytes = Convert.FromBase64String(document.Tickets ?? string.Empty);
            }
            catch (FormatException)
            {
                throw new DealFileException($"Deal {document.DealId}: ticket data is not valid base64.");
            }

            if (!string.Equals(Sha256Hex(ticketBytes), document.TicketsSha256, StringComparison.Ordinal))
            {
                throw new DealFileException($"Deal {document.DealId}: ticket data does not match its SHA-256. The file is corrupt.");
            }

            ushort[] ticketTypeIds = FromBytes(ticketBytes);
            if (ticketTypeIds.Length != document.TicketCount)
            {
                throw new DealFileException($"Deal {document.DealId}: {ticketTypeIds.Length} tickets stored, header says {document.TicketCount}.");
            }

            try
            {
                return BuildDeal(document, ticketTypeIds);
            }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException || exception is OverflowException)
            {
                throw new DealFileException($"Deal {document.DealId}: header is invalid ({exception.Message}).");
            }
        }

        private static Deal BuildDeal(DealDocument document, ushort[] ticketTypeIds)
        {
            var types = new List<TicketType>();
            foreach (TicketTypeDocument type in document.TicketTypes ?? new List<TicketTypeDocument>())
            {
                types.Add(new TicketType(
                    type.Id,
                    ParseEnum<TicketCategory>(type.Category, document.DealId),
                    ParseEnum<AwardKind>(type.Kind, document.DealId),
                    decimal.Parse(type.Multiplier, NumberStyles.Number, CultureInfo.InvariantCulture),
                    type.BonusUnits,
                    Money.FromUnits(type.AwardUnits),
                    type.CountPerCycle));
            }

            foreach (ushort id in ticketTypeIds)
            {
                if (id >= types.Count)
                {
                    throw new DealFileException($"Deal {document.DealId}: ticket type id {id} has no entry in the type table.");
                }
            }

            LimitsDocument limits = document.Limits ?? throw new DealFileException($"Deal {document.DealId}: placement limits missing.");

            return new Deal(
                document.DealId,
                ParseEnum<GameMode>(document.Mode, document.DealId),
                BetLevel.FromCents(document.BetCents),
                document.ConfigVersion,
                DateTime.Parse(document.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                new Fraction(document.TargetRtpNumerator, document.TargetRtpDenominator),
                document.PairedCycles,
                Money.FromUnits(document.BonusUnitValueUnits),
                new PlacementLimits(limits.MaxConsecutiveLosses, limits.MaxRegularGap, limits.MaxBigGap, limits.RepeatingIntervalMaxPeriod),
                types,
                ticketTypeIds);
        }

        private static byte[] ToBytes(IReadOnlyList<ushort> ids)
        {
            var bytes = new byte[ids.Count * 2];
            for (int i = 0; i < ids.Count; i++)
            {
                bytes[2 * i] = (byte)(ids[i] & 0xFF);
                bytes[2 * i + 1] = (byte)(ids[i] >> 8);
            }

            return bytes;
        }

        private static ushort[] FromBytes(byte[] bytes)
        {
            if (bytes.Length % 2 != 0)
            {
                throw new DealFileException("Ticket data has an odd number of bytes.");
            }

            var ids = new ushort[bytes.Length / 2];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = (ushort)(bytes[2 * i] | (bytes[2 * i + 1] << 8));
            }

            return ids;
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return hex.ToString();
            }
        }

        private static T ParseEnum<T>(string value, string dealId) where T : struct
        {
            if (!Enum.TryParse(value, false, out T parsed) || !Enum.IsDefined(typeof(T), parsed))
            {
                throw new DealFileException($"Deal {dealId}: '{value}' is not a valid {typeof(T).Name}.");
            }

            return parsed;
        }

        // ---- File shape ----

        private sealed class DealDocument
        {
            public int FormatVersion { get; set; }
            public string DealId { get; set; }
            public string Mode { get; set; }
            public long BetCents { get; set; }
            public string ConfigVersion { get; set; }
            public string CreatedUtc { get; set; }
            public long TargetRtpNumerator { get; set; }
            public long TargetRtpDenominator { get; set; }
            public long PairedCycles { get; set; }
            public long BonusUnitValueUnits { get; set; }
            public LimitsDocument Limits { get; set; }
            public List<TicketTypeDocument> TicketTypes { get; set; }
            public int TicketCount { get; set; }
            public string Tickets { get; set; }
            public string TicketsSha256 { get; set; }
        }

        private sealed class LimitsDocument
        {
            public int MaxConsecutiveLosses { get; set; }
            public int MaxRegularGap { get; set; }
            public int MaxBigGap { get; set; }
            public int RepeatingIntervalMaxPeriod { get; set; }
        }

        private sealed class TicketTypeDocument
        {
            public ushort Id { get; set; }
            public string Category { get; set; }
            public string Kind { get; set; }
            public string Multiplier { get; set; }
            public int BonusUnits { get; set; }
            public long AwardUnits { get; set; }
            public int CountPerCycle { get; set; }
        }
    }
}
