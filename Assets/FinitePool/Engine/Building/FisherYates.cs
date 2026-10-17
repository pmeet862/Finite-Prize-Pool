using System;
using System.Collections.Generic;

namespace FinitePool.Engine.Building
{
    /// <summary>The one shuffle used anywhere in the engine (brief §3.2).</summary>
    public static class FisherYates
    {
        /// <summary>
        /// Shuffles in place. Walking from the end, each position swaps with a
        /// uniformly chosen position at or before it, so every ordering is equally likely.
        /// </summary>
        public static void Shuffle<T>(IList<T> items, IRandomSource random)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (random == null) throw new ArgumentNullException(nameof(random));

            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = random.NextInt(i + 1);
                T temp = items[i];
                items[i] = items[j];
                items[j] = temp;
            }
        }
    }
}
