using System.Collections;
using System.Linq;
using FinitePool.Engine.Core;
using FinitePool.Engine.Service;
using UnityEngine;

namespace FinitePool.Game
{
    /// <summary>
    /// The game screen (brief §5): player, mode and bet selectors, a spin button, the
    /// result and the balance. It only talks to <see cref="IDealService"/> and never sees tickets.
    ///
    /// Spin flow: ask the engine for a spin (already saved when it returns), play the
    /// "animation", show the result, then tell the engine it was shown. If the game closes
    /// mid-animation, the engine still has the result as pending and it is shown on the next start.
    /// </summary>
    public sealed class GameClientView : MonoBehaviour
    {
        private const float SPIN_SECONDS = 1.5f;
        private static readonly GameMode[] Modes = { GameMode.Inner, GameMode.Outer };

        private int m_playerIndex;
        private int m_modeIndex;
        private int m_betIndex = 3; // $1.00
        private bool m_spinning;
        private SpinResult m_shownResult;
        private Money m_balanceDuringSpin;
        private string m_message = string.Empty;

        public PlayerId CurrentPlayer => GameBootstrap.Players[m_playerIndex];
        public GameMode CurrentMode => Modes[m_modeIndex];
        public BetLevel CurrentBet => Deals.BetLevels[m_betIndex];

        private static IDealService Deals => GameBootstrap.Deals;

        private void Start()
        {
            Deals.EnsurePrepared(CurrentMode, CurrentBet);
            ShowPendingResult();
        }

        private void OnGUI()
        {
            // A script recompile during Play Mode clears the bootstrap's statics; restart Play Mode to recover.
            if (Deals == null || GameBootstrap.Players == null) return;

            GUILayout.BeginArea(new Rect(20, 20, 460, 560), GUI.skin.box);
            GUILayout.Label("<b>Finite Prize Pool</b>", RichText());

            GUI.enabled = !m_spinning;
            DrawSelectors();
            GUI.enabled = true;

            GUILayout.Space(10);
            GUILayout.Label($"Balance: {(m_spinning ? m_balanceDuringSpin : Deals.GetBalance(CurrentPlayer))}");

            DealAvailability availability = Deals.GetAvailability(CurrentMode, CurrentBet);
            if (availability.Status != AvailabilityStatus.Available)
            {
                GUILayout.Label(availability.Status == AvailabilityStatus.Preparing ? "Preparing deal…" : availability.ToString());
            }

            GUI.enabled = !m_spinning && availability.Status == AvailabilityStatus.Available;
            if (GUILayout.Button($"SPIN ({CurrentBet})", GUILayout.Height(50)))
            {
                StartCoroutine(SpinRoutine());
            }

            GUI.enabled = true;

            GUILayout.Space(10);
            GUILayout.Label(m_spinning ? "Spinning…" : Describe(m_shownResult), RichText());
            GUILayout.Label(m_message);
            GUILayout.EndArea();
        }

        private void DrawSelectors()
        {
            int player = GUILayout.Toolbar(m_playerIndex, GameBootstrap.Players.Select(id => id.Value).ToArray());
            if (player != m_playerIndex)
            {
                m_playerIndex = player;
                m_shownResult = null;
                m_message = string.Empty;
                ShowPendingResult();
            }

            int mode = GUILayout.Toolbar(m_modeIndex, Modes.Select(m => m.ToString()).ToArray());
            int bet = GUILayout.SelectionGrid(m_betIndex, Deals.BetLevels.Select(level => level.ToString()).ToArray(), 5);
            if (mode != m_modeIndex || bet != m_betIndex)
            {
                m_modeIndex = mode;
                m_betIndex = bet;
                Deals.EnsurePrepared(CurrentMode, CurrentBet);
            }
        }

        private IEnumerator SpinRoutine()
        {
            Money balanceBefore = Deals.GetBalance(CurrentPlayer);
            SpinOutcome outcome = Deals.Spin(CurrentPlayer, CurrentMode, CurrentBet); // saved before any animation
            if (!outcome.IsAccepted)
            {
                m_message = outcome.Message;
                yield break;
            }

            m_message = string.Empty;
            yield return Reveal(outcome.Result, balanceBefore - outcome.Result.Wager);
        }

        /// <summary>A result saved but not shown before the game closed is shown now, without a new spin.</summary>
        private void ShowPendingResult()
        {
            SpinResult pending = Deals.GetPendingResult(CurrentPlayer);
            if (pending != null)
            {
                m_message = "Showing a result that wasn't shown before the game closed.";
                StartCoroutine(Reveal(pending, pending.BalanceAfter - pending.Award));
            }
        }

        private IEnumerator Reveal(SpinResult result, Money balanceDuringSpin)
        {
            m_spinning = true;
            m_shownResult = null;
            m_balanceDuringSpin = balanceDuringSpin;
            yield return new WaitForSeconds(SPIN_SECONDS);

            m_spinning = false;
            m_shownResult = result;
            Deals.MarkShown(result.Player, result.SpinId);
        }

        private static string Describe(SpinResult result)
        {
            if (result == null) return string.Empty;

            switch (result.AwardKind)
            {
                case AwardKind.Multiplier: return $"<b>WIN {result.Award}</b>  multiplier prize ({result.Category})";
                case AwardKind.Bonus: return $"<b>WIN {result.Award}</b>  bonus prize ({result.Category})";
                default: return "No win (loss)";
            }
        }

        private static GUIStyle RichText() => new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16 };
    }
}
