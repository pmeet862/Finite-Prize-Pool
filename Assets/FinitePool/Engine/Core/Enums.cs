namespace FinitePool.Engine.Core
{
    public enum GameMode
    {
        Inner,
        Outer
    }

    /// <summary>The slot type a ticket fills in a deal.</summary>
    public enum TicketCategory
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
