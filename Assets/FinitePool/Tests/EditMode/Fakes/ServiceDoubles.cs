using System;
using System.Collections.Generic;
using System.IO;
using FinitePool.Engine.Building;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Deals;
using FinitePool.Engine.Infrastructure;
using FinitePool.Engine.Lifecycle;
using FinitePool.Engine.Persistence;
using FinitePool.Engine.Service;
using NUnit.Framework;

namespace FinitePool.Tests.Fakes
{
    /// <summary>Wraps a real store and fails on demand, to simulate a crash or a full disk at a chosen step.</summary>
    internal sealed class FaultInjectingStore : IDealStore
    {
        private readonly IDealStore m_inner;

        public bool FailNextStateWrite { get; set; }
        public bool FailNextSpinAppend { get; set; }

        public FaultInjectingStore(IDealStore inner)
        {
            m_inner = inner;
        }

        public void WriteDeal(Deal deal) => m_inner.WriteDeal(deal);

        public Deal ReadDeal(string dealId) => m_inner.ReadDeal(dealId);

        public EngineState ReadState() => m_inner.ReadState();

        public void WriteState(EngineState state)
        {
            if (FailNextStateWrite)
            {
                FailNextStateWrite = false;
                throw new IOException("Simulated failure writing state.");
            }

            m_inner.WriteState(state);
        }

        public void AppendSpinRecord(SpinRecord record)
        {
            if (FailNextSpinAppend)
            {
                FailNextSpinAppend = false;
                throw new IOException("Simulated crash before the spin log append.");
            }

            m_inner.AppendSpinRecord(record);
        }

        public IReadOnlyList<SpinRecord> ReadSpinRecords() => m_inner.ReadSpinRecords();
    }

    /// <summary>Counts every random number drawn, to prove spins draw none.</summary>
    internal sealed class CountingRandomSource : IRandomSource
    {
        private readonly IRandomSource m_inner = new CryptoRandomSource();

        public int Draws { get; private set; }

        public int NextInt(int maxExclusive)
        {
            Draws++;
            return m_inner.NextInt(maxExclusive);
        }
    }

    /// <summary>Holds background builds until the test runs them, to observe "still building" moments.</summary>
    internal sealed class ManualScheduler : IBackgroundScheduler
    {
        private readonly Queue<Action> m_work = new Queue<Action>();

        public int Pending => m_work.Count;

        public void Run(Action work) => m_work.Enqueue(work);

        public void RunNext() => m_work.Dequeue()();
    }

    /// <summary>A fresh folder under the system temp path, deleted when disposed.</summary>
    internal sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FinitePoolTests", Guid.NewGuid().ToString("N"));

        public TempFolder()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    /// <summary>Builds a service the way tests need it: synchronous builds, a fixed clock, two players.</summary>
    internal static class Services
    {
        public static readonly PlayerId Alice = new PlayerId("alice");
        public static readonly PlayerId Bob = new PlayerId("bob");
        public static readonly Money StartingBalance = Money.FromCents(10000);

        public static DealService Open(IDealStore store, DealConfig config = null, IRandomSource random = null, IEngineLog log = null,
            IClock clock = null, IBackgroundScheduler scheduler = null) =>
            new DealService(config ?? TestConfigs.Placeholder(), store, random ?? new CryptoRandomSource(), clock ?? new FakeClock(),
                log ?? NullEngineLog.Instance, scheduler ?? new ImmediateScheduler());

        /// <summary>A service with Alice and Bob registered ($100 each) and an active deal for the given mode and bet.</summary>
        public static DealService ReadyToPlay(IDealStore store, GameMode mode, long betCents, IRandomSource random = null, IClock clock = null)
        {
            DealService service = Open(store, random: random, clock: clock);
            service.RegisterPlayer(Alice, StartingBalance);
            service.RegisterPlayer(Bob, StartingBalance);
            service.EnsurePrepared(mode, TestConfigs.Bet(betCents));
            Assert.That(service.GetAvailability(mode, TestConfigs.Bet(betCents)).Status, Is.EqualTo(AvailabilityStatus.Available));
            return service;
        }

        /// <summary>The Ready successor waiting behind the active deal, read from saved state.</summary>
        public static DealEntry ReadyDeal(IDealStore store, GameMode mode, long betCents) =>
            store.ReadState().FindDeal(mode, TestConfigs.Bet(betCents), DealState.Ready);

        /// <summary>Spins and marks the result shown, as the client does after its animation.</summary>
        public static SpinResult SpinAndShow(DealService service, PlayerId player, GameMode mode, long betCents)
        {
            SpinOutcome outcome = service.Spin(player, mode, TestConfigs.Bet(betCents));
            Assert.That(outcome.IsAccepted, Is.True, outcome.Message);
            Assert.That(service.MarkShown(player, outcome.Result.SpinId), Is.True);
            return outcome.Result;
        }
    }
}
