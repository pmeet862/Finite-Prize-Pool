using FinitePool.Engine.Sizing;

namespace FinitePool.Engine.Placement
{
    /// <summary>
    /// Rejects limits that no layout could ever meet, before any generation
    /// attempt is spent (brief §4.4: "reject limits that are mathematically impossible").
    /// Passing this check doesn't promise success; the 1,000-attempt cap still applies.
    /// </summary>
    public static class PlacementFeasibility
    {
        /// <summary>Null when the limits are possible, otherwise the reason they are not.</summary>
        public static string FindImpossibility(DealSize size, PlacementLimits limits)
        {
            long slots = size.PaidSpins;
            long prizes = size.PrizeTickets;
            long losses = size.LosingTickets;

            // L losses must fit into the P + 1 runs around the prize slots.
            if (losses > (prizes + 1) * limits.MaxConsecutiveLosses)
            {
                return $"{losses} losses cannot fit into {prizes + 1} runs of at most {limits.MaxConsecutiveLosses} " +
                       $"(capacity {(prizes + 1) * limits.MaxConsecutiveLosses}).";
            }

            // The N − R non-Regular slots must fit into the R + 1 gaps around Regular slots. Same for Big.
            if (slots - size.RegularTickets > (size.RegularTickets + 1) * limits.MaxRegularGap)
            {
                return $"{slots - size.RegularTickets} non-Regular slots cannot fit into {size.RegularTickets + 1} gaps " +
                       $"of at most {limits.MaxRegularGap}.";
            }

            if (slots - size.BigTickets > (size.BigTickets + 1) * limits.MaxBigGap)
            {
                return $"{slots - size.BigTickets} non-Big slots cannot fit into {size.BigTickets + 1} gaps " +
                       $"of at most {limits.MaxBigGap}.";
            }

            // If the loss runs are completely full, every run has exactly the maximum length,
            // so prize slots land at a fixed spacing. That breaks the no-fixed-interval rule.
            // (L = 0 is the special case where every slot is a prize.)
            bool hasEnoughPrizesToShowAPattern = prizes >= RepeatingIntervalDetector.MIN_POSITIONS_FOR_PATTERN;
            if (hasEnoughPrizesToShowAPattern && losses == (prizes + 1) * limits.MaxConsecutiveLosses)
            {
                return $"Every loss run would have to be exactly {limits.MaxConsecutiveLosses} long, " +
                       $"which forces prize slots into a fixed repeating interval of {limits.MaxConsecutiveLosses + 1}.";
            }

            return null;
        }
    }
}
