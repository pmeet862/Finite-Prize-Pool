using System;
using System.Globalization;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;

namespace FinitePool.Engine.Sizing
{
    /// <summary>
    /// Works out the exact size of a deal (brief §4.2) using integer math only.
    ///
    /// For bet b, target RTP num/den and payout per paired cycle C:
    ///   wagers = N × b and payouts = k × C, so exact RTP means  N × b × num = k × C × den
    ///   paid spins for k paired cycles:  N = k × C × den ÷ (b × num)     (must be a whole number)
    ///   smallest k:                      k = (b × num) ÷ gcd(b × num, C × den)
    ///   prize tickets P = k × tickets per paired cycle;  losses L = N − P  (rejected if N &lt; P)
    /// Nothing is ever rounded: if a whole deal is impossible, the result is a rejection.
    ///
    /// Tolerance mode (brief §6) relaxes "exactly" to "within target ± tolerance"; see
    /// <see cref="SmallestWithinTolerance"/>.
    /// </summary>
    public static class DealSizeCalculator
    {
        /// <summary>The smallest deal for this mode and bet: exact, or within the tolerance band in tolerance mode.</summary>
        public static DealSizeResult Calculate(DealConfig config, GameMode mode, BetLevel bet)
        {
            if (!TryGetCycleEconomics(config, mode, bet, out CycleEconomics economics, out DealSizeResult rejection))
            {
                return rejection;
            }

            try
            {
                return config.IsToleranceMode
                    ? SmallestWithinTolerance(config, mode, bet, economics)
                    : BuildForCycles(config, mode, bet, economics, ExactPairedCycles(config, bet, economics));
            }
            catch (OverflowException)
            {
                return Overflow(mode, bet);
            }
        }

        /// <summary>
        /// The deal for an exact number of paired cycles. Rejects with
        /// <see cref="DealSizeRejection.FractionalTickets"/> when that many cycles
        /// can't give a whole number of paid spins (brief §8.1 scenario 10).
        /// </summary>
        public static DealSizeResult CalculateForCycles(DealConfig config, GameMode mode, BetLevel bet, long pairedCycles)
        {
            if (pairedCycles < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pairedCycles), pairedCycles, "A deal needs at least one paired cycle.");
            }

            if (!TryGetCycleEconomics(config, mode, bet, out CycleEconomics economics, out DealSizeResult rejection))
            {
                return rejection;
            }

            try
            {
                return BuildForCycles(config, mode, bet, economics, pairedCycles);
            }
            catch (OverflowException)
            {
                return Overflow(mode, bet);
            }
        }

        /// <summary>The smallest k for an exact deal: k = (b × num) ÷ gcd(b × num, C × den).</summary>
        private static long ExactPairedCycles(DealConfig config, BetLevel bet, CycleEconomics economics)
        {
            long spinValue = checked(bet.Amount.Units * config.TargetRtp.Numerator);
            long cycleValue = checked(economics.PayoutPerPairedCycle.Units * config.TargetRtp.Denominator);
            return spinValue / IntMath.Gcd(spinValue, cycleValue);
        }

        /// <summary>
        /// Tolerance mode: the fewest paired cycles k that allow a whole number of paid spins N with
        /// RTP = k·C ÷ (N·b) inside [target − tolerance, target + tolerance], and N ≥ prize tickets.
        /// One paired cycle is tried first. That band means
        ///   k·C ÷ (high × b) ≤ N ≤ k·C ÷ (low × b)
        /// When several N fit, the one whose RTP is closest to the target wins (a tie goes to fewer
        /// spins, A-41). The exact deal is always inside the band, so the search never needs more
        /// cycles than exact mode would.
        /// </summary>
        private static DealSizeResult SmallestWithinTolerance(DealConfig config, GameMode mode, BetLevel bet, CycleEconomics economics)
        {
            Fraction target = config.TargetRtp;
            Fraction low = target - config.RtpTolerance;
            Fraction high = target + config.RtpTolerance;
            long betUnits = bet.Amount.Units;
            long ticketsPerPairedCycle = economics.RegularTicketsPerCycle + economics.BigTicketsPerCycle;
            long exactCycles = ExactPairedCycles(config, bet, economics);

            checked
            {
                for (long pairedCycles = 1; pairedCycles <= exactCycles; pairedCycles++)
                {
                    long payout = pairedCycles * economics.PayoutPerPairedCycle.Units;
                    long fewestSpins = Math.Max(CeilingDivide(payout * high.Denominator, betUnits * high.Numerator), pairedCycles * ticketsPerPairedCycle);
                    long mostSpins = payout * low.Denominator / (betUnits * low.Numerator);
                    if (fewestSpins > mostSpins)
                    {
                        continue;
                    }

                    // The RTP closest to the target comes from the spin count just below or just above the exact one.
                    long spinsBelowExact = payout * target.Denominator / (betUnits * target.Numerator);
                    long below = Math.Min(Math.Max(spinsBelowExact, fewestSpins), mostSpins);
                    long above = Math.Min(Math.Max(spinsBelowExact + 1, fewestSpins), mostSpins);
                    long paidSpins = IsCloserToTarget(above, below, payout, betUnits, target) ? above : below;

                    return CreateSize(config, mode, bet, economics, pairedCycles, paidSpins);
                }
            }

            // Nothing fit, so the exact deal itself was rejected; report why.
            return BuildForCycles(config, mode, bet, economics, exactCycles);
        }

        /// <summary>
        /// Whether payout ÷ (spinsA × bet) is strictly closer to the target than payout ÷ (spinsB × bet).
        /// Distance = |payout·den − num·N·bet| ÷ (N·bet·den); the common bet·den cancels when comparing.
        /// </summary>
        private static bool IsCloserToTarget(long spinsA, long spinsB, long payout, long betUnits, Fraction target)
        {
            checked
            {
                long gapA = Math.Abs(payout * target.Denominator - target.Numerator * spinsA * betUnits);
                long gapB = Math.Abs(payout * target.Denominator - target.Numerator * spinsB * betUnits);
                return gapA * spinsB < gapB * spinsA;
            }
        }

        private static long CeilingDivide(long numerator, long divisor) => (numerator + divisor - 1) / divisor;

        private static DealSizeResult BuildForCycles(DealConfig config, GameMode mode, BetLevel bet, CycleEconomics economics, long pairedCycles)
        {
            checked
            {
                long spinsNumerator = pairedCycles * economics.PayoutPerPairedCycle.Units * config.TargetRtp.Denominator;
                long spinsDivisor = bet.Amount.Units * config.TargetRtp.Numerator;

                if (spinsNumerator % spinsDivisor != 0)
                {
                    decimal exactSpins = (decimal)spinsNumerator / spinsDivisor;
                    return DealSizeResult.Rejected(mode, bet, DealSizeRejection.FractionalTickets,
                        $"{pairedCycles} paired cycle(s) would need {exactSpins.ToString("0.####", CultureInfo.InvariantCulture)} paid spins, " +
                        "which is not a whole number. Deals are never rounded to fit.");
                }

                return CreateSize(config, mode, bet, economics, pairedCycles, spinsNumerator / spinsDivisor);
            }
        }

        private static DealSizeResult CreateSize(DealConfig config, GameMode mode, BetLevel bet, CycleEconomics economics, long pairedCycles, long paidSpins)
        {
            checked
            {
                long prizeTickets = pairedCycles * (economics.RegularTicketsPerCycle + economics.BigTicketsPerCycle);

                if (paidSpins < prizeTickets)
                {
                    return DealSizeResult.Rejected(mode, bet, DealSizeRejection.PaidSpinsFewerThanPrizeTickets,
                        $"{paidSpins} paid spins is fewer than the {prizeTickets} prize tickets.");
                }

                if (paidSpins > config.MaxDealTickets)
                {
                    return DealSizeResult.Rejected(mode, bet, DealSizeRejection.ExceedsMaxDealTickets,
                        $"{paidSpins} tickets exceeds the configured maximum of {config.MaxDealTickets}.");
                }

                var size = new DealSize(
                    mode,
                    bet,
                    config.TargetRtp,
                    config.RtpTolerance,
                    pairedCycles,
                    economics.RegularTicketsPerCycle,
                    economics.BigTicketsPerCycle,
                    economics.MultiplierTotal,
                    economics.BonusUnits,
                    economics.PayoutPerPairedCycle,
                    paidSpins,
                    bet.Amount * paidSpins,
                    economics.PayoutPerPairedCycle * pairedCycles);

                // Holds by construction; checked anyway so a future formula change can't slip through.
                if (!size.IsOnTarget)
                {
                    throw new InvalidOperationException($"Internal error: {size} is not on target {config.TargetRtp} (tolerance {config.RtpTolerance}).");
                }

                return DealSizeResult.Accepted(size);
            }
        }

        /// <summary>Totals for one Regular cycle plus one Big cycle at this bet.</summary>
        private static bool TryGetCycleEconomics(DealConfig config, GameMode mode, BetLevel bet, out CycleEconomics economics, out DealSizeResult rejection)
        {
            economics = default;
            rejection = null;

            if (!config.Modes.TryGetValue(mode, out ModeConfig modeConfig))
            {
                rejection = DealSizeResult.Rejected(mode, bet, DealSizeRejection.UnknownMode, $"Config '{config.ConfigVersion}' has no mode '{mode}'.");
                return false;
            }

            if (!config.HasBetLevel(bet))
            {
                rejection = DealSizeResult.Rejected(mode, bet, DealSizeRejection.UnknownBetLevel, $"Config '{config.ConfigVersion}' has no bet level {bet}.");
                return false;
            }

            Money payout = Money.Zero;
            try
            {
                foreach (BagDefinition bag in new[] { modeConfig.RegularBag, modeConfig.BigBag })
                {
                    foreach (TicketEntry entry in bag.Entries)
                    {
                        if (!TicketAward.TryCompute(entry, bet, config.BonusUnitValue, out Money award))
                        {
                            rejection = DealSizeResult.Rejected(mode, bet, DealSizeRejection.FractionalCentAward,
                                $"{bag.Category} ticket {entry.Label} at {bet} is not a whole number of cents. Awards are never rounded.");
                            return false;
                        }

                        payout += award * entry.Count;
                    }
                }
            }
            catch (OverflowException)
            {
                rejection = Overflow(mode, bet);
                return false;
            }

            if (payout <= Money.Zero)
            {
                rejection = DealSizeResult.Rejected(mode, bet, DealSizeRejection.ZeroPayout, "One paired cycle pays nothing.");
                return false;
            }

            economics = new CycleEconomics(
                payout,
                modeConfig.RegularBag.MultiplierTotal + modeConfig.BigBag.MultiplierTotal,
                modeConfig.RegularBag.BonusUnitsTotal + modeConfig.BigBag.BonusUnitsTotal,
                modeConfig.RegularBag.TicketsPerCycle,
                modeConfig.BigBag.TicketsPerCycle);
            return true;
        }

        private static DealSizeResult Overflow(GameMode mode, BetLevel bet) =>
            DealSizeResult.Rejected(mode, bet, DealSizeRejection.ArithmeticOverflow, "The deal is too large for 64-bit integer math.");

        private readonly struct CycleEconomics
        {
            public Money PayoutPerPairedCycle { get; }
            public decimal MultiplierTotal { get; }
            public long BonusUnits { get; }
            public int RegularTicketsPerCycle { get; }
            public int BigTicketsPerCycle { get; }

            public CycleEconomics(Money payoutPerPairedCycle, decimal multiplierTotal, long bonusUnits, int regularTicketsPerCycle, int bigTicketsPerCycle)
            {
                PayoutPerPairedCycle = payoutPerPairedCycle;
                MultiplierTotal = multiplierTotal;
                BonusUnits = bonusUnits;
                RegularTicketsPerCycle = regularTicketsPerCycle;
                BigTicketsPerCycle = bigTicketsPerCycle;
            }
        }
    }
}
