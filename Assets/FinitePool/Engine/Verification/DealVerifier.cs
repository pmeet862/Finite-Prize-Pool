using System;
using System.Collections.Generic;
using FinitePool.Engine.Core;
using FinitePool.Engine.Deals;
using FinitePool.Engine.Placement;

namespace FinitePool.Engine.Verification
{
    /// <summary>
    /// Brief §4.3 step 5: before a deal is used, recalculate everything from its
    /// tickets and confirm the RTP is exactly the target.
    ///
    /// It trusts nothing it is told: no stored totals, no config. From the ticket
    /// types it works out what the deal *should* contain, using the same formula
    /// as the calculator, and then counts what the ticket list actually contains.
    /// Checks:
    ///   1. ticket types: well-formed, and each award matches multiplier × bet or units × bonus value
    ///   2. size: the ticket count is the exact deal size for this many paired cycles
    ///   3. contents: each ticket type appears exactly paired cycles × count-per-cycle times
    ///   4. money: wagers = tickets × bet, payouts = Σ awards, payouts ÷ wagers == target RTP exactly
    ///   5. cycle isolation: every block of one cycle holds exactly one cycle's tickets (Regular and Big)
    ///   6. placement: the layout meets the limits the deal was built with
    /// </summary>
    internal static class DealVerifier
    {
        public static VerificationReport Verify(Deal deal)
        {
            var failures = new List<string>();

            if (!CheckTicketTypes(deal, failures))
            {
                // Without a sound type table the counts below would be meaningless.
                return VerificationReport.Failed(deal.DealId, failures);
            }

            try
            {
                CheckSize(deal, failures);
                TicketTotals totals = CountTickets(deal);
                CheckContents(deal, totals, failures);

                Money wagers = deal.Bet.Amount * deal.TicketCount;
                Fraction actualRtp = default;
                if (wagers > Money.Zero)
                {
                    actualRtp = new Fraction(totals.Payouts.Units, wagers.Units);
                    if (actualRtp != deal.TargetRtp)
                    {
                        failures.Add($"RTP is {actualRtp} ({actualRtp.ToPercentString()}), not exactly {deal.TargetRtp}.");
                    }
                }
                else
                {
                    failures.Add("The deal has no tickets.");
                }

                CheckCycleIsolation(deal, TicketCategory.Regular, failures);
                CheckCycleIsolation(deal, TicketCategory.Big, failures);

                PlacementReport placement = PlacementValidator.Validate(CategoryLayout(deal), deal.Limits);
                if (!placement.IsValid)
                {
                    failures.Add("Placement: " + placement);
                }

                return new VerificationReport(deal.DealId, failures, deal.TicketCount, totals.Regular, totals.Big, totals.Losses,
                    wagers, totals.Payouts, actualRtp, placement);
            }
            catch (OverflowException)
            {
                failures.Add("Deal totals overflow 64-bit integer math.");
                return VerificationReport.Failed(deal.DealId, failures);
            }
        }

        private static bool CheckTicketTypes(Deal deal, List<string> failures)
        {
            IReadOnlyList<TicketType> types = deal.TicketTypes;
            int failuresBefore = failures.Count;

            if (types.Count == 0 || types[0].Category != TicketCategory.Loss || types[0].Award != Money.Zero || types[0].CountPerCycle != 0)
            {
                failures.Add("Ticket type 0 must be the Loss ticket, paying nothing.");
            }

            for (int i = 0; i < types.Count; i++)
            {
                TicketType type = types[i];
                if (type.Id != i)
                {
                    failures.Add($"Ticket type at index {i} has id {type.Id}.");
                }

                if (i == 0)
                {
                    continue;
                }

                if (type.Category == TicketCategory.Loss || type.CountPerCycle < 1)
                {
                    failures.Add($"Ticket type {type} must be a Regular or Big prize with at least one per cycle.");
                    continue;
                }

                Money? expectedAward = ExpectedAward(type, deal);
                if (expectedAward == null)
                {
                    failures.Add($"Ticket type {type}: its award is not a whole number of cents at {deal.Bet}.");
                }
                else if (type.Award != expectedAward.Value)
                {
                    failures.Add($"Ticket type {type} pays {type.Award} but its definition gives {expectedAward.Value}.");
                }
            }

            return failures.Count == failuresBefore;
        }

        /// <summary>What the type's definition says it pays at the deal's bet, or null if that isn't whole cents.</summary>
        private static Money? ExpectedAward(TicketType type, Deal deal)
        {
            switch (type.Kind)
            {
                case AwardKind.Bonus:
                    return deal.BonusUnitValue * type.BonusUnits;
                case AwardKind.Multiplier:
                    decimal units = type.Multiplier * deal.Bet.Amount.Units;
                    return units == decimal.Truncate(units) ? Money.FromUnits(decimal.ToInt64(units)) : (Money?)null;
                default:
                    return null;
            }
        }

        /// <summary>N must equal k × C × den ÷ (b × num), where C is what one paired cycle pays.</summary>
        private static void CheckSize(Deal deal, List<string> failures)
        {
            try
            {
                checked
                {
                    long payoutPerPairedCycle = 0;
                    foreach (TicketType type in deal.TicketTypes)
                    {
                        payoutPerPairedCycle += type.Award.Units * type.CountPerCycle;
                    }

                    long spinsNumerator = deal.PairedCycles * payoutPerPairedCycle * deal.TargetRtp.Denominator;
                    long spinsDivisor = deal.Bet.Amount.Units * deal.TargetRtp.Numerator;

                    if (deal.PairedCycles < 1 || spinsDivisor <= 0 || spinsNumerator % spinsDivisor != 0)
                    {
                        failures.Add($"{deal.PairedCycles} paired cycle(s) can't form an exact deal at {deal.Bet}.");
                    }
                    else if (spinsNumerator / spinsDivisor != deal.TicketCount)
                    {
                        failures.Add($"The deal has {deal.TicketCount} tickets; {deal.PairedCycles} paired cycle(s) need exactly {spinsNumerator / spinsDivisor}.");
                    }
                }
            }
            catch (OverflowException)
            {
                failures.Add("Deal size overflows 64-bit integer math.");
            }
        }

        private static TicketTotals CountTickets(Deal deal)
        {
            var totals = new TicketTotals(deal.TicketTypes.Count);
            for (int position = 0; position < deal.TicketCount; position++)
            {
                TicketType ticket = deal.GetTicket(position);
                totals.CountByType[ticket.Id]++;
                totals.Payouts += ticket.Award;
                switch (ticket.Category)
                {
                    case TicketCategory.Regular: totals.Regular++; break;
                    case TicketCategory.Big: totals.Big++; break;
                    default: totals.Losses++; break;
                }
            }

            return totals;
        }

        private static void CheckContents(Deal deal, TicketTotals totals, List<string> failures)
        {
            for (int id = 1; id < deal.TicketTypes.Count; id++)
            {
                TicketType type = deal.TicketTypes[id];
                long expected = deal.PairedCycles * type.CountPerCycle;
                if (totals.CountByType[id] != expected)
                {
                    failures.Add($"Ticket type {type} appears {totals.CountByType[id]} times; expected {expected}.");
                }
            }
        }

        /// <summary>
        /// Cut the category's tickets (in deal order) into blocks of one cycle. Each block
        /// must contain exactly one cycle's worth of each ticket type. If a ticket from
        /// cycle n + 1 appeared before cycle n was finished, some block would be off.
        /// </summary>
        private static void CheckCycleIsolation(Deal deal, TicketCategory category, List<string> failures)
        {
            int cycleLength = deal.TicketsPerCycle(category);
            var countInBlock = new int[deal.TicketTypes.Count];
            int positionInBlock = 0;
            long block = 0;

            for (int position = 0; position < deal.TicketCount; position++)
            {
                TicketType ticket = deal.GetTicket(position);
                if (ticket.Category != category)
                {
                    continue;
                }

                countInBlock[ticket.Id]++;
                positionInBlock++;

                if (positionInBlock == cycleLength)
                {
                    foreach (TicketType type in deal.TicketTypes)
                    {
                        if (type.Category == category && countInBlock[type.Id] != type.CountPerCycle)
                        {
                            failures.Add($"{category} cycle {block} holds {countInBlock[type.Id]} of {type}, expected {type.CountPerCycle}. Cycles are mixed.");
                            return;
                        }
                    }

                    Array.Clear(countInBlock, 0, countInBlock.Length);
                    positionInBlock = 0;
                    block++;
                }
            }

            if (positionInBlock != 0)
            {
                failures.Add($"{category} tickets end part-way through a cycle.");
            }
        }

        private static TicketCategory[] CategoryLayout(Deal deal)
        {
            var layout = new TicketCategory[deal.TicketCount];
            for (int position = 0; position < layout.Length; position++)
            {
                layout[position] = deal.GetTicket(position).Category;
            }

            return layout;
        }

        private sealed class TicketTotals
        {
            public long[] CountByType { get; }
            public long Regular { get; set; }
            public long Big { get; set; }
            public long Losses { get; set; }
            public Money Payouts { get; set; }

            public TicketTotals(int typeCount)
            {
                CountByType = new long[typeCount];
                Payouts = Money.Zero;
            }
        }
    }

    /// <summary>The outcome of verifying one deal: the recomputed totals and any failures.</summary>
    internal sealed class VerificationReport
    {
        public string DealId { get; }
        public IReadOnlyList<string> Failures { get; }
        public bool IsVerified => Failures.Count == 0;

        public long TicketCount { get; }
        public long RegularTickets { get; }
        public long BigTickets { get; }
        public long LossTickets { get; }
        public Money TotalWagers { get; }
        public Money TotalPayouts { get; }

        /// <summary>Recomputed payouts ÷ wagers. Default (0/1) when it couldn't be computed.</summary>
        public Fraction ActualRtp { get; }

        public PlacementReport Placement { get; }

        public VerificationReport(string dealId, IReadOnlyList<string> failures, long ticketCount, long regularTickets, long bigTickets,
            long lossTickets, Money totalWagers, Money totalPayouts, Fraction actualRtp, PlacementReport placement)
        {
            DealId = dealId;
            Failures = failures;
            TicketCount = ticketCount;
            RegularTickets = regularTickets;
            BigTickets = bigTickets;
            LossTickets = lossTickets;
            TotalWagers = totalWagers;
            TotalPayouts = totalPayouts;
            ActualRtp = actualRtp;
            Placement = placement;
        }

        public static VerificationReport Failed(string dealId, IReadOnlyList<string> failures) =>
            new VerificationReport(dealId, failures, 0, 0, 0, 0, Money.Zero, Money.Zero, default, null);

        public override string ToString() => IsVerified
            ? $"Deal {DealId} verified: {TicketCount} tickets, wagers {TotalWagers}, payouts {TotalPayouts}, RTP {ActualRtp.ToPercentString()}."
            : $"Deal {DealId} FAILED verification: " + string.Join(" | ", Failures);
    }
}
