using System;
using UnityEngine;
// BepInEx Il2CppInterop keeps original namespaces: game global types (GameScript, GameMode,
// FPSCamera, CharacterMotor, SceneType, gameMode) need no using.

namespace CarMecaSim.Mp.Game
{
    /// <summary>Cached, null-safe access to the game objects the mod needs.</summary>
    public static class GameRefs
    {
        private static FPSCamera _cam;
        private static Camera _eye;

        public static SceneType CurrentScene
        {
            get
            {
                try { var gs = GameScript.Get(); return gs != null ? gs.CurrentSceneType : SceneType.None; }
                catch { return SceneType.None; }
            }
        }

        public static gameMode CurrentMode
        {
            get { try { var gm = GameMode.Get(); return gm != null ? gm.currentMode : gameMode.None; } catch { return gameMode.None; } }
        }

        /// <summary>True when we are standing in the garage (co-op-able gameplay).</summary>
        public static bool InGarage
        {
            get
            {
                try { return GameScript.Get() != null && CurrentScene == SceneType.Garage; }
                catch { return false; }
            }
        }

        /// <summary>
        /// True when an actual game session is loaded and running (any gameplay scene) — NOT the
        /// main menu and NOT a loading screen. Gates the in-game HUD per the design.
        /// </summary>
        public static bool InGameSession
        {
            get
            {
                try
                {
                    var scene = CurrentScene;
                    return GameScript.Get() != null && scene != SceneType.None && scene != SceneType.Menu;
                }
                catch { return false; }
            }
        }

        public static FPSCamera Cam
        {
            get
            {
                if (_cam == null)
                { try { _cam = UnityEngine.Object.FindObjectOfType<FPSCamera>(); } catch { } }
                return _cam;
            }
        }

        /// <summary>The first-person eye transform (position + look). Null outside gameplay.</summary>
        public static Transform Eye
        {
            get
            {
                try
                {
                    if (_eye == null)
                    {
                        var c = Cam;
                        if (c != null && c.MainCamera != null) _eye = c.MainCamera;
                        else _eye = Camera.main;
                    }
                    return _eye != null ? _eye.transform : null;
                }
                catch { return null; }
            }
        }

        private static CMS.MainMenu.MainMenuManager _mm;

        /// <summary>True when the game's main menu is on screen (before/between game sessions).</summary>
        public static bool OnMainMenu
        {
            get
            {
                try
                {
                    if (CurrentScene == SceneType.Menu) return true;
                    if (_mm == null) _mm = UnityEngine.Object.FindObjectOfType<CMS.MainMenu.MainMenuManager>();
                    return _mm != null;   // Unity's overridden == re-finds after a scene change destroys it
                }
                catch { return false; }
            }
        }

        /// <summary>Enable/disable the game's FPS controller input (mouse-look + movement + world interaction)
        /// WITHOUT disabling the camera render. The game toggles this flag itself during its own pause, so it's
        /// the clean way to free the mouse for our overlay: with it off, the camera stops grabbing the cursor
        /// and typing in chat no longer leaks into gameplay keybinds.</summary>
        public static void SetPlayerInputEnabled(bool enabled)
        {
            try { var cam = Cam; if (cam != null) cam.readInput = enabled; } catch { }
        }

        /// <summary>Current FPS-input state (true if unknown / no camera yet).</summary>
        public static bool PlayerInputEnabled
        {
            get { try { var cam = Cam; return cam == null || cam.readInput; } catch { return true; } }
        }

        public static void InvalidateSceneRefs() { _cam = null; _eye = null; _mm = null; }
    }
}
