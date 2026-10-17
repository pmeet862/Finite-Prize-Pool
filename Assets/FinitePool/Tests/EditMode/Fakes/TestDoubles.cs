using System;
using System.Collections.Generic;
using FinitePool.Engine.Building;
using FinitePool.Engine.Core;
using FinitePool.Engine.Infrastructure;
using FinitePool.Engine.Placement;

namespace FinitePool.Tests.Fakes
{
    /// <summary>Returns scripted values in order, and records every range it was asked for.</summary>
    internal sealed class SequenceRandomSource : IRandomSource
    {
        private readonly Queue<int> m_values;

        public List<int> RequestedRanges { get; } = new List<int>();

        public SequenceRandomSource(params int[] values)
        {
            m_values = new Queue<int>(values);
        }

        public int NextInt(int maxExclusive)
        {
            RequestedRanges.Add(maxExclusive);
            int value = m_values.Count > 0 ? m_values.Dequeue() : 0;
            if (value < 0 || value >= maxExclusive)
            {
                throw new InvalidOperationException($"Scripted value {value} is outside [0, {maxExclusive}).");
            }

            return value;
        }
    }

    internal sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>Keeps every log line so tests can assert that something was logged.</summary>
    internal sealed class CapturingLog : IEngineLog
    {
        private readonly object m_sync = new object();

        public List<string> Infos { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();

        public void Info(string message) { lock (m_sync) Infos.Add(message); }
        public void Warning(string message) { lock (m_sync) Warnings.Add(message); }
        public void Error(string message) { lock (m_sync) Errors.Add(message); }
    }

    /// <summary>Always produces the same invalid layout: all prizes first, then every loss in one run.</summary>
    internal sealed class AlwaysInvalidLayoutGenerator : ICategoryLayoutGenerator
    {
        public int Calls { get; private set; }

        public TicketCategory[] TryGenerate(int regular, int big, int losses, PlacementLimits limits, IRandomSource random)
        {
            Calls++;
            var layout = new TicketCategory[regular + big + losses];
            for (int i = 0; i < layout.Length; i++)
            {
                layout[i] = i < regular ? TicketCategory.Regular : i < regular + big ? TicketCategory.Big : TicketCategory.Loss;
            }

            return layout;
        }
    }

    /// <summary>Layouts written as text for readable tests: R = Regular, B = Big, L = Loss (spaces ignored).</summary>
    internal static class Layouts
    {
        public static TicketCategory[] Parse(string text)
        {
            var layout = new List<TicketCategory>();
            foreach (char symbol in text)
            {
                switch (symbol)
                {
                    case 'R': layout.Add(TicketCategory.Regular); break;
                    case 'B': layout.Add(TicketCategory.Big); break;
                    case 'L': layout.Add(TicketCategory.Loss); break;
                    case ' ': break;
                    default: throw new ArgumentException($"Unknown layout symbol '{symbol}'.");
                }
            }

            return layout.ToArray();
        }
    }
}
