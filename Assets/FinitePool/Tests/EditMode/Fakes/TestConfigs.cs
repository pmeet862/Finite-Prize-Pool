using System;
using System.IO;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FinitePool.Tests.Fakes
{
    /// <summary>
    /// Loads the real config file the game ships with, and makes edited copies
    /// for tests that need a specific shape (tighter limits, odd bags, ...).
    /// </summary>
    internal static class TestConfigs
    {
        public static string ConfigPath => Path.Combine(Application.dataPath, "FinitePool", "Config", "deal-config.json");

        public static string ShippedJson() => File.ReadAllText(ConfigPath);

        public static DealConfig Shipped() => DealConfigJson.Parse(ShippedJson());

        /// <summary>The shipped config with an edit applied to its JSON before parsing.</summary>
        public static DealConfig ShippedWith(Action<JObject> edit) => DealConfigJson.Parse(EditedJson(edit));

        public static string EditedJson(Action<JObject> edit)
        {
            JObject root = JObject.Parse(ShippedJson());
            edit(root);
            return root.ToString();
        }

        public static BetLevel Bet(long cents) => BetLevel.FromCents(cents);
    }
}
