namespace FinitePool.Engine.Core
{
    public enum GameMode
    {
        Inner,
        Outer
    }

    /// <summary>
    /// The slot type a ticket fills in a deal. Backed by a byte so a
    /// multi-million-slot category layout stays small in memory.
    /// </summary>
    public enum TicketCategory : byte
    {
        Loss,
        Regular,
        Big
    }

    /// <summary>
    /// How a ticket pays. A multiplier pays multiplier × bet as the full credit
    /// (no stake returned on top). A bonus pays a fixed number of bonus units.
    /// A ticket never pays both.
    /// </summary>
    public enum AwardKind
    {
        None,
        Multiplier,
        Bonus
    }

    /// <summary>
    /// Where a deal is in its life (brief §4.6). Ready → Active → Completed,
    /// or Retired when taken out of use early (or replaced before it was used).
    /// </summary>
    public enum DealState
    {
        Ready,
        Active,
        Completed,
        Retired
    }
}
