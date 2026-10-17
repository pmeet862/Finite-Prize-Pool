using System;

namespace FinitePool.Engine.Building
{
    /// <summary>
    /// Brief §4.3 step 1: shuffle each cycle on its own, then append the cycles
    /// in order. Tickets never mix across cycles, so all of cycle n is used
    /// before anything from cycle n + 1.
    /// </summary>
    public static class CycleSequenceBuilder
    {
        /// <param name="cycleTemplate">The ticket ids of one cycle (any order).</param>
        /// <param name="cycles">How many cycles to append.</param>
        public static ushort[] Build(ushort[] cycleTemplate, long cycles, IRandomSource random)
        {
            if (cycleTemplate == null) throw new ArgumentNullException(nameof(cycleTemplate));
            if (cycles < 0) throw new ArgumentOutOfRangeException(nameof(cycles), cycles, null);

            int cycleLength = cycleTemplate.Length;
            var sequence = new ushort[checked((int)(cycles * cycleLength))];
            var cycle = new ushort[cycleLength];

            for (long cycleIndex = 0; cycleIndex < cycles; cycleIndex++)
            {
                Array.Copy(cycleTemplate, cycle, cycleLength);
                FisherYates.Shuffle(cycle, random);
                Array.Copy(cycle, 0, sequence, cycleIndex * cycleLength, cycleLength);
            }

            return sequence;
        }
    }
}
