using System;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Placement;

namespace FinitePool.Engine.Building
{
    /// <summary>
    /// Makes ONE attempt at a category layout (the list of Regular/Big/Loss
    /// labels, brief §4.3 step 2). The builder checks every attempt with
    /// <see cref="PlacementValidator"/> and counts it toward the attempt cap,
    /// whatever strategy produced it.
    /// </summary>
    public interface ICategoryLayoutGenerator
    {
        /// <summary>A layout with exactly these counts, or null if this attempt hit a dead end.</summary>
        TicketCategory[] TryGenerate(int regular, int big, int losses, PlacementLimits limits, IRandomSource random);
    }

    public static class CategoryLayoutGenerators
    {
        public static ICategoryLayoutGenerator For(LayoutStrategy strategy)
        {
            switch (strategy)
            {
                case LayoutStrategy.ShuffleAndCheck: return new ShuffleLayoutGenerator();
                case LayoutStrategy.GapRepair: return new GapRepairLayoutGenerator();
                default: throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null);
            }
        }
    }

    /// <summary>
    /// The brief's literal procedure: exactly R Regular, B Big and L Loss labels,
    /// shuffled with Fisher–Yates. It ignores the limits, so only the validator
    /// decides whether the attempt counts. Only practical for small deals (A-13).
    /// </summary>
    public sealed class ShuffleLayoutGenerator : ICategoryLayoutGenerator
    {
        public TicketCategory[] TryGenerate(int regular, int big, int losses, PlacementLimits limits, IRandomSource random)
        {
            var layout = new TicketCategory[regular + big + losses];
            for (int i = 0; i < layout.Length; i++)
            {
                layout[i] = i < regular ? TicketCategory.Regular
                    : i < regular + big ? TicketCategory.Big
                    : TicketCategory.Loss;
            }

            FisherYates.Shuffle(layout, random);
            return layout;
        }
    }
}
