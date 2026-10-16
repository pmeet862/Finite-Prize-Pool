using System;
using FinitePool.Engine.Core;

namespace FinitePool.Engine.Config
{
    /// <summary>The Regular and Big bags for one game mode. One of each makes a "paired cycle".</summary>
    public sealed class ModeConfig
    {
        public GameMode Mode { get; }
        public BagDefinition RegularBag { get; }
        public BagDefinition BigBag { get; }

        public ModeConfig(GameMode mode, BagDefinition regularBag, BagDefinition bigBag)
        {
            Mode = mode;
            RegularBag = regularBag ?? throw new ArgumentNullException(nameof(regularBag));
            BigBag = bigBag ?? throw new ArgumentNullException(nameof(bigBag));
        }

        public int TicketsPerPairedCycle => RegularBag.TicketsPerCycle + BigBag.TicketsPerCycle;

        public BagDefinition GetBag(TicketCategory category)
        {
            switch (category)
            {
                case TicketCategory.Regular: return RegularBag;
                case TicketCategory.Big: return BigBag;
                default: throw new ArgumentOutOfRangeException(nameof(category), category, "Only Regular and Big have bags.");
            }
        }
    }
}
