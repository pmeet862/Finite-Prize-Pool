using FinitePool.Engine.Core;

namespace FinitePool.Engine.Service
{
    /// <summary>The outcome of a spin the engine accepted. Saved before it is returned (brief §4.5).</summary>
    public sealed class SpinResult
    {
        public long SpinId { get; }
        public PlayerId Player { get; }
        public GameMode Mode { get; }
        public BetLevel Bet { get; }
        public string DealId { get; }

        /// <summary>0-based position of the ticket used.</summary>
        public int TicketIndex { get; }

        public Money Wager { get; }
        public Money Award { get; }
        public AwardKind AwardKind { get; }
        public TicketCategory Category { get; }
        public Money BalanceAfter { get; }

        public bool IsWin => Award > Money.Zero;

        public SpinResult(long spinId, PlayerId player, GameMode mode, BetLevel bet, string dealId, int ticketIndex,
            Money wager, Money award, AwardKind awardKind, TicketCategory category, Money balanceAfter)
        {
            SpinId = spinId;
            Player = player;
            Mode = mode;
            Bet = bet;
            DealId = dealId;
            TicketIndex = ticketIndex;
            Wager = wager;
            Award = award;
            AwardKind = awardKind;
            Category = category;
            BalanceAfter = balanceAfter;
        }

        public override string ToString() =>
            $"Spin {SpinId}: {Player} {Mode} {Bet} → {Category} {(IsWin ? Award.ToString() : "loss")} (ticket {TicketIndex} of {DealId}), balance {BalanceAfter}";
    }

    public enum SpinRejection
    {
        /// <summary>The balance is lower than the bet. No ticket is used.</summary>
        InsufficientBalance,

        /// <summary>The player's previous result was saved but has not been shown yet. Show it first.</summary>
        PreviousResultNotShown,

        /// <summary>There is no playable deal for this mode and bet (not prepared yet, still building, or unavailable).</summary>
        NoActiveDeal,

        UnknownPlayer,
        UnknownBetLevel,

        /// <summary>The spin could not be saved, so it did not happen. No ticket is used.</summary>
        StorageError
    }

    /// <summary>Either an accepted spin, or the reason it was turned down. A rejected spin changes nothing.</summary>
    public sealed class SpinOutcome
    {
        public SpinResult Result { get; }
        public SpinRejection? Rejection { get; }
        public string Message { get; }
        public bool IsAccepted => Result != null;

        private SpinOutcome(SpinResult result, SpinRejection? rejection, string message)
        {
            Result = result;
            Rejection = rejection;
            Message = message;
        }

        public static SpinOutcome Accepted(SpinResult result) => new SpinOutcome(result, null, result.ToString());

        public static SpinOutcome Rejected(SpinRejection rejection, string message) => new SpinOutcome(null, rejection, message);

        public override string ToString() => Message;
    }

    public enum AvailabilityStatus
    {
        /// <summary>No deal has been requested for this mode and bet yet.</summary>
        NotPrepared,

        /// <summary>The first deal is being built in the background.</summary>
        Preparing,

        Available,

        /// <summary>No deal can be built; see the reason.</summary>
        Unavailable
    }

    public sealed class DealAvailability
    {
        public AvailabilityStatus Status { get; }
        public string Reason { get; }

        public DealAvailability(AvailabilityStatus status, string reason = null)
        {
            Status = status;
            Reason = reason;
        }

        public override string ToString() => Reason == null ? Status.ToString() : $"{Status}: {Reason}";
    }

    /// <summary>
    /// What the debug panel may show about a deal (brief §5). It deliberately has no
    /// ticket data: no upcoming tickets, no remaining prizes, not even the deal length.
    /// </summary>
    public sealed class DealInfo
    {
        public string DealId { get; }
        public DealState State { get; }
        public string ConfigVersion { get; }
        public int SpinsPlayed { get; }

        public DealInfo(string dealId, DealState state, string configVersion, int spinsPlayed)
        {
            DealId = dealId;
            State = state;
            ConfigVersion = configVersion;
            SpinsPlayed = spinsPlayed;
        }

        public override string ToString() => $"{DealId} ({State}, config {ConfigVersion}, {SpinsPlayed} spins played)";
    }
}
