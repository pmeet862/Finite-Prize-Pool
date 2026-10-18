using System.Collections.Generic;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Persistence;

namespace FinitePool.Engine.Service
{
    /// <summary>
    /// Everything the game client needs, and nothing more (brief §3.1). The client
    /// never sees tickets; it asks for a spin and gets back what happened. A server
    /// version would implement this same interface over the network.
    /// </summary>
    public interface IDealService
    {
        /// <summary>
        /// Plays one spin: check the balance, charge the bet, take the next ticket of the
        /// active deal for this mode and bet, credit the award, and save it all, before
        /// returning. Animate only after this returns.
        /// </summary>
        SpinOutcome Spin(PlayerId player, GameMode mode, BetLevel bet);

        /// <summary>The player's saved result that hasn't been shown yet (e.g. the game closed mid-animation), or null.</summary>
        SpinResult GetPendingResult(PlayerId player);

        /// <summary>Call once the result has been shown. Returns false if that spin isn't the pending one.</summary>
        bool MarkShown(PlayerId player, long spinId);

        Money GetBalance(PlayerId player);

        /// <summary>The bet levels the current config allows, lowest first.</summary>
        IReadOnlyList<BetLevel> BetLevels { get; }

        /// <summary>
        /// Makes sure this mode and bet has an active deal and a verified Ready successor, building
        /// whatever is missing in the background. Also retries a combination that failed to build.
        /// </summary>
        void EnsurePrepared(GameMode mode, BetLevel bet);

        DealAvailability GetAvailability(GameMode mode, BetLevel bet);
    }

    /// <summary>Operator actions: retire, config changes, audit records, players.</summary>
    public interface IDealAdmin
    {
        /// <summary>
        /// Stops using the active deal for this mode and bet, marks it Retired and switches to a
        /// freshly verified replacement (brief §4.7). Throws if there is no active deal, or no
        /// replacement can be built (in which case nothing changes).
        /// </summary>
        RetireRecord Retire(GameMode mode, BetLevel bet, string reason);

        /// <summary>
        /// Uses a new config for every deal built from now on. Active deals keep their own
        /// version; Ready successors built on the old one are retired and rebuilt (brief §4.6).
        /// </summary>
        void ApplyConfig(DealConfig config);

        /// <summary>Every deal the engine knows about, in any state (copies of the saved entries; no tickets).</summary>
        IReadOnlyList<DealEntry> GetDeals();

        IReadOnlyList<CompletedDealRecord> GetCompletedDeals();
        IReadOnlyList<RetireRecord> GetRetiredDeals();
        IReadOnlyList<BuildFailureRecord> GetBuildFailures();

        /// <summary>Adds a player with a starting balance. Returns false (and changes nothing) if the player exists.</summary>
        bool RegisterPlayer(PlayerId player, Money startingBalance);

        /// <summary>Dev top-up or correction. The resulting balance can't go below zero.</summary>
        void AdjustBalance(PlayerId player, Money delta, string reason);
    }

    /// <summary>Read-only facts for the developer debug panel. Contains no ticket data.</summary>
    public interface IDealDiagnostics
    {
        /// <summary>The active deal for this mode and bet, or null when there isn't one.</summary>
        DealInfo GetActiveDealInfo(GameMode mode, BetLevel bet);
    }
}
