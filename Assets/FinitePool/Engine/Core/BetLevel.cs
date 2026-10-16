using System;

namespace FinitePool.Engine.Core
{
    /// <summary>The stake for one paid spin. Valid levels come from the deal config.</summary>
    public readonly struct BetLevel : IEquatable<BetLevel>, IComparable<BetLevel>
    {
        public Money Amount { get; }

        private BetLevel(Money amount)
        {
            Amount = amount;
        }

        public static BetLevel FromCents(long cents)
        {
            if (cents <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cents), cents, "A bet must be positive.");
            }

            return new BetLevel(Money.FromCents(cents));
        }

        public static bool operator ==(BetLevel left, BetLevel right) => left.Equals(right);

        public static bool operator !=(BetLevel left, BetLevel right) => !left.Equals(right);

        public bool Equals(BetLevel other) => Amount == other.Amount;

        public override bool Equals(object obj) => obj is BetLevel other && Equals(other);

        public override int GetHashCode() => Amount.GetHashCode();

        public int CompareTo(BetLevel other) => Amount.CompareTo(other.Amount);

        public override string ToString() => Amount.ToString();
    }
}
