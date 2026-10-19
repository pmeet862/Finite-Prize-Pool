using System;
using System.Collections.Generic;
using System.Linq;
using FinitePool.Engine.Core;

namespace FinitePool.Engine.Config
{
    /// <summary>
    /// Everything a deal is built from (brief §4.1). Immutable once created.
    /// Every deal records the <see cref="ConfigVersion"/> it was built with,
    /// so a config change only affects deals built afterwards.
    /// Load it with <see cref="DealConfigJson.Parse"/>, which also validates it.
    /// </summary>
    public sealed class DealConfig
    {
        public string ConfigVersion { get; }

        /// <summary>True while the bag values are placeholders (the math PDF has not been applied yet).</summary>
        public bool IsPlaceholder { get; }

        public string Notes { get; }
        public Fraction TargetRtp { get; }

        /// <summary>
        /// Tolerance mode (brief §6): deals may land anywhere in target ± this value (0.001 = ±0.1 points).
        /// Zero means exact mode, the default: every deal hits the target exactly.
        /// </summary>
        public Fraction RtpTolerance { get; }

        public bool IsToleranceMode => RtpTolerance.Numerator > 0;
        public Money BonusUnitValue { get; }
        public IReadOnlyList<BetLevel> BetLevels { get; }
        public int MaxGenerationAttempts { get; }

        /// <summary>Safety guard: larger deals are rejected with a reason, never truncated (A-08).</summary>
        public long MaxDealTickets { get; }

        public PlacementLimitsConfig PlacementLimits { get; }
        public IReadOnlyDictionary<GameMode, ModeConfig> Modes { get; }

        public DealConfig(
            string configVersion,
            bool isPlaceholder,
            string notes,
            Fraction targetRtp,
            Money bonusUnitValue,
            IReadOnlyList<BetLevel> betLevels,
            int maxGenerationAttempts,
            long maxDealTickets,
            PlacementLimitsConfig placementLimits,
            IReadOnlyDictionary<GameMode, ModeConfig> modes,
            Fraction? rtpTolerance = null)
        {
            ConfigVersion = configVersion;
            IsPlaceholder = isPlaceholder;
            Notes = notes ?? string.Empty;
            TargetRtp = targetRtp;
            RtpTolerance = rtpTolerance ?? Fraction.Zero;
            BonusUnitValue = bonusUnitValue;
            BetLevels = betLevels ?? throw new ArgumentNullException(nameof(betLevels));
            MaxGenerationAttempts = maxGenerationAttempts;
            MaxDealTickets = maxDealTickets;
            PlacementLimits = placementLimits ?? throw new ArgumentNullException(nameof(placementLimits));
            Modes = modes ?? throw new ArgumentNullException(nameof(modes));
        }

        public bool HasBetLevel(BetLevel bet) => BetLevels.Contains(bet);

        public ModeConfig GetMode(GameMode mode)
        {
            if (!Modes.TryGetValue(mode, out ModeConfig modeConfig))
            {
                throw new KeyNotFoundException($"Config '{ConfigVersion}' has no mode '{mode}'.");
            }

            return modeConfig;
        }
    }
}
