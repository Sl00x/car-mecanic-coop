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

        public static void InvalidateSceneRefs() { _cam = null; _eye = null; }
    }
}
