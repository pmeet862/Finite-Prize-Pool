using System.Collections.Generic;

namespace FinitePool.Engine.Placement
{
    /// <summary>
    /// Detects prize slots that follow a fixed repeating interval (brief §4.4, A-11).
    /// Take the spacing between consecutive prize positions. If that spacing
    /// sequence repeats one short pattern from start to end (period 1: 5,5,5,…;
    /// period 2: 3,7,3,7,…), the layout is predictable and is rejected.
    /// A pattern must be visible at least twice to count.
    /// </summary>
    public static class RepeatingIntervalDetector
    {
        /// <summary>Three positions give two spacings, the fewest that can show a repeat.</summary>
        public const int MIN_POSITIONS_FOR_PATTERN = 3;

        /// <summary>The shortest repeating period up to <paramref name="maxPeriod"/>, or 0 when there is none.</summary>
        public static int FindRepeatingPeriod(IReadOnlyList<int> positions, int maxPeriod)
        {
            int spacingCount = positions.Count - 1;

            for (int period = 1; period <= maxPeriod && spacingCount >= 2 * period; period++)
            {
                if (SpacingRepeatsWithPeriod(positions, period))
                {
                    return period;
                }
            }

            return 0;
        }

        private static bool SpacingRepeatsWithPeriod(IReadOnlyList<int> positions, int period)
        {
            for (int i = period; i < positions.Count - 1; i++)
            {
                int spacing = positions[i + 1] - positions[i];
                int spacingOnePeriodEarlier = positions[i + 1 - period] - positions[i - period];
                if (spacing != spacingOnePeriodEarlier)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
