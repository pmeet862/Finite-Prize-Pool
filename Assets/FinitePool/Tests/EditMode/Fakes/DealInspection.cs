using System.Collections.Generic;
using System.Linq;
using FinitePool.Engine.Core;
using FinitePool.Engine.Deals;
using FinitePool.Engine.Placement;

namespace FinitePool.Tests.Fakes
{
    /// <summary>
    /// Independent checks on a built deal, written separately from the engine's
    /// own validator so tests don't just ask the engine to grade itself.
    /// </summary>
    internal static class DealInspection
    {
        public static TicketCategory[] Layout(Deal deal) =>
            Enumerable.Range(0, deal.TicketCount).Select(i => deal.GetTicket(i).Category).ToArray();

        public static long Count(Deal deal, TicketCategory category) =>
            Enumerable.Range(0, deal.TicketCount).Count(i => deal.GetTicket(i).Category == category);

        public static Money TotalAwards(Deal deal)
        {
            Money total = Money.Zero;
            for (int i = 0; i < deal.TicketCount; i++)
            {
                total += deal.GetTicket(i).Award;
            }

            return total;
        }

        /// <summary>
        /// Brief §8.1 scenario 2. Takes the category's tickets in deal order and cuts
        /// them into blocks of one cycle. Each block must hold exactly one cycle's
        /// contents. If any ticket of cycle n + 1 came before all of cycle n was used,
        /// some block would hold the wrong mix.
        /// Returns a description of the first bad block, or null when every block is a whole cycle.
        /// </summary>
        public static string FindCycleMixing(Deal deal, TicketCategory category)
        {
            List<ushort> ticketsInOrder = Enumerable.Range(0, deal.TicketCount)
                .Select(i => deal.GetTicket(i))
                .Where(ticket => ticket.Category == category)
                .Select(ticket => ticket.Id)
                .ToList();

            List<ushort> oneCycle = deal.TicketTypes
                .Where(type => type.Category == category)
                .SelectMany(type => Enumerable.Repeat(type.Id, type.CountPerCycle))
                .OrderBy(id => id)
                .ToList();

            if (ticketsInOrder.Count != oneCycle.Count * deal.PairedCycles)
            {
                return $"{category}: {ticketsInOrder.Count} tickets is not {deal.PairedCycles} whole cycles of {oneCycle.Count}.";
            }

            for (int cycle = 0; cycle < deal.PairedCycles; cycle++)
            {
                List<ushort> block = ticketsInOrder.Skip(cycle * oneCycle.Count).Take(oneCycle.Count).OrderBy(id => id).ToList();
                if (!block.SequenceEqual(oneCycle))
                {
                    return $"{category} cycle {cycle} holds [{string.Join(",", block)}], expected [{string.Join(",", oneCycle)}].";
                }
            }

            return null;
        }

        /// <summary>
        /// Recomputes the longest loss run and gaps from slot positions (a different
        /// method from the engine's single-pass counter) and lists any limit broken.
        /// </summary>
        public static List<string> FindLimitBreaks(Deal deal, PlacementLimits limits)
        {
            TicketCategory[] layout = Layout(deal);
            var breaks = new List<string>();

            int longestLossRun = LongestGap(layout, category => category != TicketCategory.Loss);
            int longestRegularGap = LongestGap(layout, category => category == TicketCategory.Regular);
            int longestBigGap = LongestGap(layout, category => category == TicketCategory.Big);

            if (longestLossRun > limits.MaxConsecutiveLosses) breaks.Add($"loss run {longestLossRun} > {limits.MaxConsecutiveLosses}");
            if (longestRegularGap > limits.MaxRegularGap) breaks.Add($"Regular gap {longestRegularGap} > {limits.MaxRegularGap}");
            if (longestBigGap > limits.MaxBigGap) breaks.Add($"Big gap {longestBigGap} > {limits.MaxBigGap}");

            List<int> prizePositions = Enumerable.Range(0, layout.Length).Where(i => layout[i] != TicketCategory.Loss).ToList();
            List<int> spacings = prizePositions.Zip(prizePositions.Skip(1), (a, b) => b - a).ToList();
            if (spacings.Count >= 2 && spacings.Distinct().Count() == 1)
            {
                breaks.Add($"prizes evenly spaced every {spacings[0]} slots");
            }

            return breaks;
        }

        /// <summary>Largest number of slots between separators, counting before the first and after the last.</summary>
        private static int LongestGap(TicketCategory[] layout, System.Func<TicketCategory, bool> isSeparator)
        {
            var edges = new List<int> { -1 };
            edges.AddRange(Enumerable.Range(0, layout.Length).Where(i => isSeparator(layout[i])));
            edges.Add(layout.Length);
            return edges.Zip(edges.Skip(1), (a, b) => b - a - 1).Max();
        }
    }
}
