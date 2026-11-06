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
        public bool MelonLoaderInstalled;
        public bool InteropGenerated;
        public bool ModInstalled;
        public string? InstalledModVersion;
        public bool SteamAppIdFile;

        public bool FullyReady => GameFound && MelonLoaderInstalled && InteropGenerated && ModInstalled;
    }

    /// <summary>Installs/verifies/removes MelonLoader + the co-op mod in the game install.</summary>
    public sealed class ModInstaller
    {
        private readonly string _gameDir;
        public ModInstaller(string gameDir) => _gameDir = gameDir;

        private string MlVersionDll => Path.Combine(_gameDir, "version.dll");
        private string MlDir        => Path.Combine(_gameDir, "MelonLoader");
        private string InteropDir   => Path.Combine(MlDir, "Il2CppAssemblies");
        private string ModsDir      => Path.Combine(_gameDir, "Mods");
        private string ModDll       => Path.Combine(ModsDir, "CarMecaSim.Mp.dll");
        private string AppIdFile    => Path.Combine(_gameDir, "steam_appid.txt");

        public InstallStatus GetStatus()
        {
            var s = new InstallStatus { GameDir = _gameDir, GameFound = GameLocator.IsGameDir(_gameDir) };
            s.MelonLoaderInstalled = File.Exists(MlVersionDll) && Directory.Exists(MlDir);
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

        /// <summary>Locate the install payload (MelonLoader + mod). Works from a shipped layout or the dev repo.</summary>
        public static string? FindPayloadRoot()
        {
            var baseDir = AppContext.BaseDirectory;
            var shipped = Path.Combine(baseDir, "payload");
            if (Directory.Exists(Path.Combine(shipped, "MelonLoader_x64"))) return shipped;

            // Dev fallback: climb to repo root (contains CarMecaSim.sln)
            var dir = new DirectoryInfo(baseDir);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CarMecaSim.sln")))
                dir = dir.Parent;
            if (dir != null) return Path.Combine(dir.FullName, "tools"); // tools/MelonLoader_x64 lives here
            return null;
        }

        /// <summary>Install MelonLoader if missing. Returns true if present after the call.</summary>
        public bool EnsureMelonLoader(string payloadRoot, Action<string>? log = null)
        {
            if (File.Exists(MlVersionDll) && Directory.Exists(MlDir)) { log?.Invoke("MelonLoader already installed."); return true; }
            var mlPayload = Path.Combine(payloadRoot, "MelonLoader_x64");
            if (!Directory.Exists(mlPayload)) { log?.Invoke("ERROR: MelonLoader payload not found at " + mlPayload); return false; }
            log?.Invoke("Installing MelonLoader...");
            CopyDir(mlPayload, _gameDir, log);
            return File.Exists(MlVersionDll);
        }

        /// <summary>Install/update the co-op mod dll and its managed dependencies into Mods/.</summary>
        public bool InstallMod(string payloadRoot, Action<string>? log = null)
        {
            Directory.CreateDirectory(ModsDir);
            // Mod dll + deps can come from payload/Mods, or dev: mod/bin/*/net6.0
            var sources = new[]
            {
                Path.Combine(payloadRoot, "Mods"),
                RepoModBin(payloadRoot, "Release"),
                RepoModBin(payloadRoot, "Debug"),
            };
            var src = sources.FirstOrDefault(p => p != null && File.Exists(Path.Combine(p!, "CarMecaSim.Mp.dll")));
            if (src == null) { log?.Invoke("ERROR: built mod (CarMecaSim.Mp.dll) not found in any of: " + string.Join("; ", sources)); return false; }

            log?.Invoke("Installing mod from " + src);
            foreach (var dll in Directory.GetFiles(src, "*.dll"))
            {
                // Skip interop/MelonLoader assemblies (already in game); ship only the mod + Steamworks.NET.
                var name = Path.GetFileName(dll);
                if (name.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase)) continue;
                if (name is "MelonLoader.dll" or "0Harmony.dll") continue;
                File.Copy(dll, Path.Combine(ModsDir, name), overwrite: true);
            }
            EnsureAppId(log);
            return File.Exists(ModDll);
        }

        public void EnsureAppId(Action<string>? log = null)
        {
            if (!File.Exists(AppIdFile))
            {
                File.WriteAllText(AppIdFile, Constants.AppId.ToString());
                log?.Invoke("Wrote steam_appid.txt (" + Constants.AppId + ")");
            }
        }

        public void Uninstall(bool removeMelonLoader, Action<string>? log = null)
        {
            if (File.Exists(ModDll)) { File.Delete(ModDll); log?.Invoke("Removed mod dll."); }
            var sw = Path.Combine(ModsDir, "Steamworks.NET.dll");
            if (File.Exists(sw)) File.Delete(sw);
            if (removeMelonLoader)
            {
                foreach (var m in new[] { "version.dll", "MelonLoader", "dobby.dll", "Latest.log", "Logs" })
                {
                    var p = Path.Combine(_gameDir, m);
                    if (File.Exists(p)) File.Delete(p);
                    else if (Directory.Exists(p)) Directory.Delete(p, true);
                }
                log?.Invoke("Removed MelonLoader. Game restored to vanilla.");
            }
        }

        private static string? RepoModBin(string payloadRoot, string config)
        {
            // payloadRoot in dev = <repo>/tools ; mod bin = <repo>/mod/bin/<config>/net6.0
            var repo = Directory.GetParent(payloadRoot)?.FullName;
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
