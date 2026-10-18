using System.Collections.Generic;
using System.IO;
using System.Linq;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Infrastructure;
using FinitePool.Engine.Service;
using UnityEngine;

namespace FinitePool.Game
{
    /// <summary>
    /// Composition root: creates the deal engine once and hands out its interfaces.
    /// The only place that touches Unity paths. Application.persistentDataPath is read
    /// here, on the main thread, and passed to the engine as a plain string, because
    /// background builds must never call Unity APIs.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private TextAsset m_dealConfig;
        [SerializeField] private string[] m_playerIds = { "player-1", "player-2" };
        [SerializeField] private long m_startingBalanceCents = 10000;

        public static IDealService Deals { get; private set; }
        public static IDealAdmin Admin { get; private set; }
        public static IDealDiagnostics Diagnostics { get; private set; }
        public static IReadOnlyList<PlayerId> Players { get; private set; }

        /// <summary>Where all saved deals, state and the spin log live. Main thread only (it reads a Unity API).</summary>
        public static string DataFolder => Path.Combine(Application.persistentDataPath, "FinitePool");

        private void Awake()
        {
            DealConfig config = DealConfigJson.Parse(m_dealConfig.text);
            string dataFolder = DataFolder;
            DealService service = DealService.OpenWithFiles(config, dataFolder, new UnityEngineLog());

            Players = m_playerIds.Select(id => new PlayerId(id)).ToList();
            foreach (PlayerId player in Players)
            {
                service.RegisterPlayer(player, Money.FromCents(m_startingBalanceCents)); // no-op if already saved
            }

            Deals = service;
            Admin = service;
            Diagnostics = service;
            Debug.Log($"Finite Prize Pool started. Config {config.ConfigVersion}, data in {dataFolder}");
        }

        private void OnDestroy()
        {
            Deals = null;
            Admin = null;
            Diagnostics = null;
        }
    }

    /// <summary>Sends engine log lines to the Unity console. Debug.Log is safe to call from background threads.</summary>
    public sealed class UnityEngineLog : IEngineLog
    {
        public void Info(string message) => Debug.Log("[Engine] " + message);
        public void Warning(string message) => Debug.LogWarning("[Engine] " + message);
        public void Error(string message) => Debug.LogError("[Engine] " + message);
    }
}
