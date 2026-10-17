using System;
using System.Collections.Generic;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Sizing;

namespace FinitePool.Engine.Deals
{
    /// <summary>
    /// One kind of ticket in a deal: a bag entry at a fixed bet, or the Loss ticket.
    /// A deal stores each ticket as a small id into its table of types instead of
    /// a full object, which keeps very large deals compact.
    /// </summary>
    internal sealed class TicketType
    {
        public ushort Id { get; }
        public TicketCategory Category { get; }
        public AwardKind Kind { get; }
        public decimal Multiplier { get; }
        public int BonusUnits { get; }

        /// <summary>What this ticket pays at the deal's bet. Zero for Loss.</summary>
        public Money Award { get; }

        /// <summary>How many of this ticket one cycle of its bag holds. Zero for Loss.</summary>
        public int CountPerCycle { get; }

        public TicketType(ushort id, TicketCategory category, AwardKind kind, decimal multiplier, int bonusUnits, Money award, int countPerCycle)
        {
            Id = id;
            Category = category;
            Kind = kind;
            Multiplier = multiplier;
            BonusUnits = bonusUnits;
            Award = award;
            CountPerCycle = countPerCycle;
        }

        public override string ToString() => $"#{Id} {Category} {Kind} {Award}";
    }

    /// <summary>
    /// Builds the ticket-type table for a mode and bet. Id 0 is always Loss;
    /// then one id per Regular bag entry, then one per Big bag entry.
    /// </summary>
    internal static class TicketTypeTable
    {
        public const ushort LOSS_ID = 0;

        public static IReadOnlyList<TicketType> Create(ModeConfig mode, BetLevel bet, Money bonusUnitValue)
        {
            var types = new List<TicketType>
            {
                new TicketType(LOSS_ID, TicketCategory.Loss, AwardKind.None, 0m, 0, Money.Zero, 0)
            };

            foreach (BagDefinition bag in new[] { mode.RegularBag, mode.BigBag })
            {
                foreach (TicketEntry entry in bag.Entries)
                {
                    if (types.Count > ushort.MaxValue)
                    {
                        throw new InvalidOperationException($"Too many ticket types for mode {mode.Mode}.");
                    }

                    if (!TicketAward.TryCompute(entry, bet, bonusUnitValue, out Money award))
                    {
                        // The calculator rejects these before a build starts; reaching here is a bug.
                        throw new InvalidOperationException($"{entry.Label} at {bet} is not a whole number of cents.");
                    }

                    types.Add(new TicketType((ushort)types.Count, bag.Category, entry.Kind, entry.Multiplier, entry.BonusUnits, award, entry.Count));
                }
            }

            return types;
        }

        /// <summary>The ids making up one cycle of a category's bag, each repeated by its count (unshuffled).</summary>
        public static ushort[] CycleTemplate(IReadOnlyList<TicketType> types, TicketCategory category)
        {
            var template = new List<ushort>();
            foreach (TicketType type in types)
            {
                if (type.Category != category)
                {
                    continue;
                }

                for (int i = 0; i < type.CountPerCycle; i++)
                {
                    template.Add(type.Id);
                }
            }

            return template.ToArray();
        }
    }
}
