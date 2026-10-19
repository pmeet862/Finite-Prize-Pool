using System;
using System.Collections.Generic;
using System.Linq;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Persistence;
using FinitePool.Engine.Service;
using FinitePool.Game;
using UnityEditor;
using UnityEngine;

namespace FinitePool.Editor
{
    /// <summary>
    /// Admin screen (brief §5): retire a deal, apply a new config, and view the completed
    /// and retired deal records.
    /// In Play Mode it works on the running game. Outside Play Mode it shows the records saved
    /// on disk, read-only; retiring and config changes need the running engine.
    /// </summary>
    public sealed class DealAdminWindow : EditorWindow
    {
        private static readonly Column[] DealColumns =
        {
            new Column("Deal ID", 210), new Column("State", 75), new Column("Mode", 50), new Column("Bet", 55),
            new Column("Config", 190), new Column("Spins played", 90), new Column("Wagers", 80), new Column("Payouts", 80),
            new Column("RTP so far", 80), new Column("Target RTP", 80), new Column("Created (UTC)", 150),
            new Column("Activated (UTC)", 150), new Column("Closed (UTC)", 150)
        };

        // Row tints by deal state. Low alpha keeps the text readable in both Editor themes.
        private static readonly Dictionary<DealState, Color> StateColors = new Dictionary<DealState, Color>
        {
            { DealState.Active, new Color(0.20f, 0.75f, 0.30f, 0.25f) },
            { DealState.Ready, new Color(0.25f, 0.50f, 0.95f, 0.25f) },
            { DealState.Completed, new Color(0.55f, 0.55f, 0.55f, 0.20f) },
            { DealState.Retired, new Color(0.90f, 0.30f, 0.25f, 0.25f) }
        };

        private static readonly Column[] CompletedColumns =
        {
            new Column("Deal ID", 210), new Column("Mode", 50), new Column("Bet", 55), new Column("Config", 190),
            new Column("Tickets", 60), new Column("Wagers", 80), new Column("Payouts", 80),
            new Column("Target RTP", 80), new Column("Planned RTP", 80), new Column("Actual RTP", 80), new Column("Completed (UTC)", 150)
        };

        private static readonly Column[] RetiredColumns =
        {
            new Column("Deal ID", 210), new Column("Mode", 50), new Column("Bet", 55), new Column("Reason", 230),
            new Column("Retired (UTC)", 150), new Column("Old config", 190), new Column("New config", 190),
            new Column("Replacement", 210), new Column("Used", 50), new Column("Remaining", 70),
            new Column("Wagers", 80), new Column("Payouts", 80), new Column("Actual RTP", 80)
        };

        private string m_reason = "Operator retire";
        private TextAsset m_newConfig;
        private string m_message = string.Empty;
        private Vector2 m_scroll;
        private SavedDealRecords m_savedRecords;

        [MenuItem("Finite Pool/Deal Admin")]
        private static void Open() => GetWindow<DealAdminWindow>("Deal Admin");

        private void OnInspectorUpdate() => Repaint();

        // Saved records are re-read when the window regains focus (e.g. after Delete Saved Data), not on every repaint.
        private void OnFocus() => m_savedRecords = null;

        /// <summary>Makes every open admin window re-read the saved records, e.g. right after Delete Saved Data.</summary>
        internal static void ReloadSavedRecords()
        {
            foreach (DealAdminWindow window in Resources.FindObjectsOfTypeAll<DealAdminWindow>())
            {
                window.m_savedRecords = null;
                window.Repaint();
            }
        }

        private void OnGUI()
        {
            IDealAdmin admin = Application.isPlaying ? GameBootstrap.Admin : null;
            IReadOnlyList<DealEntry> deals;
            IReadOnlyList<CompletedDealRecord> completed;
            IReadOnlyList<RetireRecord> retired;

            if (admin != null)
            {
                EditorGUILayout.HelpBox("Live: showing the running game.", MessageType.None);
                DrawRetire(admin);
                EditorGUILayout.Space();
                DrawConfig(admin);
                EditorGUILayout.Space();
                if (m_message.Length > 0) EditorGUILayout.HelpBox(m_message, MessageType.None);
                deals = admin.GetDeals();
                completed = admin.GetCompletedDeals();
                retired = admin.GetRetiredDeals();
            }
            else
            {
                if (!TryLoadSavedRecords()) return;
                EditorGUILayout.HelpBox($"Saved records on disk (read-only). Enter Play Mode to retire deals or apply a config.\n{GameBootstrap.DataFolder}", MessageType.Info);
                if (GUILayout.Button("Refresh")) m_savedRecords = null;
                deals = m_savedRecords?.Deals ?? new List<DealEntry>();
                completed = m_savedRecords?.CompletedDeals ?? new List<CompletedDealRecord>();
                retired = m_savedRecords?.RetiredDeals ?? new List<RetireRecord>();
            }

            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
            DrawDeals(deals, admin);
            EditorGUILayout.Space();
            DrawTable($"Completed deals ({completed.Count})", CompletedColumns, completed.Select(CompletedRow));
            EditorGUILayout.Space();
            DrawTable($"Retired deals ({retired.Count}) — target RTP not applicable", RetiredColumns, retired.Select(RetiredRow));
            EditorGUILayout.EndScrollView();
        }

        private bool TryLoadSavedRecords()
        {
            if (m_savedRecords != null) return true;

            try
            {
                m_savedRecords = SavedDealRecords.Read(GameBootstrap.DataFolder);
                return true;
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox($"Could not read saved records: {exception.Message}", MessageType.Error);
                return false;
            }
        }

        private void DrawRetire(IDealAdmin admin)
        {
            EditorGUILayout.LabelField("Retire an active deal", EditorStyles.boldLabel);
            m_reason = EditorGUILayout.TextField("Reason", m_reason);
            EditorGUILayout.LabelField("Use the Retire button on an Active row in the deals table below.", EditorStyles.miniLabel);
        }

        private void RetireDeal(IDealAdmin admin, DealEntry deal)
        {
            try
            {
                RetireRecord record = admin.Retire(deal.Mode, BetLevel.FromUnits(deal.BetUnits), m_reason);
                m_message = $"Retired {record.DealId}; {record.ReplacementDealId} is now active.";
            }
            catch (InvalidOperationException exception)
            {
                m_message = exception.Message;
            }
        }

        /// <summary>
        /// Every deal from the saved state, tinted by state. Only Active deals can be retired, and only
        /// in Play Mode (<paramref name="admin"/> is null otherwise). Shows bookkeeping only, never tickets.
        /// </summary>
        private void DrawDeals(IReadOnlyList<DealEntry> deals, IDealAdmin admin)
        {
            EditorGUILayout.LabelField($"Deals ({deals.Count})", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            foreach (KeyValuePair<DealState, Color> state in StateColors)
            {
                Rect swatch = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
                EditorGUI.DrawRect(swatch, state.Value);
                GUILayout.Label(state.Key.ToString(), GUILayout.Width(80));
            }

            EditorGUILayout.EndHorizontal();
            DrawRow(DealColumns, DealColumns.Select(column => column.Title).ToArray(), EditorStyles.boldLabel);

            IEnumerable<DealEntry> ordered = deals
                .OrderBy(deal => StateOrder(deal.State))
                .ThenBy(deal => deal.Mode)
                .ThenBy(deal => deal.BetUnits);

            DealEntry toRetire = null;
            foreach (DealEntry deal in ordered)
            {
                Rect row = EditorGUILayout.BeginHorizontal();
                EditorGUI.DrawRect(row, StateColors[deal.State]);
                string[] cells = DealRow(deal);
                for (int i = 0; i < DealColumns.Length; i++)
                {
                    GUILayout.Label(new GUIContent(cells[i], cells[i]), EditorStyles.label, GUILayout.Width(DealColumns[i].Width));
                }

                if (admin != null && deal.State == DealState.Active && GUILayout.Button("Retire", GUILayout.Width(60)))
                {
                    toRetire = deal;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (deals.Count == 0) EditorGUILayout.LabelField("None yet.", EditorStyles.miniLabel);

            // Retired after the loop so the table isn't redrawn from changed data mid-layout.
            if (toRetire != null) RetireDeal(admin, toRetire);
        }

        private static int StateOrder(DealState state)
        {
            switch (state)
            {
                case DealState.Active: return 0;
                case DealState.Ready: return 1;
                case DealState.Completed: return 2;
                default: return 3;
            }
        }

        private static string[] DealRow(DealEntry deal) => new[]
        {
            deal.DealId, deal.State.ToString(), deal.Mode.ToString(), BetLevel.FromUnits(deal.BetUnits).ToString(), deal.ConfigVersion,
            $"{deal.Cursor} / {deal.TicketCount}", Money.FromUnits(deal.WagersUnits).ToString(), Money.FromUnits(deal.PayoutsUnits).ToString(),
            deal.WagersUnits > 0 ? new Fraction(deal.PayoutsUnits, deal.WagersUnits).ToPercentString() : "-",
            new Fraction(deal.TargetRtpNumerator, deal.TargetRtpDenominator).ToPercentString(),
            deal.CreatedUtc.ToString("u"), deal.ActivatedUtc?.ToString("u") ?? "-", deal.ClosedUtc?.ToString("u") ?? "-"
        };

        private void DrawConfig(IDealAdmin admin)
        {
            EditorGUILayout.LabelField("Apply a new config (next deals only)", EditorStyles.boldLabel);
            m_newConfig = (TextAsset)EditorGUILayout.ObjectField("Config JSON", m_newConfig, typeof(TextAsset), false);

            GUI.enabled = m_newConfig != null;
            if (GUILayout.Button("Apply config"))
            {
                try
                {
                    DealConfig config = DealConfigJson.Parse(m_newConfig.text);
                    admin.ApplyConfig(config);
                    m_message = $"Config {config.ConfigVersion} applied. Active deals keep their version.";
                }
                catch (DealConfigException exception)
                {
                    m_message = exception.Message;
                }
            }

            GUI.enabled = true;
        }

        private static string[] CompletedRow(CompletedDealRecord record) => new[]
        {
            record.DealId, record.Mode.ToString(), BetLevel.FromUnits(record.BetUnits).ToString(), record.ConfigVersion,
            record.TicketCount.ToString(), Money.FromUnits(record.WagersUnits).ToString(), Money.FromUnits(record.PayoutsUnits).ToString(),
            record.TargetRtp.ToPercentString(), record.PlannedRtp.ToPercentString(), record.ActualRtp.ToPercentString(), record.CompletedUtc.ToString("u")
        };

        private static string[] RetiredRow(RetireRecord record) => new[]
        {
            record.DealId, record.Mode.ToString(), BetLevel.FromUnits(record.BetUnits).ToString(), record.Reason,
            record.RetiredUtc.ToString("u"), record.OldConfigVersion, record.NewConfigVersion, record.ReplacementDealId ?? "-",
            record.TicketsUsed.ToString(), record.TicketsRemaining.ToString(),
            Money.FromUnits(record.WagersUnits).ToString(), Money.FromUnits(record.PayoutsUnits).ToString(),
            record.ActualRtp?.ToPercentString() ?? "n/a"
        };

        private static void DrawTable(string title, Column[] columns, IEnumerable<string[]> rows)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            DrawRow(columns, columns.Select(column => column.Title).ToArray(), EditorStyles.boldLabel);

            bool any = false;
            foreach (string[] row in rows)
            {
                DrawRow(columns, row, EditorStyles.label);
                any = true;
            }

            if (!any) EditorGUILayout.LabelField("None yet.", EditorStyles.miniLabel);
        }

        private static void DrawRow(Column[] columns, string[] cells, GUIStyle style)
        {
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < columns.Length; i++)
            {
                // Full value in the tooltip, in case a long reason or id is clipped.
                GUILayout.Label(new GUIContent(cells[i], cells[i]), style, GUILayout.Width(columns[i].Width));
            }

            EditorGUILayout.EndHorizontal();
        }

        private readonly struct Column
        {
            public string Title { get; }
            public float Width { get; }

            public Column(string title, float width)
            {
                Title = title;
                Width = width;
            }
        }
    }
}
