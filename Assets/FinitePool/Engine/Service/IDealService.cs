using FinitePool.Engine.Core;

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

        /// <summary>Starts building the first deal for this mode and bet, in the background, if there isn't one yet.</summary>
        void EnsurePrepared(GameMode mode, BetLevel bet);

        DealAvailability GetAvailability(GameMode mode, BetLevel bet);
    }

    /// <summary>Operator actions: players and balances now; retire, config and records in Phase 5.</summary>
    public interface IDealAdmin
    {
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
