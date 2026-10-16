using FinitePool.Engine.Core;

namespace FinitePool.Engine.Sizing
{
    /// <summary>
    /// The exact shape of a deal for one mode/bet (brief §4.2). Only the
    /// calculator creates it, and only when every count is a whole number and
    /// payouts ÷ wagers is exactly the target RTP.
    /// </summary>
    public sealed class DealSize
    {
        public GameMode Mode { get; }
        public BetLevel Bet { get; }
        public Fraction TargetRtp { get; }

        public long PairedCycles { get; }
        public int RegularTicketsPerCycle { get; }
        public int BigTicketsPerCycle { get; }

        /// <summary>Σ multipliers in one Regular cycle plus one Big cycle.</summary>
        public decimal MultiplierTotalPerPairedCycle { get; }

        /// <summary>Σ bonus units in one Regular cycle plus one Big cycle.</summary>
        public long BonusUnitsPerPairedCycle { get; }

        /// <summary>Multiplier total × bet + bonus units × bonus unit value.</summary>
        public Money PayoutPerPairedCycle { get; }

        /// <summary>N: total paid spins = total tickets in the deal (prizes + losses).</summary>
        public long PaidSpins { get; }

        public long RegularTickets { get; }
        public long BigTickets { get; }
        public long PrizeTickets => RegularTickets + BigTickets;
        public long LosingTickets => PaidSpins - PrizeTickets;

        public Money TotalWagers { get; }
        public Money TotalPayouts { get; }

        /// <summary>Total payouts ÷ total wagers as an exact fraction.</summary>
        public Fraction ActualRtp => new Fraction(TotalPayouts.Units, TotalWagers.Units);

        public bool IsExactlyTargetRtp => ActualRtp == TargetRtp;

        internal DealSize(
            GameMode mode,
            BetLevel bet,
            Fraction targetRtp,
            long pairedCycles,
            int regularTicketsPerCycle,
            int bigTicketsPerCycle,
            decimal multiplierTotalPerPairedCycle,
            long bonusUnitsPerPairedCycle,
            Money payoutPerPairedCycle,
            long paidSpins,
            Money totalWagers,
            Money totalPayouts)
        {
            Mode = mode;
            Bet = bet;
            TargetRtp = targetRtp;
            PairedCycles = pairedCycles;
            RegularTicketsPerCycle = regularTicketsPerCycle;
            BigTicketsPerCycle = bigTicketsPerCycle;
            MultiplierTotalPerPairedCycle = multiplierTotalPerPairedCycle;
            BonusUnitsPerPairedCycle = bonusUnitsPerPairedCycle;
            PayoutPerPairedCycle = payoutPerPairedCycle;
            PaidSpins = paidSpins;
            RegularTickets = checked(pairedCycles * regularTicketsPerCycle);
            BigTickets = checked(pairedCycles * bigTicketsPerCycle);
            TotalWagers = totalWagers;
            TotalPayouts = totalPayouts;
        }

        public override string ToString() =>
            $"{Mode} {Bet}: {PairedCycles} paired cycle(s), {PaidSpins} spins " +
            $"({RegularTickets} Regular, {BigTickets} Big, {LosingTickets} Loss), " +
            $"wagers {TotalWagers}, payouts {TotalPayouts}, RTP {ActualRtp.ToPercentString()}";
    }
}
