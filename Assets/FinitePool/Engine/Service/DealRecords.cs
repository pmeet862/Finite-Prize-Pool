using System;
using FinitePool.Engine.Core;
using Newtonsoft.Json;

namespace FinitePool.Engine.Service
{
    /// <summary>Written when a deal's final ticket is used (brief §4.6).</summary>
    public sealed class CompletedDealRecord
    {
        public string DealId { get; set; }
        public GameMode Mode { get; set; }
        public long BetUnits { get; set; }
        public string ConfigVersion { get; set; }
        public int TicketCount { get; set; }
        public long WagersUnits { get; set; }
        public long PayoutsUnits { get; set; }
        public long TargetRtpNumerator { get; set; }
        public long TargetRtpDenominator { get; set; }
        public long PlannedRtpNumerator { get; set; }
        public long PlannedRtpDenominator { get; set; }
        public DateTime CompletedUtc { get; set; }

        [JsonIgnore] public Fraction TargetRtp => new Fraction(TargetRtpNumerator, TargetRtpDenominator);

        /// <summary>The RTP the deal was built to pay: the target in exact mode, inside target ± tolerance otherwise.</summary>
        [JsonIgnore] public Fraction PlannedRtp =>
            PlannedRtpDenominator == 0 ? TargetRtp : new Fraction(PlannedRtpNumerator, PlannedRtpDenominator); // records saved before tolerance mode were exact

        [JsonIgnore] public Fraction ActualRtp => new Fraction(PayoutsUnits, WagersUnits);

        /// <summary>
        /// The deal paid back exactly what it was planned to. The planned RTP was checked against the
        /// target band before the deal was used. Only a completed deal can say this; a retired one never does.
        /// </summary>
        [JsonIgnore] public bool ReachedTargetRtp => ActualRtp == PlannedRtp;
    }

    /// <summary>
    /// Written when a deal is taken out of use before its last ticket (brief §4.7).
    /// Deliberately has no "reached target" field: a retired deal is never reported as having hit its RTP.
    /// The deal file is kept, so the unused tickets stay available for audit.
    /// </summary>
    public sealed class RetireRecord
    {
        public string DealId { get; set; }
        public GameMode Mode { get; set; }
        public long BetUnits { get; set; }
        public string Reason { get; set; }
        public DateTime RetiredUtc { get; set; }
        public string OldConfigVersion { get; set; }
        public string NewConfigVersion { get; set; }

        /// <summary>The deal that took over, or null when the retired deal was never active (e.g. a superseded successor).</summary>
        public string ReplacementDealId { get; set; }

        public int TicketsUsed { get; set; }
        public int TicketsRemaining { get; set; }
        public long WagersUnits { get; set; }
        public long PayoutsUnits { get; set; }

        /// <summary>Payouts ÷ wagers so far, or null when nothing was wagered.</summary>
        [JsonIgnore] public Fraction? ActualRtp => WagersUnits > 0 ? new Fraction(PayoutsUnits, WagersUnits) : (Fraction?)null;
    }

    /// <summary>Written when no deal could be built for a mode and bet (brief §4.4: reject and log).</summary>
    public sealed class BuildFailureRecord
    {
        public GameMode Mode { get; set; }
        public long BetUnits { get; set; }
        public string ConfigVersion { get; set; }
        public string Message { get; set; }
        public DateTime FailedUtc { get; set; }
    }
}
