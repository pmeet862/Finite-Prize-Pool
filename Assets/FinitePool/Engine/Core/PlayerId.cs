using System;

namespace FinitePool.Engine.Core
{
    /// <summary>A player profile on this device. Each profile has its own balance; all share the same deals.</summary>
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public string Value { get; }

        public PlayerId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A player id can't be empty.", nameof(value));
            }

            Value = value;
        }

        public static bool operator ==(PlayerId left, PlayerId right) => left.Equals(right);

        public static bool operator !=(PlayerId left, PlayerId right) => !left.Equals(right);

        public bool Equals(PlayerId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is PlayerId other && Equals(other);

        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value;
    }
}
