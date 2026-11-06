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

        private bool _overlayVisible = true;
        private bool _autoStarted;

        private void Update()
        {
            var s = CarMecaPlugin.Session;
            if (s == null) return;

            if (Input.GetKeyDown(KeyCode.F6))
            { _overlayVisible = !_overlayVisible; if (CarMecaPlugin.Overlay != null) CarMecaPlugin.Overlay.Visible = _overlayVisible; }

            s.EnsureSteam();
            float dt = Time.deltaTime;
            s.Update(dt);
            s.SendPresenceTick(Time.time);

            var cfg = CarMecaPlugin.Cfg;
            // Host/join as soon as Steam is ready (lobby works from the menu, so you can invite
            // friends before entering the garage). Presence sync itself stays gated to the garage.
            if (!_autoStarted && cfg.AutoStart && cfg.Role != MpRole.Menu && s.EnsureSteam())
            {
                s.StartFromConfig();
                _autoStarted = true;
            }
        }

        private void OnGUI()
        {
            CarMecaPlugin.Overlay?.Draw();
        }
    }
}
