using System;

namespace FinitePool.Engine.Infrastructure
{
    /// <summary>Source of the current time, injected so tests can control timestamps.</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
