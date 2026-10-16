using FinitePool.Engine.Core;

namespace FinitePool.Tests
{
    /// <summary>
    /// Expected deal sizes, worked out independently of the engine (python,
    /// using N = k·C·den ÷ (b·num), k = b·num ÷ gcd(b·num, C·den)).
    ///
    /// PLACEHOLDER: these match the placeholder bags in deal-config.json.
    /// When the math PDF arrives, this file and deal-config.json are the two
    /// files to update (see .docs/IMPLEMENTATION_PLAN.md §8).
    /// </summary>
    internal static class GoldenDealSizes
    {
        public const bool IS_PLACEHOLDER = true;

        public static readonly GoldenDealSize Inner100 = new GoldenDealSize(GameMode.Inner, 100, 1, 60, 8, 6000, 5400);
        public static readonly GoldenDealSize Outer100 = new GoldenDealSize(GameMode.Outer, 100, 9, 1360, 117, 136000, 122400);
        public static readonly GoldenDealSize Outer175 = new GoldenDealSize(GameMode.Outer, 175, 63, 8140, 819, 1424500, 1282050);

        /// <summary>Paid spins Outer $1.75 would need with only one paired cycle: not a whole number.</summary>
        public const string OUTER_175_ONE_CYCLE_SPINS = "129.2";

        /// <summary>Brief §8.1 scenario 3: Inner $1.00 must be impossible with this max-consecutive-losses limit.</summary>
        public const int INNER_100_IMPOSSIBLE_MAX_LOSS_RUN = 5;

        /// <summary>All 40 combinations: mode, bet in cents, paired cycles, paid spins, prize tickets.</summary>
        public static readonly object[][] AllCombinations =
        {
            new object[] { GameMode.Inner, 25, 3L, 430L, 24L },
            new object[] { GameMode.Inner, 50, 9L, 790L, 72L },
            new object[] { GameMode.Inner, 75, 27L, 1870L, 216L },
            new object[] { GameMode.Inner, 100, 1L, 60L, 8L },
            new object[] { GameMode.Inner, 125, 9L, 490L, 72L },
            new object[] { GameMode.Inner, 150, 27L, 1370L, 216L },
            new object[] { GameMode.Inner, 175, 21L, 1010L, 168L },
            new object[] { GameMode.Inner, 200, 9L, 415L, 72L },
            new object[] { GameMode.Inner, 225, 81L, 3610L, 648L },
            new object[] { GameMode.Inner, 250, 3L, 130L, 24L },
            new object[] { GameMode.Inner, 275, 99L, 4190L, 792L },
            new object[] { GameMode.Inner, 300, 27L, 1120L, 216L },
            new object[] { GameMode.Inner, 325, 13L, 530L, 104L },
            new object[] { GameMode.Inner, 350, 63L, 2530L, 504L },
            new object[] { GameMode.Inner, 375, 27L, 1070L, 216L },
            new object[] { GameMode.Inner, 400, 6L, 235L, 48L },
            new object[] { GameMode.Inner, 425, 153L, 5930L, 1224L },
            new object[] { GameMode.Inner, 450, 81L, 3110L, 648L },
            new object[] { GameMode.Inner, 475, 57L, 2170L, 456L },
            new object[] { GameMode.Inner, 500, 9L, 340L, 72L },
            new object[] { GameMode.Outer, 25, 9L, 2740L, 117L },
            new object[] { GameMode.Outer, 50, 9L, 1820L, 117L },
            new object[] { GameMode.Outer, 75, 27L, 4540L, 351L },
            new object[] { GameMode.Outer, 100, 9L, 1360L, 117L },
            new object[] { GameMode.Outer, 125, 9L, 1268L, 117L },
            new object[] { GameMode.Outer, 150, 27L, 3620L, 351L },
            new object[] { GameMode.Outer, 175, 63L, 8140L, 819L },
            new object[] { GameMode.Outer, 200, 9L, 1130L, 117L },
            new object[] { GameMode.Outer, 225, 81L, 9940L, 1053L },
            new object[] { GameMode.Outer, 250, 9L, 1084L, 117L },
            new object[] { GameMode.Outer, 275, 99L, 11740L, 1287L },
            new object[] { GameMode.Outer, 300, 27L, 3160L, 351L },
            new object[] { GameMode.Outer, 325, 117L, 13540L, 1521L },
            new object[] { GameMode.Outer, 350, 63L, 7220L, 819L },
            new object[] { GameMode.Outer, 375, 27L, 3068L, 351L },
            new object[] { GameMode.Outer, 400, 9L, 1015L, 117L },
            new object[] { GameMode.Outer, 425, 153L, 17140L, 1989L },
            new object[] { GameMode.Outer, 450, 81L, 9020L, 1053L },
            new object[] { GameMode.Outer, 475, 171L, 18940L, 2223L },
            new object[] { GameMode.Outer, 500, 9L, 992L, 117L },
        };
    }

    internal sealed class GoldenDealSize
    {
        public GameMode Mode { get; }
        public long BetCents { get; }
        public long PairedCycles { get; }
        public long PaidSpins { get; }
        public long PrizeTickets { get; }
        public long LosingTickets => PaidSpins - PrizeTickets;
        public long WagersCents { get; }
        public long PayoutsCents { get; }

        public GoldenDealSize(GameMode mode, long betCents, long pairedCycles, long paidSpins, long prizeTickets, long wagersCents, long payoutsCents)
        {
            Mode = mode;
            BetCents = betCents;
            PairedCycles = pairedCycles;
            PaidSpins = paidSpins;
            PrizeTickets = prizeTickets;
            WagersCents = wagersCents;
            PayoutsCents = payoutsCents;
        }

        public override string ToString() => $"{Mode} {BetCents}c";
    }
}
