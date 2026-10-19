using System;
using System.Collections.Generic;
using System.Linq;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Infrastructure;

namespace FinitePool.Engine.Service
{
    /// <summary>
    /// Auto-play (brief §6, §7.4): plays complete deals with no animation, through the same
    /// <see cref="DealService"/> the game uses, on a private in-memory engine so no saved
    /// game data is touched.
    /// </summary>
    public static class DealSimulator
    {
        /// <summary>Plays until <paramref name="dealCount"/> deals for this mode and bet have used their last ticket.</summary>
        public static IReadOnlyList<CompletedDealRecord> PlayCompleteDeals(DealConfig config, GameMode mode, BetLevel bet, int dealCount)
        {
            if (dealCount < 1) throw new ArgumentOutOfRangeException(nameof(dealCount), dealCount, "Play at least one deal.");

            DealService service = DealService.OpenInMemory(config, NullEngineLog.Instance);
            var player = new PlayerId("simulator");
            service.RegisterPlayer(player, Money.FromUnits(long.MaxValue / 4)); // effectively unlimited
            service.EnsurePrepared(mode, bet);

            int completed = 0;
            while (completed < dealCount)
            {
                SpinOutcome outcome = service.Spin(player, mode, bet);
                if (!outcome.IsAccepted) throw new InvalidOperationException($"Simulation stopped: {outcome.Message}");
                service.MarkShown(player, outcome.Result.SpinId);

                // The active deal changes exactly when the one just played used its last ticket.
                if (service.GetActiveDealInfo(mode, bet)?.DealId != outcome.Result.DealId)
                {
                    completed++;
                }
            }

            return service.GetCompletedDeals().Take(dealCount).ToList();
        }
    }
}
