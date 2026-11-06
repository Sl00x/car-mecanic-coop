using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using CarMecaSim.Shared;

namespace CarMecaSim.Launcher
{
    /// <summary>Finds the Car Mechanic Simulator 2021 install directory.</summary>
    public static class GameLocator
    {
        /// <summary>Returns the game directory, or null if not found. Tries registry, then all Steam libraries.</summary>
        public static string? Find()
        {
            foreach (var dir in Candidates())
            {
                if (IsGameDir(dir)) return dir;
            }
            return null;
        }

        public static bool IsGameDir(string? dir) =>
            !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir!, Constants.GameExeName));

        private static IEnumerable<string> Candidates()
        {
            var steam = FindSteamRoot();
            if (steam != null)
            {
                foreach (var lib in SteamLibraryFolders(steam))
                {
                    yield return Path.Combine(lib, "steamapps", "common", Constants.GameFolderName);
                }
            }
            // Common fallbacks
            yield return @"C:\Program Files (x86)\Steam\steamapps\common\" + Constants.GameFolderName;
        }

        private static string? FindSteamRoot()
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                    .OpenSubKey(@"Software\Valve\Steam");
                var p = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(p)) return p!.Replace('/', '\\');
            }
            catch { /* ignore */ }

            foreach (var guess in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" })
                if (Directory.Exists(guess)) return guess;
            return null;
        }

        /// <summary>Parses libraryfolders.vdf to enumerate every Steam library root.</summary>
        private static IEnumerable<string> SteamLibraryFolders(string steamRoot)
        {
            yield return steamRoot;
            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) yield break;

            string text;
            try { text = File.ReadAllText(vdf); } catch { yield break; }

            // Minimal VDF scan: pull each "path"  "<dir>" pair.
            foreach (var line in text.Split('\n'))
            {
                var t = line.Trim();
                if (!t.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = t.Split('"').Where(s => s.Length > 0).ToArray();
                if (parts.Length >= 2)
                {
                    var path = parts[^1].Replace(@"\\", @"\").Trim();
                    if (Directory.Exists(path)) yield return path;
                }
            }
        }
    }
}
