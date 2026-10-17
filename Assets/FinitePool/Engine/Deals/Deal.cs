using System;
using System.Collections.Generic;
using FinitePool.Engine.Core;

namespace FinitePool.Engine.Deals
{
    /// <summary>
    /// A complete, ordered deal: the fixed list of tickets that spins use one by
    /// one. Immutable once built, and never reshuffled.
    ///
    /// Internal on purpose: only the engine (and its tests) can see tickets.
    /// The game, debug panel and editor tools cannot compile code that reads them
    /// (brief §3.1, §5: the UI must never read the ticket list).
    /// </summary>
    internal sealed class Deal
    {
        private readonly ushort[] m_ticketTypeIds;

        public string DealId { get; }
        public GameMode Mode { get; }
        public BetLevel Bet { get; }
        public string ConfigVersion { get; }
        public DateTime CreatedUtc { get; }
        public Fraction TargetRtp { get; }
        public long PairedCycles { get; }
        public IReadOnlyList<TicketType> TicketTypes { get; }

        public Deal(
            string dealId,
            GameMode mode,
            BetLevel bet,
            string configVersion,
            DateTime createdUtc,
            Fraction targetRtp,
            long pairedCycles,
            IReadOnlyList<TicketType> ticketTypes,
            ushort[] ticketTypeIds)
        {
            DealId = dealId ?? throw new ArgumentNullException(nameof(dealId));
            Mode = mode;
            Bet = bet;
            ConfigVersion = configVersion;
            CreatedUtc = createdUtc;
            TargetRtp = targetRtp;
            PairedCycles = pairedCycles;
            TicketTypes = ticketTypes ?? throw new ArgumentNullException(nameof(ticketTypes));
            m_ticketTypeIds = ticketTypeIds ?? throw new ArgumentNullException(nameof(ticketTypeIds));
        }

        /// <summary>Total tickets = total paid spins.</summary>
        public int TicketCount => m_ticketTypeIds.Length;

        /// <summary>The ticket at a position (0-based).</summary>
        public TicketType GetTicket(int position) => TicketTypes[m_ticketTypeIds[position]];

        /// <summary>The raw ticket-type ids in deal order, read-only. Used for saving and verifying.</summary>
        public IReadOnlyList<ushort> TicketTypeIds => Array.AsReadOnly(m_ticketTypeIds);

        /// <summary>Tickets in one cycle of a category's bag.</summary>
        public int TicketsPerCycle(TicketCategory category)
        {
            int count = 0;
            foreach (TicketType type in TicketTypes)
            {
                if (type.Category == category)
                {
                    count += type.CountPerCycle;
                }
            }

            return count;
        }

        public static string NewId(GameMode mode, BetLevel bet) =>
            $"{mode.ToString().ToLowerInvariant()}-{bet.Amount.Units:D4}-{Guid.NewGuid():N}".Substring(0, 28);

        public override string ToString() => $"Deal {DealId} ({Mode} {Bet}, {TicketCount} tickets, config {ConfigVersion})";
    }
}
