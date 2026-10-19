using System;
using System.Globalization;

namespace FinitePool.Engine.Core
{
    /// <summary>
    /// An exact rational number, always stored in lowest terms with a positive
    /// denominator. Used for RTP so "exactly 90%" means 9/10, not 0.8999...
    /// </summary>
    public readonly struct Fraction : IEquatable<Fraction>
    {
        public static readonly Fraction Zero = new Fraction(0, 1);

        public long Numerator { get; }
        public long Denominator { get; }

        public Fraction(long numerator, long denominator)
        {
            if (denominator == 0)
            {
                throw new ArgumentException("Denominator cannot be zero.", nameof(denominator));
            }

            if (denominator < 0)
            {
                numerator = checked(-numerator);
                denominator = checked(-denominator);
            }

            long divisor = IntMath.Gcd(numerator, denominator);
            Numerator = numerator / divisor;
            Denominator = denominator / divisor;
        }

        /// <summary>Converts a decimal such as 0.90 to an exact fraction (9/10) without rounding.</summary>
        public static Fraction FromDecimal(decimal value)
        {
            long denominator = 1;
            decimal scaled = value;
            while (scaled != decimal.Truncate(scaled))
            {
                scaled *= 10;
                denominator = checked(denominator * 10);
            }

            return new Fraction(decimal.ToInt64(scaled), denominator);
        }

        /// <summary>Approximate decimal value, for display only. Never use it for exactness checks.</summary>
        public decimal ToDecimal() => (decimal)Numerator / Denominator;

        /// <summary>Display helper, for example "90.000%".</summary>
        public string ToPercentString(int decimals = 3)
        {
            decimal percent = ToDecimal() * 100m;
            return percent.ToString("F" + decimals, CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>True when this value is within ± <paramref name="tolerance"/> of <paramref name="center"/>, exactly.</summary>
        public bool IsWithin(Fraction center, Fraction tolerance)
        {
            // |a/b − c/d| ≤ e/f  ⇔  |a·d − c·b| · f ≤ e · b · d   (denominators are always positive)
            long difference = Math.Abs(checked(Numerator * center.Denominator - center.Numerator * Denominator));
            return checked(difference * tolerance.Denominator) <= checked(tolerance.Numerator * Denominator * center.Denominator);
        }

        public static Fraction operator +(Fraction left, Fraction right) =>
            new Fraction(checked(left.Numerator * right.Denominator + right.Numerator * left.Denominator), checked(left.Denominator * right.Denominator));

        public static Fraction operator -(Fraction left, Fraction right) => left + new Fraction(-right.Numerator, right.Denominator);

        public static bool operator ==(Fraction left, Fraction right) => left.Equals(right);

        public static bool operator !=(Fraction left, Fraction right) => !left.Equals(right);

        // Both sides are in lowest terms, so equal values have identical parts.
        public bool Equals(Fraction other) => Numerator == other.Numerator && Denominator == other.Denominator;

        public override bool Equals(object obj) => obj is Fraction other && Equals(other);

        public override int GetHashCode() => (Numerator, Denominator).GetHashCode();

        public override string ToString() => $"{Numerator}/{Denominator}";
    }
}
