using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using CarMecaSim.Shared;
using CarMecaSim.Mp;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;
using CarMecaSim.Mp.UI;

[assembly: MelonInfo(typeof(CarMecaPlugin), Constants.ModName, Constants.ModVersion, "alexu")]
[assembly: MelonGame("Red Dot Games", "Car Mechanic Simulator 2021")]

namespace CarMecaSim.Mp
{
    public sealed class CarMecaPlugin : MelonMod
    {
        public static CarMecaPlugin Instance { get; private set; }
        public static MelonLogger.Instance Log { get; private set; }

        private readonly MpSession _session = new();
        private Overlay _overlay;
        private MpConfig _config = new();
        private bool _autoStarted;
        private bool _overlayVisible = true;

        public override void OnInitializeMelon()
        {
            Instance = this;
            Log = LoggerInstance;
            Log.Msg("CarMecaSim co-op initializing...");
            LoadConfig();
            _session.Init(_config, s => Log.Msg(s));
            _overlay = new Overlay(_session);
            Log.Msg($"Ready. role={_config.Role} name={_config.DisplayName} lobby={_config.LobbyId}");
        }

        private void LoadConfig()
        {
            try
            {
                var path = Path.Combine(MelonEnvironment.UserDataDirectory, "CarMecaSim", "mp_config.json");
                if (File.Exists(path)) { _config = ConfigIo.Read(path); Log.Msg("Loaded mp_config.json"); }
                else Log.Warning("No mp_config.json; use the in-game overlay (F6) to host/join.");
            }
            catch (Exception e) { Log.Error("Config load failed: " + e); }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            GameRefs.InvalidateSceneRefs();
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F6)) { _overlayVisible = !_overlayVisible; if (_overlay != null) _overlay.Visible = _overlayVisible; }

            // Bring Steam up (no-op once ready) and pump the session.
            _session.EnsureSteam();
            float dt = Time.deltaTime;
            _session.Update(dt);
            _session.SendPresenceTick(Time.time);

            // Auto host/join from the launcher config the first time we reach the garage.
            if (!_autoStarted && _config.AutoStart && _config.Role != MpRole.Menu
                && GameRefs.InGarage && _session.EnsureSteam())
            {
                _session.StartFromConfig();
                _autoStarted = true;
            }
        }

        public override void OnGUI()
        {
            _overlay?.Draw();
        }

        public override void OnApplicationQuit()
        {
            try { _session.Leave(); } catch { }
        }
    }
}
