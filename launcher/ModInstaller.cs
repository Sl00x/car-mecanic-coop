using System;
using System.IO;
using System.Linq;
using CarMecaSim.Shared;

namespace CarMecaSim.Launcher
{
    public sealed class InstallStatus
    {
        public bool GameFound;
        public string? GameDir;
        public bool LoaderInstalled;       // BepInEx present
        public bool InteropGenerated;      // BepInEx/interop generated (first launch)
        public bool ModInstalled;
        public string? InstalledModVersion;
        public bool SteamAppIdFile;

        public bool FullyReady => GameFound && LoaderInstalled && ModInstalled; // interop auto-generates on first launch
    }

    /// <summary>Installs/verifies/removes BepInEx + the co-op plugin in the game install.</summary>
    public sealed class ModInstaller
    {
        private readonly string _gameDir;
        public ModInstaller(string gameDir) => _gameDir = gameDir;

        private string Winhttp    => Path.Combine(_gameDir, "winhttp.dll");
        private string BepInExDir => Path.Combine(_gameDir, "BepInEx");
        private string InteropDir => Path.Combine(BepInExDir, "interop");
        private string PluginsDir => Path.Combine(BepInExDir, "plugins");
        private string ModDll     => Path.Combine(PluginsDir, "CarMecaSim", "CarMecaSim.Mp.dll");
        private string AppIdFile  => Path.Combine(_gameDir, "steam_appid.txt");

        private static readonly string[] ShipDlls =
            { "CarMecaSim.Mp.dll", "CarMecaSim.Shared.dll", "Steamworks.NET.dll" };

        public InstallStatus GetStatus()
        {
            var s = new InstallStatus { GameDir = _gameDir, GameFound = GameLocator.IsGameDir(_gameDir) };
            s.LoaderInstalled = File.Exists(Winhttp) && Directory.Exists(BepInExDir);
            s.InteropGenerated = Directory.Exists(InteropDir) &&
                                 File.Exists(Path.Combine(InteropDir, "Assembly-CSharp.dll"));
            s.ModInstalled = File.Exists(ModDll);
            s.SteamAppIdFile = File.Exists(AppIdFile);
            if (s.ModInstalled)
            {
                try { s.InstalledModVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(ModDll).ProductVersion; }
                catch { /* ignore */ }
            }
            return s;
        }

        /// <summary>Locate the install payload (BepInEx + plugin). Works from a shipped layout or the dev repo.</summary>
        public static string? FindPayloadRoot()
        {
            var baseDir = AppContext.BaseDirectory;
            var shipped = Path.Combine(baseDir, "payload");
            if (Directory.Exists(Path.Combine(shipped, "BepInEx_x64"))) return shipped;

            // Dev fallback: climb to repo root (marked by the solution file), payload = <repo>/tools
            var dir = new DirectoryInfo(baseDir);
            while (dir != null
                   && !File.Exists(Path.Combine(dir.FullName, "CarMecaSim.sln"))
                   && !File.Exists(Path.Combine(dir.FullName, "CarMecaSim.slnx")))
                dir = dir.Parent;
            if (dir != null && Directory.Exists(Path.Combine(dir.FullName, "tools", "BepInEx_x64")))
                return Path.Combine(dir.FullName, "tools");
            return null;
        }

        /// <summary>Install BepInEx if missing. Returns true if present after the call.</summary>
        public bool EnsureLoader(string payloadRoot, Action<string>? log = null)
        {
            if (File.Exists(Winhttp) && Directory.Exists(BepInExDir)) { log?.Invoke("BepInEx already installed."); return true; }
            var payload = Path.Combine(payloadRoot, "BepInEx_x64");
            if (!Directory.Exists(payload)) { log?.Invoke("ERROR: BepInEx payload not found at " + payload); return false; }
            log?.Invoke("Installing BepInEx...");
            CopyDir(payload, _gameDir, log);
            return File.Exists(Winhttp);
        }

        /// <summary>Install/update the co-op plugin into BepInEx/plugins.</summary>
        public bool InstallMod(string payloadRoot, Action<string>? log = null)
        {
            Directory.CreateDirectory(PluginsDir);
            var sources = new[]
            {
                Path.Combine(payloadRoot, "plugins"),
                RepoModBin(payloadRoot, "Release"),
                RepoModBin(payloadRoot, "Debug"),
            };
            var src = sources.FirstOrDefault(p => p != null && File.Exists(Path.Combine(p!, "CarMecaSim.Mp.dll")));
            if (src == null) { log?.Invoke("ERROR: built plugin (CarMecaSim.Mp.dll) not found in: " + string.Join("; ", sources)); return false; }

            log?.Invoke("Installing plugin from " + src);
            var dst = Path.Combine(PluginsDir, "CarMecaSim");
            Directory.CreateDirectory(dst);
            foreach (var name in ShipDlls)
            {
                var from = Path.Combine(src!, name);
                if (File.Exists(from)) File.Copy(from, Path.Combine(dst, name), overwrite: true);
            }
            EnsureAppId(log);
            return File.Exists(Path.Combine(dst, "CarMecaSim.Mp.dll"));
        }

        public void EnsureAppId(Action<string>? log = null)
        {
            if (!File.Exists(AppIdFile))
            {
                File.WriteAllText(AppIdFile, Constants.AppId.ToString());
                log?.Invoke("Wrote steam_appid.txt (" + Constants.AppId + ")");
            }
        }

        public void Uninstall(bool removeLoader, Action<string>? log = null)
        {
            var pluginDir = Path.Combine(PluginsDir, "CarMecaSim");
            if (Directory.Exists(pluginDir)) { Directory.Delete(pluginDir, true); log?.Invoke("Removed co-op plugin."); }
            if (File.Exists(ModDll)) File.Delete(ModDll); // legacy flat layout

            if (removeLoader)
            {
                foreach (var m in new[] { "winhttp.dll", "BepInEx", "dotnet", "doorstop_config.ini", ".doorstop_version", ".doorstop_version.txt" })
                {
                    var p = Path.Combine(_gameDir, m);
                    if (File.Exists(p)) File.Delete(p);
                    else if (Directory.Exists(p)) Directory.Delete(p, true);
                }
                log?.Invoke("Removed BepInEx. Game restored to vanilla.");
            }
        }

        private static string? RepoModBin(string payloadRoot, string config)
        {
            var repo = Directory.GetParent(payloadRoot)?.FullName; // payloadRoot(dev) = <repo>/tools
            if (repo == null) return null;
            return Path.Combine(repo, "mod", "bin", config, "net6.0");
        }

        private static void CopyDir(string src, string dst, Action<string>? log)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
            foreach (var d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)), log);
        }
    }
}
