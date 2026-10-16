using System.Globalization;
using FinitePool.Engine.Core;

namespace FinitePool.Engine.Config
{
    /// <summary>
    /// One line of a bag: <see cref="Count"/> identical tickets that each pay
    /// either a multiplier of the bet or a fixed number of bonus units.
    /// Values are checked by <see cref="DealConfigValidator"/>, not here, so
    /// every problem in a config can be reported at once.
    /// </summary>
    public sealed class TicketEntry
    {
        public AwardKind Kind { get; }

        /// <summary>Multiplier of the bet. Only meaningful when <see cref="Kind"/> is Multiplier.</summary>
        public decimal Multiplier { get; }

        /// <summary>Fixed bonus units (1 unit = config bonus unit value). Only meaningful when <see cref="Kind"/> is Bonus.</summary>
        public int BonusUnits { get; }

        public int Count { get; }

        private TicketEntry(AwardKind kind, decimal multiplier, int bonusUnits, int count)
        {
            Kind = kind;
            Multiplier = multiplier;
            BonusUnits = bonusUnits;
            Count = count;
        }

        public static TicketEntry ForMultiplier(decimal multiplier, int count) =>
            new TicketEntry(AwardKind.Multiplier, multiplier, 0, count);

        public static TicketEntry ForBonus(int bonusUnits, int count) =>
            new TicketEntry(AwardKind.Bonus, 0m, bonusUnits, count);

        /// <summary>Short label such as "x2" or "bonus 5".</summary>
        public string Label => Kind == AwardKind.Multiplier
            ? "x" + Multiplier.ToString("0.####", CultureInfo.InvariantCulture)
            : "bonus " + BonusUnits.ToString(CultureInfo.InvariantCulture);

        public override string ToString() => $"{Label} ×{Count}";
    }
}
