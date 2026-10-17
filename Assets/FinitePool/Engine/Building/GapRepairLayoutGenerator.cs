using System.Collections.Generic;
using FinitePool.Engine.Core;
using FinitePool.Engine.Placement;

namespace FinitePool.Engine.Building
{
    /// <summary>
    /// Default layout strategy (A-13). The brief's plain shuffle almost never meets
    /// the limits for deals with more than a few dozen prizes (each gap breaks its
    /// 2 × average limit about 13.5% of the time, and there are P + 1 gaps), so this
    /// starts from the same shuffle and repairs it:
    ///
    ///   1. Shuffle the R Regular, B Big and L Loss labels (the brief's procedure).
    ///      Keep the order of the prize labels as is.
    ///   2. Read the losses as P + 1 "loss runs": the losses before each prize slot,
    ///      plus the losses after the last one.
    ///   3. Trim: take losses out of any run, Regular gap or Big gap that is too long.
    ///   4. Scatter: put each trimmed loss into a random run that still has room
    ///      under all three limits.
    ///
    /// Why one pass can satisfy all three limits: every loss run lies inside exactly
    /// one Regular gap and exactly one Big gap. A Regular gap's length is its losses
    /// plus the Big slots inside it, so its loss budget is (max Regular gap − Big
    /// slots inside); Big gaps work the same way. Checking a run's own limit and its
    /// two gaps' budgets before adding a loss keeps every limit satisfied.
    ///
    /// The result is random (only the crypto source is used) but not perfectly
    /// uniform over all valid layouts. It is always checked by the validator,
    /// which also enforces the fixed-interval rule; a failed attempt is retried and
    /// counts toward the attempt cap.
    /// </summary>
    public sealed class GapRepairLayoutGenerator : ICategoryLayoutGenerator
    {
        private readonly ShuffleLayoutGenerator m_shuffle = new ShuffleLayoutGenerator();

        public TicketCategory[] TryGenerate(int regular, int big, int losses, PlacementLimits limits, IRandomSource random)
        {
            TicketCategory[] shuffled = m_shuffle.TryGenerate(regular, big, losses, limits, random);

            LossRuns runs = LossRuns.Read(shuffled, regular, big, limits);
            if (runs == null)
            {
                return null;
            }

            int trimmed = runs.TrimToLimits(random);
            if (!runs.Scatter(trimmed, random))
            {
                return null;
            }

            return runs.ToLayout();
        }

        /// <summary>
        /// Loss runs around fixed prize labels. Run i holds the losses just before
        /// prize slot i; the last run holds the losses after the final prize slot.
        /// </summary>
        private sealed class LossRuns
        {
            private readonly TicketCategory[] m_prizeLabels;
            private readonly int[] m_runLength;
            private readonly int m_maxRun;

            // Which Regular gap and which Big gap each run lies in.
            private readonly int[] m_regularGapOfRun;
            private readonly int[] m_bigGapOfRun;

            // Losses currently in each gap, and how many losses each gap may hold.
            private readonly int[] m_regularGapLosses;
            private readonly int[] m_bigGapLosses;
            private readonly int[] m_regularGapBudget;
            private readonly int[] m_bigGapBudget;

            private LossRuns(TicketCategory[] prizeLabels, int[] runLength, int regular, int big, PlacementLimits limits)
            {
                m_prizeLabels = prizeLabels;
                m_runLength = runLength;
                m_maxRun = limits.MaxConsecutiveLosses;
                m_regularGapOfRun = new int[runLength.Length];
                m_bigGapOfRun = new int[runLength.Length];
                m_regularGapLosses = new int[regular + 1];
                m_bigGapLosses = new int[big + 1];
                m_regularGapBudget = Filled(regular + 1, limits.MaxRegularGap);
                m_bigGapBudget = Filled(big + 1, limits.MaxBigGap);

                int regularsSoFar = 0, bigsSoFar = 0;
                for (int run = 0; run < runLength.Length; run++)
                {
                    m_regularGapOfRun[run] = regularsSoFar;
                    m_bigGapOfRun[run] = bigsSoFar;
                    m_regularGapLosses[regularsSoFar] += runLength[run];
                    m_bigGapLosses[bigsSoFar] += runLength[run];

                    if (run == prizeLabels.Length)
                    {
                        break;
                    }

                    // A prize slot sits inside the other category's gap and uses one slot of its budget.
                    if (prizeLabels[run] == TicketCategory.Regular)
                    {
                        m_bigGapBudget[bigsSoFar]--;
                        regularsSoFar++;
                    }
                    else
                    {
                        m_regularGapBudget[regularsSoFar]--;
                        bigsSoFar++;
                    }
                }
            }

            /// <summary>Splits a shuffled layout into prize labels and loss runs. Null if some gap can't hold even zero losses.</summary>
            public static LossRuns Read(TicketCategory[] shuffled, int regular, int big, PlacementLimits limits)
            {
                var prizeLabels = new TicketCategory[regular + big];
                var runLength = new int[regular + big + 1];
                int prizeIndex = 0;

                foreach (TicketCategory category in shuffled)
                {
                    if (category == TicketCategory.Loss)
                    {
                        runLength[prizeIndex]++;
                    }
                    else
                    {
                        prizeLabels[prizeIndex++] = category;
                    }
                }

                var runs = new LossRuns(prizeLabels, runLength, regular, big, limits);
                return runs.AllBudgetsNonNegative() ? runs : null;
            }

            /// <summary>Removes losses until every run and gap is within its limit. Returns how many were removed.</summary>
            public int TrimToLimits(IRandomSource random)
            {
                int trimmed = 0;

                for (int run = 0; run < m_runLength.Length; run++)
                {
                    while (m_runLength[run] > m_maxRun)
                    {
                        RemoveLoss(run);
                        trimmed++;
                    }
                }

                trimmed += TrimGaps(m_regularGapOfRun, m_regularGapLosses, m_regularGapBudget, random);
                trimmed += TrimGaps(m_bigGapOfRun, m_bigGapLosses, m_bigGapBudget, random);
                return trimmed;
            }

            /// <summary>Adds each loss to a random run that still has room. False if room runs out first.</summary>
            public bool Scatter(int lossesToPlace, IRandomSource random)
            {
                var candidates = new List<int>(m_runLength.Length);
                for (int run = 0; run < m_runLength.Length; run++)
                {
                    candidates.Add(run);
                }

                while (lossesToPlace > 0)
                {
                    if (candidates.Count == 0)
                    {
                        return false;
                    }

                    int pick = random.NextInt(candidates.Count);
                    int run = candidates[pick];
                    if (HasRoom(run))
                    {
                        AddLoss(run);
                        lossesToPlace--;
                    }
                    else
                    {
                        // Room only ever shrinks, so a full run never needs another look.
                        candidates[pick] = candidates[candidates.Count - 1];
                        candidates.RemoveAt(candidates.Count - 1);
                    }
                }

                return true;
            }

            public TicketCategory[] ToLayout()
            {
                var layout = new List<TicketCategory>();
                for (int run = 0; run < m_runLength.Length; run++)
                {
                    for (int i = 0; i < m_runLength[run]; i++)
                    {
                        layout.Add(TicketCategory.Loss);
                    }

                    if (run < m_prizeLabels.Length)
                    {
                        layout.Add(m_prizeLabels[run]);
                    }
                }

                return layout.ToArray();
            }

            private int TrimGaps(int[] gapOfRun, int[] gapLosses, int[] gapBudget, IRandomSource random)
            {
                int trimmed = 0;
                var runsWithLosses = new List<int>();

                // Gaps cover consecutive runs, so one forward sweep visits each gap's runs together.
                int firstRun = 0;
                while (firstRun < m_runLength.Length)
                {
                    int gap = gapOfRun[firstRun];
                    int endRun = firstRun;
                    while (endRun < m_runLength.Length && gapOfRun[endRun] == gap)
                    {
                        endRun++;
                    }

                    while (gapLosses[gap] > gapBudget[gap])
                    {
                        runsWithLosses.Clear();
                        for (int run = firstRun; run < endRun; run++)
                        {
                            if (m_runLength[run] > 0) runsWithLosses.Add(run);
                        }

                        RemoveLoss(runsWithLosses[random.NextInt(runsWithLosses.Count)]);
                        trimmed++;
                    }

                    firstRun = endRun;
                }

                return trimmed;
            }

            private bool HasRoom(int run) =>
                m_runLength[run] < m_maxRun
                && m_regularGapLosses[m_regularGapOfRun[run]] < m_regularGapBudget[m_regularGapOfRun[run]]
                && m_bigGapLosses[m_bigGapOfRun[run]] < m_bigGapBudget[m_bigGapOfRun[run]];

            private void AddLoss(int run)
            {
                m_runLength[run]++;
                m_regularGapLosses[m_regularGapOfRun[run]]++;
                m_bigGapLosses[m_bigGapOfRun[run]]++;
            }

            private void RemoveLoss(int run)
            {
                m_runLength[run]--;
                m_regularGapLosses[m_regularGapOfRun[run]]--;
                m_bigGapLosses[m_bigGapOfRun[run]]--;
            }

            private bool AllBudgetsNonNegative()
            {
                foreach (int budget in m_regularGapBudget) if (budget < 0) return false;
                foreach (int budget in m_bigGapBudget) if (budget < 0) return false;
                return true;
            }

            private static int[] Filled(int length, int value)
            {
                var array = new int[length];
                for (int i = 0; i < length; i++) array[i] = value;
                return array;
            }
        }
    }
}
