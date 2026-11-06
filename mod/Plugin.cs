using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using CarMecaSim.Shared;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;
using CarMecaSim.Mp.UI;

namespace CarMecaSim.Mp
{
    [BepInPlugin("com.alexu.carmecasim.mp", Constants.ModName, Constants.ModVersion)]
    public sealed class CarMecaPlugin : BasePlugin
    {
        public static ManualLogSource Logger;
        public static MpSession Session;
        public static Overlay Overlay;
        public static MpConfig Cfg = new();

        public override void Load()
        {
            Logger = Log;
            Log.LogInfo("CarMecaSim co-op loading...");
            LoadConfig();

            Session = new MpSession();
            Session.Init(Cfg, s => Log.LogInfo(s));
            Overlay = new Overlay(Session);

            // Drive Update/OnGUI via an injected Il2Cpp MonoBehaviour (loader-independent game loop).
            ClassInjector.RegisterTypeInIl2Cpp<MpBehaviour>();
            var go = new GameObject("CarMecaSim_Mp");
            go.hideFlags = HideFlags.HideAndDontSave;
            GameObject.DontDestroyOnLoad(go);
            go.AddComponent<MpBehaviour>();

            Log.LogInfo($"Ready. role={Cfg.Role} name={Cfg.DisplayName} lobby={Cfg.LobbyId}");
        }

        private void LoadConfig()
        {
            try
            {
                var path = Path.Combine(Paths.ConfigPath, "CarMecaSim", "mp_config.json");
                if (File.Exists(path)) { Cfg = ConfigIo.Read(path); Log.LogInfo("Loaded mp_config.json"); }
                else Log.LogWarning("No mp_config.json; use the in-game overlay (F6) to host/join.");
            }
            catch (Exception e) { Log.LogError("Config load failed: " + e); }
        }
    }

    /// <summary>Il2Cpp-injected MonoBehaviour that forwards the Unity loop to the mod.</summary>
    public sealed class MpBehaviour : MonoBehaviour
    {
        public MpBehaviour(IntPtr ptr) : base(ptr) { }

        private bool _autoStarted;
        private bool _frozen;
        private float _savedTime = 1f;
        private CursorLockMode _savedLock;
        private bool _savedCursor;

        private void Update()
        {
            var s = CarMecaPlugin.Session;
            if (s == null) return;

            // F6 opens/closes the full co-op panel — only during an active session.
            if (GameRefs.InGameSession && Input.GetKeyDown(KeyCode.F6))
            {
                var ov = CarMecaPlugin.Overlay;
                if (ov != null) { ov.Visible = !ov.Visible; SetFrozen(ov.Visible); }
            }
            // Safety: never stay frozen if we lose the session or the panel closed another way.
            if (_frozen && (!GameRefs.InGameSession || CarMecaPlugin.Overlay == null || !CarMecaPlugin.Overlay.Visible))
                SetFrozen(false);
            // Keep the cursor free each frame while frozen, so the game's FPS controller can't grab it back.
            if (_frozen)
            {
                try { Time.timeScale = 0f; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } catch { }
            }

            s.EnsureSteam();
            // Use UNSCALED time so networking/chat keep running even while the game is frozen (timeScale 0).
            s.Update(Time.unscaledDeltaTime);
            s.SendPresenceTick(Time.unscaledTime);

            var cfg = CarMecaPlugin.Cfg;
            if (!_autoStarted && cfg.AutoStart && cfg.Role != MpRole.Menu && s.EnsureSteam())
            {
                s.StartFromConfig();
                _autoStarted = true;
            }
        }

        /// <summary>Freeze/unfreeze the local game while the co-op panel is open (F6), freeing the cursor.</summary>
        private void SetFrozen(bool on)
        {
            if (on == _frozen) return;
            _frozen = on;
            try
            {
                if (on)
                {
                    _savedTime = Time.timeScale; _savedLock = Cursor.lockState; _savedCursor = Cursor.visible;
                    Time.timeScale = 0f;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    Time.timeScale = _savedTime <= 0f ? 1f : _savedTime;
                    Cursor.lockState = _savedLock;
                    Cursor.visible = _savedCursor;
                }
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("freeze toggle failed: " + e.Message); }
        }

        private void OnGUI()
        {
            CarMecaPlugin.Overlay?.Draw();
        }
    }
}
