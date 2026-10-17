using System;
using System.Collections.Generic;
using System.Linq;
using FinitePool.Engine.Core;

namespace FinitePool.Engine.Config
{
    /// <summary>
    /// Structural checks on a config: values that could never make sense.
    /// Whether a specific mode/bet can form an exact deal is NOT checked here;
    /// that is the calculator's job, and it reports a reason per combination.
    /// </summary>
    public static class DealConfigValidator
    {
        public static IReadOnlyList<string> Validate(DealConfig config)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(config.ConfigVersion))
            {
                errors.Add("configVersion is required.");
            }

            if (config.TargetRtp.Numerator <= 0)
            {
                errors.Add($"targetRtp must be positive (got {config.TargetRtp}).");
            }

            if (config.BonusUnitValue <= Money.Zero)
            {
                errors.Add($"bonusUnitValueCents must be positive (got {config.BonusUnitValue}).");
            }

            if (config.BetLevels.Count == 0)
            {
                errors.Add("betLevelsCents must list at least one bet.");
            }

            if (config.BetLevels.Distinct().Count() != config.BetLevels.Count)
            {
                errors.Add("betLevelsCents contains duplicates.");
            }

            if (config.MaxGenerationAttempts < 1)
            {
                errors.Add($"maxGenerationAttempts must be at least 1 (got {config.MaxGenerationAttempts}).");
            }

            if (config.MaxDealTickets < 1 || config.MaxDealTickets > int.MaxValue)
            {
                errors.Add($"maxDealTickets must be between 1 and {int.MaxValue} (got {config.MaxDealTickets}); a deal is held in one array.");
            }

            ValidatePlacementLimits(config.PlacementLimits, errors);

            foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
            {
                if (!config.Modes.TryGetValue(mode, out ModeConfig modeConfig))
                {
                    errors.Add($"modes.{mode} is missing.");
                    continue;
                }

                ValidateBag(modeConfig.RegularBag, $"modes.{mode}.regularBag", errors);
                ValidateBag(modeConfig.BigBag, $"modes.{mode}.bigBag", errors);
            }

            return errors;
        }

        private static void ValidatePlacementLimits(PlacementLimitsConfig limits, List<string> errors)
        {
            if (limits.LossRunFactor <= 0 || limits.RegularGapFactor <= 0 || limits.BigGapFactor <= 0)
            {
                errors.Add("placementLimits factors must all be positive.");
            }

            if (limits.MaxConsecutiveLossesOverride < 0 || limits.MaxRegularGapOverride < 0 || limits.MaxBigGapOverride < 0)
            {
                errors.Add("placementLimits overrides cannot be negative.");
            }

            if (limits.RepeatingIntervalMaxPeriod < 1)
            {
                errors.Add($"placementLimits.repeatingIntervalMaxPeriod must be at least 1 (got {limits.RepeatingIntervalMaxPeriod}).");
            }
        }

        private static void ValidateBag(BagDefinition bag, string path, List<string> errors)
        {
            if (bag.Entries.Count == 0)
            {
                errors.Add($"{path} has no tickets. Every cycle needs at least one prize ticket.");
                return;
            }

            for (int i = 0; i < bag.Entries.Count; i++)
            {
                TicketEntry entry = bag.Entries[i];
                string entryPath = $"{path}.tickets[{i}]";

                if (entry.Count < 1)
                {
                    errors.Add($"{entryPath}.count must be at least 1 (got {entry.Count}).");
                }

                if (entry.Kind == AwardKind.Multiplier && entry.Multiplier <= 0)
                {
                    errors.Add($"{entryPath}.multiplier must be positive (got {entry.Multiplier}).");
                }

                if (entry.Kind == AwardKind.Bonus && entry.BonusUnits <= 0)
                {
                    errors.Add($"{entryPath}.bonusUnits must be positive (got {entry.BonusUnits}).");
                }
            }
        }
    }
}
