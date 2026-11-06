using System;
using UnityEngine;
using CarMecaSim.Mp.Core;

namespace CarMecaSim.Mp.Game
{
    /// <summary>
    /// Bridge to the game's native main menu (CMS.MainMenu.MainMenuManager): detect it and open the
    /// save-selection ("play") section so hosting/joining uses the game's own load-a-save flow.
    /// </summary>
    public static class MenuActions
    {
        private static CMS.MainMenu.MainMenuManager _mm;

        public static CMS.MainMenu.MainMenuManager Menu()
        {
            try { if (_mm == null) _mm = UnityEngine.Object.FindObjectOfType<CMS.MainMenu.MainMenuManager>(); }
            catch { }
            return _mm;   // Unity's overridden == re-finds after a scene change destroys it
        }

        /// <summary>True when we're sitting on the game's main menu.</summary>
        public static bool OnMainMenu => Menu() != null;

        /// <summary>True when the top-level menu (Play/Settings/Quit) is the active section — i.e. no
        /// native sub-window (save list, difficulty, settings) is open over it.</summary>
        public static bool MainSectionActive()
        {
            try
            {
                var mm = Menu();
                if (mm == null) return false;
                var ms = mm.GetMainSection();
                return ms != null && ms.IsActive();
            }
            catch { return false; }
        }

        /// <summary>Open the native save-selection screen (the game's own "play" section).</summary>
        public static void OpenSaveSelection()
        {
            try
            {
                var mm = Menu();
                if (mm != null) mm.OpenPlaySection(true, true);
                else CarMecaPlugin.Logger?.LogWarning("[menu] no MainMenuManager to open play section");
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[menu] OpenPlaySection failed: " + e.Message); }
        }
    }
}
