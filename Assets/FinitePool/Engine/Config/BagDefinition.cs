using System;
using System.Collections.Generic;
using System.Linq;
using FinitePool.Engine.Core;

namespace FinitePool.Engine.Config
{
    /// <summary>
    /// The contents of ONE cycle of a Regular or Big bag (assumption A-02).
    /// A deal uses whole cycles only; each cycle is shuffled on its own.
    /// Multiplier and bonus tickets may be mixed in the same cycle.
    /// </summary>
    public sealed class BagDefinition
    {
        public TicketCategory Category { get; }
        public IReadOnlyList<TicketEntry> Entries { get; }

        public BagDefinition(TicketCategory category, IReadOnlyList<TicketEntry> entries)
        {
            Category = category;
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        /// <summary>Number of tickets in one cycle.</summary>
        public int TicketsPerCycle => Entries.Sum(entry => entry.Count);

        /// <summary>Sum of all multipliers in one cycle (each multiplier counted once per ticket).</summary>
        public decimal MultiplierTotal => Entries
            .Where(entry => entry.Kind == AwardKind.Multiplier)
            .Sum(entry => entry.Multiplier * entry.Count);

        /// <summary>Sum of all bonus units in one cycle.</summary>
        public long BonusUnitsTotal => Entries
            .Where(entry => entry.Kind == AwardKind.Bonus)
            .Sum(entry => (long)entry.BonusUnits * entry.Count);
    }
}
