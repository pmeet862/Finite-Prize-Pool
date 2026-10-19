using System.Collections.Generic;
using System.IO;
using System.Text;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Service;
using FinitePool.Engine.Sizing;
using UnityEditor;
using UnityEngine;

namespace FinitePool.Editor
{
    /// <summary>
    /// Writes the submission deliverables into the tracked Docs/ folder:
    /// the 40-row deal-size table (brief §4.2, §7.2) and the simulation report (§7.4).
    /// Both read the shipped config, so re-running them after the math PDF arrives refreshes them.
    /// </summary>
    public static class DealReportsMenu
    {
        private const string CONFIG_PATH = "Assets/FinitePool/Config/deal-config.json";
        private const string TOLERANCE_CONFIG_PATH = "Assets/FinitePool/Config/deal-config.tolerance.json";

        private static string DocsFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs"));

        [MenuItem("Finite Pool/Export Deal-Size Table (CSV)")]
        private static void ExportDealSizeTable() => ExportTable(CONFIG_PATH, "deal-size-table.csv");

        /// <summary>Brief §6 stretch: the same table with RTP allowed anywhere in 90% ± 0.1 points.</summary>
        [MenuItem("Finite Pool/Export Deal-Size Table, Tolerance Mode (CSV)")]
        private static void ExportToleranceTable() => ExportTable(TOLERANCE_CONFIG_PATH, "deal-size-table-tolerance.csv");

        private static void ExportTable(string configPath, string fileName)
        {
            string path = Path.Combine(DocsFolder, fileName);
            File.WriteAllText(path, DealSizeTable.ToCsv(LoadConfig(configPath)));
            Debug.Log($"Deal-size table written to {path}");
            EditorUtility.OpenWithDefaultApp(path);
        }

        [MenuItem("Finite Pool/Write Simulation Report")]
        private static void WriteSimulationReport()
        {
            DealConfig config = LoadConfig(CONFIG_PATH);
            var report = new StringBuilder();
            report.AppendLine("# Simulation report");
            report.AppendLine();
            if (config.IsPlaceholder)
            {
                report.AppendLine($"> **PLACEHOLDER bag values** (config `{config.ConfigVersion}`). Re-run *Finite Pool → Write Simulation Report* once the math PDF values are in the config.");
                report.AppendLine();
            }

            report.AppendLine($"Generated {System.DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. Each deal is played to its last ticket, without animation, " +
                              "through the same `DealService` the game uses, on a private in-memory engine.");
            report.AppendLine();

            bool allExact = AppendSection(report, config, GameMode.Inner, BetLevel.FromCents(100), 10)
                            & AppendSection(report, config, GameMode.Outer, BetLevel.FromCents(100), 1);

            report.AppendLine(allExact
                ? "**Result:** every deal reached exactly the target RTP (payouts × denominator = wagers × numerator, in whole cents)."
                : "**Result:** at least one deal did NOT reach the target RTP exactly. Investigate before submitting.");

            string path = Path.Combine(DocsFolder, "SIMULATION_REPORT.md");
            File.WriteAllText(path, report.ToString());
            Debug.Log($"Simulation report written to {path}");
            EditorUtility.OpenWithDefaultApp(path);
        }

        private static bool AppendSection(StringBuilder report, DealConfig config, GameMode mode, BetLevel bet, int dealCount)
        {
            IReadOnlyList<CompletedDealRecord> deals = DealSimulator.PlayCompleteDeals(config, mode, bet, dealCount);
            bool allExact = true;

            report.AppendLine($"## {mode} {bet}: {dealCount} complete deal(s)");
            report.AppendLine();
            report.AppendLine("| # | Deal ID | Tickets | Wagers | Payouts | Planned RTP | Actual RTP | Reached planned RTP? |");
            report.AppendLine("|---|---|---|---|---|---|---|---|");
            for (int i = 0; i < deals.Count; i++)
            {
                CompletedDealRecord deal = deals[i];
                allExact &= deal.ReachedTargetRtp;
                report.AppendLine($"| {i + 1} | `{deal.DealId}` | {deal.TicketCount} | {Money.FromUnits(deal.WagersUnits)} | " +
                                  $"{Money.FromUnits(deal.PayoutsUnits)} | {deal.PlannedRtp.ToPercentString()} | {deal.ActualRtp.ToPercentString()} ({deal.ActualRtp}) | {(deal.ReachedTargetRtp ? "yes" : "NO")} |");
            }

            report.AppendLine();
            return allExact;
        }

        private static DealConfig LoadConfig(string configPath) =>
            DealConfigJson.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(configPath).text);
    }
}
