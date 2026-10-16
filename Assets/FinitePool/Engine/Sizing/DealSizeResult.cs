using FinitePool.Engine.Core;

namespace FinitePool.Engine.Sizing
{
    /// <summary>Why a mode/bet cannot form an exact deal. A deal is never rounded to fit.</summary>
    public enum DealSizeRejection
    {
        /// <summary>The requested number of paired cycles gives a fractional number of paid spins.</summary>
        FractionalTickets,

        /// <summary>The deal would need fewer paid spins than it has prize tickets.</summary>
        PaidSpinsFewerThanPrizeTickets,

        /// <summary>A multiplier × bet is not a whole number of cents (A-06).</summary>
        FractionalCentAward,

        /// <summary>The deal is larger than the config's maxDealTickets guard (A-08).</summary>
        ExceedsMaxDealTickets,

        /// <summary>The numbers are too large for 64-bit integer math.</summary>
        ArithmeticOverflow,

        /// <summary>One paired cycle pays nothing, so no RTP is reachable.</summary>
        ZeroPayout,

        UnknownMode,
        UnknownBetLevel
    }

    /// <summary>Either an accepted <see cref="DealSize"/>, or a rejection with a readable reason.</summary>
    public sealed class DealSizeResult
    {
        public GameMode Mode { get; }
        public BetLevel Bet { get; }

        /// <summary>The deal size, or null when rejected.</summary>
        public DealSize Size { get; }

        /// <summary>The reason, or null when accepted.</summary>
        public DealSizeRejection? Rejection { get; }

        public string Message { get; }

        public bool IsAccepted => Size != null;

        private DealSizeResult(GameMode mode, BetLevel bet, DealSize size, DealSizeRejection? rejection, string message)
        {
            Mode = mode;
            Bet = bet;
            Size = size;
            Rejection = rejection;
            Message = message;
        }

        internal static DealSizeResult Accepted(DealSize size) =>
            new DealSizeResult(size.Mode, size.Bet, size, null, size.ToString());

        internal static DealSizeResult Rejected(GameMode mode, BetLevel bet, DealSizeRejection rejection, string message) =>
            new DealSizeResult(mode, bet, null, rejection, $"{mode} {bet} rejected ({rejection}): {message}");

        public override string ToString() => Message;
    }
}
