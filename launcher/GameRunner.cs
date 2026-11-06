using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarMecaSim.Shared;

namespace CarMecaSim.Launcher
{
    /// <summary>Writes the mp_config the mod reads, then launches the game.</summary>
    public sealed class GameRunner
    {
        private readonly string _gameDir;
        public GameRunner(string gameDir) => _gameDir = gameDir;

        public static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public string WriteConfig(MpConfig cfg)
        {
            var path = MpConfig.FullPathFor(_gameDir);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOpts));
            return path;
        }

        /// <summary>Launch via Steam if possible (keeps overlay/invites working), else launch the exe directly.</summary>
        public void Launch(bool viaSteam = true)
        {
            if (viaSteam)
            {
                try
                {
                    Process.Start(new ProcessStartInfo($"steam://rungameid/{Constants.AppId}") { UseShellExecute = true });
                    return;
                }
                catch { /* fall through to direct */ }
            }
            var exe = Path.Combine(_gameDir, Constants.GameExeName);
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = _gameDir, UseShellExecute = true });
        }
    }
}
