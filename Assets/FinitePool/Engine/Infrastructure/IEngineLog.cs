namespace FinitePool.Engine.Infrastructure
{
    /// <summary>
    /// Logging without a Unity dependency. The game forwards it to the Unity
    /// console; tests capture it; a server would send it to its own log.
    /// Implementations must be thread-safe (deals are built off the main thread).
    /// </summary>
    public interface IEngineLog
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }

    /// <summary>Discards everything. For callers that don't care about logs.</summary>
    public sealed class NullEngineLog : IEngineLog
    {
        public static readonly NullEngineLog Instance = new NullEngineLog();

        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
