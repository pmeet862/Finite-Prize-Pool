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
}
