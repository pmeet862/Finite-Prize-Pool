using System;
using System.Collections.Generic;
using System.Linq;
using FinitePool.Engine.Core;
using Newtonsoft.Json;

namespace FinitePool.Engine.Config
{
    /// <summary>
    /// Loads a <see cref="DealConfig"/> from JSON text (the game passes in the
    /// TextAsset's text; tests and tools read the same file).
    /// Parsing is strict: unknown fields are errors, so a typo in a bag name can't
    /// be silently ignored when the real math values are pasted in.
    /// Decimal values ("0.90", "2") are read as decimal, never as double.
    /// </summary>
    public static class DealConfigJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            FloatParseHandling = FloatParseHandling.Decimal
        };

        /// <summary>Parses and validates. Throws <see cref="DealConfigException"/> listing every problem.</summary>
        public static DealConfig Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new DealConfigException(new[] { "Config text is empty." });
            }

            DealConfigDocument document;
            try
            {
                document = JsonConvert.DeserializeObject<DealConfigDocument>(json, Settings);
            }
            catch (JsonException exception)
            {
                throw new DealConfigException(new[] { "JSON error: " + exception.Message });
            }

            var errors = new List<string>();
            DealConfig config = Map(document, errors);
            if (errors.Count == 0)
            {
                errors.AddRange(DealConfigValidator.Validate(config));
            }

            if (errors.Count > 0)
            {
                throw new DealConfigException(errors);
            }

            return config;
        }

        private static DealConfig Map(DealConfigDocument document, List<string> errors)
        {
            if (document == null)
            {
                errors.Add("Config document is null.");
                return null;
            }

            Fraction targetRtp = default;
            if (document.TargetRtp.HasValue)
            {
                try
                {
                    targetRtp = Fraction.FromDecimal(document.TargetRtp.Value);
                }
                catch (OverflowException)
                {
                    errors.Add($"targetRtp {document.TargetRtp.Value} has too many decimal places to hold exactly.");
                }
            }
            else
            {
                errors.Add("targetRtp is required.");
            }

            Fraction rtpTolerance = Fraction.Zero;
            if (document.RtpTolerance.HasValue)
            {
                try
                {
                    rtpTolerance = Fraction.FromDecimal(document.RtpTolerance.Value);
                }
                catch (OverflowException)
                {
                    errors.Add($"rtpTolerance {document.RtpTolerance.Value} has too many decimal places to hold exactly.");
                }
            }

            var betLevels = new List<BetLevel>();
            foreach (long cents in document.BetLevelsCents ?? new List<long>())
            {
                if (cents <= 0)
                {
                    errors.Add($"Bet level {cents} cents must be positive.");
                    continue;
                }

                betLevels.Add(BetLevel.FromCents(cents));
            }

            PlacementLimitsConfig placementLimits = MapPlacementLimits(document.PlacementLimits, errors);
            Dictionary<GameMode, ModeConfig> modes = MapModes(document.Modes, errors);

            return new DealConfig(
                document.ConfigVersion,
                document.IsPlaceholder,
                document.Notes,
                targetRtp,
                Money.FromCents(Required(document.BonusUnitValueCents, "bonusUnitValueCents", errors)),
                betLevels,
                Required(document.MaxGenerationAttempts, "maxGenerationAttempts", errors),
                Required(document.MaxDealTickets, "maxDealTickets", errors),
                placementLimits,
                modes,
                rtpTolerance);
        }

        private static PlacementLimitsConfig MapPlacementLimits(PlacementLimitsDocument document, List<string> errors)
        {
            if (document == null)
            {
                errors.Add("placementLimits is required.");
                document = new PlacementLimitsDocument();
            }

            LayoutStrategy strategy = LayoutStrategy.GapRepair;
            if (document.LayoutStrategy == null)
            {
                errors.Add("placementLimits.layoutStrategy is required.");
            }
            else if (!Enum.TryParse(document.LayoutStrategy, false, out strategy) || !Enum.IsDefined(typeof(LayoutStrategy), strategy))
            {
                errors.Add($"placementLimits.layoutStrategy '{document.LayoutStrategy}' is not one of: {string.Join(", ", Enum.GetNames(typeof(LayoutStrategy)))}.");
            }

            return new PlacementLimitsConfig(
                Required(document.LossRunFactor, "placementLimits.lossRunFactor", errors),
                Required(document.RegularGapFactor, "placementLimits.regularGapFactor", errors),
                Required(document.BigGapFactor, "placementLimits.bigGapFactor", errors),
                document.MaxConsecutiveLossesOverride,
                document.MaxRegularGapOverride,
                document.MaxBigGapOverride,
                Required(document.RepeatingIntervalMaxPeriod, "placementLimits.repeatingIntervalMaxPeriod", errors),
                strategy);
        }

        private static Dictionary<GameMode, ModeConfig> MapModes(Dictionary<string, ModeDocument> documents, List<string> errors)
        {
            var modes = new Dictionary<GameMode, ModeConfig>();
            if (documents == null)
            {
                errors.Add("modes is required.");
                return modes;
            }

            foreach (KeyValuePair<string, ModeDocument> pair in documents)
            {
                if (!Enum.TryParse(pair.Key, false, out GameMode mode) || !Enum.IsDefined(typeof(GameMode), mode))
                {
                    errors.Add($"Unknown mode '{pair.Key}'. Expected one of: {string.Join(", ", Enum.GetNames(typeof(GameMode)))}.");
                    continue;
                }

                BagDefinition regular = MapBag(pair.Value?.RegularBag, TicketCategory.Regular, $"modes.{pair.Key}.regularBag", errors);
                BagDefinition big = MapBag(pair.Value?.BigBag, TicketCategory.Big, $"modes.{pair.Key}.bigBag", errors);
                modes[mode] = new ModeConfig(mode, regular, big);
            }

            return modes;
        }

        private static BagDefinition MapBag(BagDocument document, TicketCategory category, string path, List<string> errors)
        {
            var entries = new List<TicketEntry>();
            if (document?.Tickets == null)
            {
                errors.Add($"{path}.tickets is required.");
                return new BagDefinition(category, entries);
            }

            for (int i = 0; i < document.Tickets.Count; i++)
            {
                TicketEntry entry = MapEntry(document.Tickets[i], $"{path}.tickets[{i}]", errors);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            return new BagDefinition(category, entries);
        }

        private static TicketEntry MapEntry(TicketEntryDocument document, string path, List<string> errors)
        {
            if (document == null)
            {
                errors.Add($"{path} is null.");
                return null;
            }

            int count = Required(document.Count, path + ".count", errors);
            switch (document.Kind)
            {
                case "Multiplier":
                    if (document.BonusUnits.HasValue)
                    {
                        errors.Add($"{path}: a Multiplier ticket must not set bonusUnits (a ticket pays one award only).");
                    }

                    return TicketEntry.ForMultiplier(Required(document.Multiplier, path + ".multiplier", errors), count);

                case "Bonus":
                    if (document.Multiplier.HasValue)
                    {
                        errors.Add($"{path}: a Bonus ticket must not set multiplier (a ticket pays one award only).");
                    }

                    return TicketEntry.ForBonus(Required(document.BonusUnits, path + ".bonusUnits", errors), count);

                default:
                    errors.Add($"{path}.kind '{document.Kind}' must be 'Multiplier' or 'Bonus'. Bags hold prize tickets only; losses come from the category list.");
                    return null;
            }
        }

        private static T Required<T>(T? value, string name, List<string> errors) where T : struct
        {
            if (!value.HasValue)
            {
                errors.Add(name + " is required.");
                return default;
            }

            return value.Value;
        }

        // ---- JSON shape. Kept private to this file so the rest of the engine only sees DealConfig. ----

        private sealed class DealConfigDocument
        {
            public string ConfigVersion { get; set; }
            public bool IsPlaceholder { get; set; }
            public string Notes { get; set; }
            public decimal? TargetRtp { get; set; }
            public decimal? RtpTolerance { get; set; }
            public long? BonusUnitValueCents { get; set; }
            public List<long> BetLevelsCents { get; set; }
            public int? MaxGenerationAttempts { get; set; }
            public long? MaxDealTickets { get; set; }
            public PlacementLimitsDocument PlacementLimits { get; set; }
            public Dictionary<string, ModeDocument> Modes { get; set; }
        }

        private sealed class PlacementLimitsDocument
        {
            public decimal? LossRunFactor { get; set; }
            public decimal? RegularGapFactor { get; set; }
            public decimal? BigGapFactor { get; set; }
            public int? MaxConsecutiveLossesOverride { get; set; }
            public int? MaxRegularGapOverride { get; set; }
            public int? MaxBigGapOverride { get; set; }
            public int? RepeatingIntervalMaxPeriod { get; set; }
            public string LayoutStrategy { get; set; }
        }

        private sealed class ModeDocument
        {
            public BagDocument RegularBag { get; set; }
            public BagDocument BigBag { get; set; }
        }

        private sealed class BagDocument
        {
            public List<TicketEntryDocument> Tickets { get; set; }
        }

        private sealed class TicketEntryDocument
        {
            public string Kind { get; set; }
            public decimal? Multiplier { get; set; }
            public int? BonusUnits { get; set; }
            public int? Count { get; set; }
        }
    }
}
