using System;
using System.Collections.Generic;

namespace FinitePool.Engine.Config
{
    /// <summary>Thrown when a config cannot be loaded. Lists every problem found, not just the first.</summary>
    public sealed class DealConfigException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public DealConfigException(IReadOnlyList<string> errors)
            : base("Invalid deal config:" + Environment.NewLine + " - " + string.Join(Environment.NewLine + " - ", errors))
        {
            Errors = errors;
        }
    }
}
