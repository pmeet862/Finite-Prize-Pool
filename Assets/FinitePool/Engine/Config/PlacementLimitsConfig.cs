namespace FinitePool.Engine.Config
{
    /// <summary>How the category layout is generated. See .docs/ASSUMPTIONS.md A-13.</summary>
    public enum LayoutStrategy
    {
        /// <summary>The brief's literal procedure: shuffle the category list, then check it.</summary>
        ShuffleAndCheck,

        /// <summary>Random shuffle followed by random, bounded repairs. The result is still fully checked.</summary>
        GapRepair
    }

    /// <summary>
    /// Settings for the brief §4.4 placement limits. The default limits are
    /// factor × average (for example 2 × L ÷ (P + 1)). An override replaces the
    /// computed value, so tighter limits can be tested.
    /// </summary>
    public sealed class PlacementLimitsConfig
    {
        public decimal LossRunFactor { get; }
        public decimal RegularGapFactor { get; }
        public decimal BigGapFactor { get; }
        public int? MaxConsecutiveLossesOverride { get; }
        public int? MaxRegularGapOverride { get; }
        public int? MaxBigGapOverride { get; }

        /// <summary>Longest repeating pattern of prize-slot spacing that counts as a "fixed repeating interval" (A-11).</summary>
        public int RepeatingIntervalMaxPeriod { get; }

        public LayoutStrategy LayoutStrategy { get; }

        public PlacementLimitsConfig(
            decimal lossRunFactor,
            decimal regularGapFactor,
            decimal bigGapFactor,
            int? maxConsecutiveLossesOverride,
            int? maxRegularGapOverride,
            int? maxBigGapOverride,
            int repeatingIntervalMaxPeriod,
            LayoutStrategy layoutStrategy)
        {
            LossRunFactor = lossRunFactor;
            RegularGapFactor = regularGapFactor;
            BigGapFactor = bigGapFactor;
            MaxConsecutiveLossesOverride = maxConsecutiveLossesOverride;
            MaxRegularGapOverride = maxRegularGapOverride;
            MaxBigGapOverride = maxBigGapOverride;
            RepeatingIntervalMaxPeriod = repeatingIntervalMaxPeriod;
            LayoutStrategy = layoutStrategy;
        }
    }
}
