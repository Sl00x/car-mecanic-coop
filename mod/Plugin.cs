using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using CarMecaSim.Shared;
using CarMecaSim.Mp;

[assembly: MelonInfo(typeof(CarMecaPlugin), Constants.ModName, Constants.ModVersion, "alexu")]
[assembly: MelonGame("Red Dot Games", "Car Mechanic Simulator 2021")]

namespace CarMecaSim.Mp
{
    public sealed class CarMecaPlugin : MelonMod
    {
        public static CarMecaPlugin Instance { get; private set; }
        public static MelonLogger.Instance Log { get; private set; }

        public MpConfig Config { get; private set; } = new();
        private bool _overlay = true;

        public override void OnInitializeMelon()
        {
            Instance = this;
            Log = LoggerInstance;
            Log.Msg("CarMecaSim co-op mod initializing...");
            LoadConfig();
            Log.Msg($"Config: role={Config.Role} name={Config.DisplayName} lobby={Config.LobbyId} max={Config.MaxPlayers}");
        }

        private void LoadConfig()
        {
            try
            {
                var path = Path.Combine(MelonEnvironment.UserDataDirectory, "CarMecaSim", "mp_config.json");
                if (File.Exists(path))
                {
                    Config = ConfigIo.Read(path);
                    Log.Msg("Loaded mp_config.json");
                }
                else
                {
                    Log.Warning("No mp_config.json found; defaulting to Menu role.");
                }
            }
            catch (Exception e) { Log.Error("Config load failed: " + e); }
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F6)) _overlay = !_overlay;
        }

        public override void OnGUI()
        {
            if (!_overlay) return;
            GUI.Box(new Rect(10, 10, 280, 92), "");
            GUILayout.BeginArea(new Rect(18, 14, 266, 86));
            GUILayout.Label($"<b>CarMecaSim Co-op</b> v{Constants.ModVersion}");
            GUILayout.Label($"Role: {Config.Role}   Name: {Config.DisplayName}");
            GUILayout.Label($"Net: not connected (F6 to toggle)");
            GUILayout.EndArea();
        }
    }
}
