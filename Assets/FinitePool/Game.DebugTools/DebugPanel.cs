using FinitePool.Engine.Core;
using FinitePool.Engine.Service;
using UnityEngine;

namespace FinitePool.Game.DebugTools
{
    /// <summary>
    /// Developer-only panel (brief §5): deal ID, deal state, config version and spins played.
    /// It reads <see cref="IDealDiagnostics"/>, which has no ticket data, so it can't show
    /// upcoming tickets or remaining prizes.
    ///
    /// This assembly only compiles in the Editor and development builds, and the panel adds
    /// itself at startup, so a release build has neither the code nor a scene reference to it.
    /// </summary>
    public sealed class DebugPanel : MonoBehaviour
    {
        private const int FAST_SPINS = 10;

        private GameClientView m_client;
        private string m_message = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (GameBootstrap.Diagnostics != null)
            {
                new GameObject("Debug Panel (dev only)").AddComponent<DebugPanel>();
            }
        }

        private void Start() => m_client = FindFirstObjectByType<GameClientView>();

        private void OnGUI()
        {
            if (m_client == null || GameBootstrap.Diagnostics == null) return;

            GUILayout.BeginArea(new Rect(Screen.width - 380, 20, 360, 260), GUI.skin.box);
            GUILayout.Label("DEBUG (dev builds only)");

            DealInfo info = GameBootstrap.Diagnostics.GetActiveDealInfo(m_client.CurrentMode, m_client.CurrentBet);
            if (info == null)
            {
                GUILayout.Label($"{m_client.CurrentMode} {m_client.CurrentBet}: no active deal");
            }
            else
            {
                GUILayout.Label($"Deal ID: {info.DealId}");
                GUILayout.Label($"State: {info.State}");
                GUILayout.Label($"Config version: {info.ConfigVersion}");
                GUILayout.Label($"Spins played: {info.SpinsPlayed}");
            }

            if (GUILayout.Button("Top up current player +$100"))
            {
                GameBootstrap.Admin.AdjustBalance(m_client.CurrentPlayer, Money.FromCents(10000), "debug top-up");
            }

            if (GUILayout.Button($"Fast spin ×{FAST_SPINS} (no animation)"))
            {
                FastSpin();
            }

            GUILayout.Label(m_message);
            GUILayout.EndArea();
        }

        /// <summary>Plays several spins at once, through the same engine calls the client uses, to reach a deal handoff quickly.</summary>
        private void FastSpin()
        {
            for (int i = 0; i < FAST_SPINS; i++)
            {
                SpinOutcome outcome = GameBootstrap.Deals.Spin(m_client.CurrentPlayer, m_client.CurrentMode, m_client.CurrentBet);
                if (!outcome.IsAccepted)
                {
                    m_message = outcome.Message;
                    return;
                }

                GameBootstrap.Deals.MarkShown(outcome.Result.Player, outcome.Result.SpinId);
            }

            m_message = $"Played {FAST_SPINS} spins.";
        }
    }
}
