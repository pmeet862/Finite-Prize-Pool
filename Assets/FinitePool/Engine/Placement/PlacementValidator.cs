using System;
using System.Collections.Generic;
using FinitePool.Engine.Core;

namespace FinitePool.Engine.Placement
{
    /// <summary>
    /// Measures a category layout in one pass and checks it against the limits.
    /// A "gap" counts the slots between two separators, plus the slots before the
    /// first separator and after the last one (both ends included, A-10):
    ///   loss run    = losses between prize slots
    ///   Regular gap = non-Regular slots between Regular slots
    ///   Big gap     = non-Big slots between Big slots
    /// The builder and the verifier both use this, so they can never disagree.
    /// </summary>
    public static class PlacementValidator
    {
        public static PlacementReport Validate(IReadOnlyList<TicketCategory> layout, PlacementLimits limits)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (limits == null) throw new ArgumentNullException(nameof(limits));

            int regularCount = 0, bigCount = 0, lossCount = 0;
            int lossRun = 0, regularGap = 0, bigGap = 0;
            int longestLossRun = 0, longestRegularGap = 0, longestBigGap = 0;
            var prizePositions = new List<int>();

            for (int slot = 0; slot < layout.Count; slot++)
            {
                switch (layout[slot])
                {
                    case TicketCategory.Loss:
                        lossCount++;
                        lossRun++;
                        regularGap++;
                        bigGap++;
                        break;

                    case TicketCategory.Regular:
                        regularCount++;
                        longestLossRun = Math.Max(longestLossRun, lossRun);
                        longestRegularGap = Math.Max(longestRegularGap, regularGap);
                        lossRun = 0;
                        regularGap = 0;
                        bigGap++;
                        prizePositions.Add(slot);
                        break;

                    case TicketCategory.Big:
                        bigCount++;
                        longestLossRun = Math.Max(longestLossRun, lossRun);
                        longestBigGap = Math.Max(longestBigGap, bigGap);
                        lossRun = 0;
                        bigGap = 0;
                        regularGap++;
                        prizePositions.Add(slot);
                        break;

                    default:
                        throw new ArgumentException($"Unknown category {layout[slot]} at slot {slot}.", nameof(layout));
                }
            }

            // The run after the last separator counts too.
            longestLossRun = Math.Max(longestLossRun, lossRun);
            longestRegularGap = Math.Max(longestRegularGap, regularGap);
            longestBigGap = Math.Max(longestBigGap, bigGap);

            int repeatingPeriod = RepeatingIntervalDetector.FindRepeatingPeriod(prizePositions, limits.RepeatingIntervalMaxPeriod);

            return new PlacementReport(
                limits,
                regularCount,
                bigCount,
                lossCount,
                longestLossRun,
                longestRegularGap,
                longestBigGap,
                repeatingPeriod);
        }
    }

    /// <summary>What a layout looks like, and which limits (if any) it breaks.</summary>
    public sealed class PlacementReport
    {
        public PlacementLimits Limits { get; }
        public int RegularCount { get; }
        public int BigCount { get; }
        public int LossCount { get; }
        public int LongestLossRun { get; }
        public int LongestRegularGap { get; }
        public int LongestBigGap { get; }

        /// <summary>The repeating prize-spacing period found, or 0 when there is none.</summary>
        public int RepeatingPeriod { get; }

        public IReadOnlyList<string> Violations { get; }
        public bool IsValid => Violations.Count == 0;

        public PlacementReport(
            PlacementLimits limits,
            int regularCount,
            int bigCount,
            int lossCount,
            int longestLossRun,
            int longestRegularGap,
            int longestBigGap,
            int repeatingPeriod)
        {
            Limits = limits;
            RegularCount = regularCount;
            BigCount = bigCount;
            LossCount = lossCount;
            LongestLossRun = longestLossRun;
            LongestRegularGap = longestRegularGap;
            LongestBigGap = longestBigGap;
            RepeatingPeriod = repeatingPeriod;
            Violations = FindViolations();
        }

        private List<string> FindViolations()
        {
            var violations = new List<string>();
            if (LongestLossRun > Limits.MaxConsecutiveLosses)
            {
                violations.Add($"{LongestLossRun} consecutive losses (max {Limits.MaxConsecutiveLosses})");
            }

            if (LongestRegularGap > Limits.MaxRegularGap)
            {
                violations.Add($"Regular gap of {LongestRegularGap} (max {Limits.MaxRegularGap})");
            }

            if (LongestBigGap > Limits.MaxBigGap)
            {
                violations.Add($"Big gap of {LongestBigGap} (max {Limits.MaxBigGap})");
            }

            if (RepeatingPeriod > 0)
            {
                violations.Add($"prize slots repeat a fixed spacing pattern of period {RepeatingPeriod}");
            }

            return violations;
        }

        public override string ToString() => IsValid
            ? $"valid (longest loss run {LongestLossRun}, Regular gap {LongestRegularGap}, Big gap {LongestBigGap})"
            : "invalid: " + string.Join("; ", Violations);
    }
}
