using System;
using System.Globalization;

namespace FinitePool.Engine.Core
{
    /// <summary>
    /// An exact amount of money held as whole minor units (cents).
    /// Floating point is never used for money anywhere in the engine.
    /// All arithmetic is checked, so an overflow throws instead of wrapping.
    /// </summary>
    public readonly struct Money : IEquatable<Money>, IComparable<Money>
    {
        /// <summary>
        /// Minor units per dollar. This is the single place to change if the
        /// math ever needs sub-cent amounts (it must stay a multiple of 100).
        /// </summary>
        public const long UNITS_PER_DOLLAR = 100;

        private const long CENTS_PER_DOLLAR = 100;

        public static readonly Money Zero = new Money(0);

        public long Units { get; }

        private Money(long units)
        {
            Units = units;
        }

        public static Money FromUnits(long units) => new Money(units);

        public static Money FromCents(long cents) => new Money(checked(cents * (UNITS_PER_DOLLAR / CENTS_PER_DOLLAR)));

        public static Money FromDollars(decimal dollars)
        {
            decimal units = dollars * UNITS_PER_DOLLAR;
            if (units != decimal.Truncate(units))
            {
                throw new ArgumentException($"{dollars} dollars is not a whole number of minor units.", nameof(dollars));
            }

            return new Money(decimal.ToInt64(units));
        }

        public decimal ToDollars() => (decimal)Units / UNITS_PER_DOLLAR;

        public static Money operator +(Money left, Money right) => new Money(checked(left.Units + right.Units));

        public static Money operator -(Money left, Money right) => new Money(checked(left.Units - right.Units));

        public static Money operator *(Money amount, long factor) => new Money(checked(amount.Units * factor));

        public static bool operator ==(Money left, Money right) => left.Units == right.Units;

        public static bool operator !=(Money left, Money right) => left.Units != right.Units;

        public static bool operator <(Money left, Money right) => left.Units < right.Units;

        public static bool operator >(Money left, Money right) => left.Units > right.Units;

        public static bool operator <=(Money left, Money right) => left.Units <= right.Units;

        public static bool operator >=(Money left, Money right) => left.Units >= right.Units;

        public bool Equals(Money other) => Units == other.Units;

        public override bool Equals(object obj) => obj is Money other && Equals(other);

        public override int GetHashCode() => Units.GetHashCode();

        public int CompareTo(Money other) => Units.CompareTo(other.Units);

        /// <summary>Formats as dollars, for example "$1.25" or "-$0.50".</summary>
        public override string ToString()
        {
            string sign = Units < 0 ? "-" : string.Empty;
            decimal dollars = Math.Abs(ToDollars());
            return sign + "$" + dollars.ToString("0.00##", CultureInfo.InvariantCulture);
        }
    }
}
