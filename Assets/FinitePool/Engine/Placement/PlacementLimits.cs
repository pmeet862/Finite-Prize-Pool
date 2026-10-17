using System;
using FinitePool.Engine.Config;
using FinitePool.Engine.Sizing;

namespace FinitePool.Engine.Placement
{
    /// <summary>
    /// The concrete brief §4.4 limits for one deal. With L losses, P prize slots,
    /// R Regular slots, B Big slots and N total slots:
    ///   max consecutive losses = floor(2 × L ÷ (P + 1))
    ///   max gap between Regular slots (both ends included) = floor(2 × N ÷ (R + 1))
    ///   max gap between Big slots (both ends included) = floor(2 × N ÷ (B + 1))
    /// "2" is the configurable factor; an override replaces the formula entirely.
    /// floor() is the strict reading: a run length is a whole number (A-09).
    /// </summary>
    public sealed class PlacementLimits
    {
        public int MaxConsecutiveLosses { get; }
        public int MaxRegularGap { get; }
        public int MaxBigGap { get; }

        /// <summary>Longest spacing pattern that counts as a fixed repeating interval (A-11).</summary>
        public int RepeatingIntervalMaxPeriod { get; }

        public PlacementLimits(int maxConsecutiveLosses, int maxRegularGap, int maxBigGap, int repeatingIntervalMaxPeriod)
        {
            MaxConsecutiveLosses = maxConsecutiveLosses;
            MaxRegularGap = maxRegularGap;
            MaxBigGap = maxBigGap;
            RepeatingIntervalMaxPeriod = repeatingIntervalMaxPeriod;
        }

        public static PlacementLimits For(DealSize size, PlacementLimitsConfig config)
        {
            long slots = size.PaidSpins;

            int maxLosses = config.MaxConsecutiveLossesOverride
                ?? FloorLimit(config.LossRunFactor, size.LosingTickets, size.PrizeTickets + 1, slots);
            int maxRegularGap = config.MaxRegularGapOverride
                ?? FloorLimit(config.RegularGapFactor, slots, size.RegularTickets + 1, slots);
            int maxBigGap = config.MaxBigGapOverride
                ?? FloorLimit(config.BigGapFactor, slots, size.BigTickets + 1, slots);

            return new PlacementLimits(maxLosses, maxRegularGap, maxBigGap, config.RepeatingIntervalMaxPeriod);
        }

        /// <summary>floor(factor × count ÷ divisor), capped at the deal length (no gap can be longer than the deal).</summary>
        private static int FloorLimit(decimal factor, long count, long divisor, long slots)
        {
            decimal limit = decimal.Floor(factor * count / divisor);
            return (int)Math.Min(limit, slots);
        }

        public override string ToString() =>
            $"max {MaxConsecutiveLosses} consecutive losses, Regular gap ≤ {MaxRegularGap}, Big gap ≤ {MaxBigGap}, " +
            $"no repeating prize interval with period ≤ {RepeatingIntervalMaxPeriod}";
    }
}
