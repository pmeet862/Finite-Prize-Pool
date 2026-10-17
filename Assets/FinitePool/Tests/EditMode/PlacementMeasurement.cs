using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using FinitePool.Engine.Building;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Infrastructure;
using FinitePool.Engine.Placement;
using FinitePool.Engine.Sizing;
using FinitePool.Tests.Fakes;
using NUnit.Framework;
using UnityEngine;

namespace FinitePool.Tests
{
    /// <summary>
    /// Risk R1 measurement (plan §4.4): how often does each layout strategy
    /// produce a valid layout per attempt, and how long does a full build take?
    /// Explicit because it is a report, not a pass/fail check. Writes
    /// .docs/R1_PLACEMENT_MEASUREMENT.md (private working notes).
    /// </summary>
    public class PlacementMeasurement
    {
        private const int ATTEMPTS_PER_STRATEGY = 200;

        [Test, Explicit("Report: run on demand")]
        public void MeasureLayoutStrategies_ForAllCombinations()
        {
            DealConfig config = TestConfigs.Placeholder();
            var random = new CryptoRandomSource();
            var builder = new DealBuilder(random, new SystemClock(), NullEngineLog.Instance);
            var report = new StringBuilder();

            report.AppendLine("# R1 placement measurement");
            report.AppendLine();
            report.AppendLine($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC from config `{config.ConfigVersion}`" +
                              (config.IsPlaceholder ? " (**PLACEHOLDER bags**)." : "."));
            report.AppendLine($"Each strategy makes {ATTEMPTS_PER_STRATEGY} independent attempts per combination; " +
                              "an attempt is valid if it passes `PlacementValidator`. Build time is one full GapRepair build (median of 3).");
            report.AppendLine();
            report.AppendLine("| Mode | Bet | N | P | L | Limits (loss / Reg gap / Big gap) | ShuffleAndCheck valid | GapRepair valid | GapRepair ms/attempt | Full build ms |");
            report.AppendLine("|---|---|---|---|---|---|---|---|---|---|");

            foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
            {
                foreach (BetLevel bet in config.BetLevels)
                {
                    DealSize size = DealSizeCalculator.Calculate(config, mode, bet).Size;
                    PlacementLimits limits = PlacementLimits.For(size, config.PlacementLimits);

                    (double shuffleRate, _) = Measure(new ShuffleLayoutGenerator(), size, limits, random);
                    (double repairRate, double repairMs) = Measure(new GapRepairLayoutGenerator(), size, limits, random);
                    double buildMs = MedianBuildMs(builder, config, mode, bet);

                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "| {0} | {1} | {2} | {3} | {4} | {5} / {6} / {7} | {8:P2} | {9:P1} | {10:F3} | {11:F1} |",
                        mode, bet, size.PaidSpins, size.PrizeTickets, size.LosingTickets,
                        limits.MaxConsecutiveLosses, limits.MaxRegularGap, limits.MaxBigGap,
                        shuffleRate, repairRate, repairMs, buildMs));
                }
            }

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".docs", "R1_PLACEMENT_MEASUREMENT.md"));
            File.WriteAllText(path, report.ToString());
            TestContext.WriteLine(report.ToString());
            UnityEngine.Debug.Log($"R1 measurement written to {path}");
        }

        private static (double validRate, double msPerAttempt) Measure(ICategoryLayoutGenerator generator, DealSize size, PlacementLimits limits, IRandomSource random)
        {
            int valid = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int attempt = 0; attempt < ATTEMPTS_PER_STRATEGY; attempt++)
            {
                TicketCategory[] layout = generator.TryGenerate(
                    (int)size.RegularTickets, (int)size.BigTickets, (int)size.LosingTickets, limits, random);
                if (layout != null && PlacementValidator.Validate(layout, limits).IsValid)
                {
                    valid++;
                }
            }

            stopwatch.Stop();
            return ((double)valid / ATTEMPTS_PER_STRATEGY, stopwatch.Elapsed.TotalMilliseconds / ATTEMPTS_PER_STRATEGY);
        }

        private static double MedianBuildMs(DealBuilder builder, DealConfig config, GameMode mode, BetLevel bet)
        {
            var times = new double[3];
            for (int i = 0; i < times.Length; i++)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                BuildResult result = builder.Build(config, mode, bet);
                stopwatch.Stop();
                Assert.That(result.IsSuccess, Is.True, result.Message);
                times[i] = stopwatch.Elapsed.TotalMilliseconds;
            }

            Array.Sort(times);
            return times[1];
        }
    }
}
