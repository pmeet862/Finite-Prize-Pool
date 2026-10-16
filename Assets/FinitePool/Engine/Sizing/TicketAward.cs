using FinitePool.Engine.Config;
using FinitePool.Engine.Core;

namespace FinitePool.Engine.Sizing
{
    /// <summary>
    /// What one ticket pays at a given bet (brief §2): a multiplier pays
    /// multiplier × bet as the full credit; a bonus pays units × bonus unit value
    /// regardless of bet. Never both. Shared by the calculator and the builder
    /// so they can't disagree.
    /// </summary>
    public static class TicketAward
    {
        /// <summary>
        /// Returns false when the award is not a whole number of money units
        /// (for example x0.5 at $0.25 = 12.5 cents). Such awards are rejected, never rounded.
        /// </summary>
        public static bool TryCompute(TicketEntry entry, BetLevel bet, Money bonusUnitValue, out Money award)
        {
            if (entry.Kind == AwardKind.Bonus)
            {
                award = bonusUnitValue * entry.BonusUnits;
                return true;
            }

            decimal units = entry.Multiplier * bet.Amount.Units;
            if (units != decimal.Truncate(units))
            {
                award = Money.Zero;
                return false;
            }

            award = Money.FromUnits(decimal.ToInt64(units));
            return true;
        }
    }
}
