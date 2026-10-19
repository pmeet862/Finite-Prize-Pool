using System.IO;
using FinitePool.Game;
using UnityEditor;
using UnityEngine;

namespace FinitePool.Editor
{
    /// <summary>
    /// Finite Pool → Delete Saved Data: wipes every saved deal, the engine state (balances,
    /// deal positions, pending results, records) and the spin log, so the next Play starts
    /// from scratch. The testing equivalent of Edit → Clear All PlayerPrefs.
    /// </summary>
    public static class DeleteSavedDataMenu
    {
        private const string MENU_PATH = "Finite Pool/Delete Saved Data";

        [MenuItem(MENU_PATH)]
        private static void DeleteSavedData()
        {
            string folder = GameBootstrap.DataFolder;
            if (!Directory.Exists(folder))
            {
                Debug.Log($"No saved Finite Pool data to delete ({folder}).");
                return;
            }

            if (!EditorUtility.DisplayDialog("Delete saved data",
                    $"Delete all saved deals, balances, records and the spin log?\n\n{folder}\n\nThis can't be undone.", "Delete", "Cancel"))
            {
                return;
            }

            Directory.Delete(folder, recursive: true);
            DealAdminWindow.ReloadSavedRecords();
            Debug.Log($"Deleted saved Finite Pool data ({folder}). The next Play starts from scratch.");
        }

        // Greyed out in Play Mode: a deal still building in the background could be writing into the folder.
        [MenuItem(MENU_PATH, validate = true)]
        private static bool CanDeleteSavedData() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
