using System.Collections.Generic;
using System.Linq;
using FinitePool.Engine.Core;
using FinitePool.Engine.Deals;
using FinitePool.Engine.Placement;

namespace FinitePool.Tests.Fakes
{
    /// <summary>Makes damaged copies of a deal, to prove the verifier catches each kind of damage.</summary>
    internal static class DealTamper
    {
        public static Deal WithTicketIds(Deal deal, ushort[] ticketTypeIds) => Copy(deal, ticketTypeIds: ticketTypeIds);

        public static Deal WithLimits(Deal deal, PlacementLimits limits) => Copy(deal, limits: limits);

        public static Deal WithTicketTypes(Deal deal, IReadOnlyList<TicketType> ticketTypes) => Copy(deal, ticketTypes: ticketTypes);

        public static ushort[] TicketIds(Deal deal) => deal.TicketTypeIds.ToArray();

        /// <summary>Positions of every ticket in a category, in deal order.</summary>
        public static List<int> PositionsOf(Deal deal, TicketCategory category) =>
            Enumerable.Range(0, deal.TicketCount).Where(i => deal.GetTicket(i).Category == category).ToList();

        private static Deal Copy(Deal deal, ushort[] ticketTypeIds = null, PlacementLimits limits = null, IReadOnlyList<TicketType> ticketTypes = null) =>
            new Deal(
                deal.DealId,
                deal.Mode,
                deal.Bet,
                deal.ConfigVersion,
                deal.CreatedUtc,
                deal.TargetRtp,
                deal.PairedCycles,
                deal.BonusUnitValue,
                limits ?? deal.Limits,
                ticketTypes ?? deal.TicketTypes,
                ticketTypeIds ?? TicketIds(deal));
    }
}
