using System;
using System.Threading.Tasks;

namespace FinitePool.Engine.Lifecycle
{
    /// <summary>
    /// Runs deal builds off the caller's thread so the game never freezes
    /// (brief §4.6). Injected so tests can run builds immediately and deterministically.
    /// </summary>
    public interface IBackgroundScheduler
    {
        void Run(Action work);
    }

    /// <summary>Production: runs work on a thread-pool thread.</summary>
    public sealed class ThreadPoolScheduler : IBackgroundScheduler
    {
        public void Run(Action work) => Task.Run(work);
    }

    /// <summary>Tests and the simulator: runs work straight away on the calling thread.</summary>
    public sealed class ImmediateScheduler : IBackgroundScheduler
    {
        public void Run(Action work) => work();
    }
}
