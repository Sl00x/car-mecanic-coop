using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CarMecaSim.Launcher
{
    /// <summary>
    /// One-click updater: reads the latest GitHub release of the mod and replaces the installed
    /// plugin DLLs. Public-repo downloads need no auth. Repo is "owner/name".
    /// Override at runtime with the CMS_UPDATE_REPO env var (handy before the repo name is baked in).
    /// </summary>
    public static class GithubUpdater
    {
        // The mod's GitHub repo (releases are pulled from here).
        public const string DefaultRepo = "Sl00x/car-mecanic-coop";

        public static string Repo =>
            Environment.GetEnvironmentVariable("CMS_UPDATE_REPO") is { Length: > 0 } r ? r : DefaultRepo;

        public sealed class Asset { public string Name = ""; public string Url = ""; }
        public sealed class ReleaseInfo
        {
            public string Tag = "";
            public string Current = "";
            public List<Asset> Assets = new();
            public bool Configured;
            public bool HasUpdate;
            public string Error = "";
        }

        private static readonly string[] PluginDlls =
            { "CarMecaSim.Mp.dll", "CarMecaSim.Shared.dll", "Steamworks.NET.dll" };

        // Non-DLL data assets that also live next to the mod (the ripped mechanic model).
        private static readonly string[] DataAssets = { "mechanic.cmch" };

        private static HttpClient Http()
        {
            var h = new HttpClient();
            h.DefaultRequestHeaders.UserAgent.ParseAdd("CarMecaSim-Launcher");
            h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            h.Timeout = TimeSpan.FromSeconds(30);
            return h;
        }

        public static async Task<ReleaseInfo> CheckAsync(string gameDir)
        {
            var info = new ReleaseInfo { Current = InstalledVersion(gameDir) };
            if (Repo.Contains("owner/") || !Repo.Contains('/'))
            { info.Error = "Dépôt de mise à jour non configuré."; return info; }
            info.Configured = true;
            try
            {
                using var http = Http();
                var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                info.Tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
                if (root.TryGetProperty("assets", out var assets))
                    foreach (var a in assets.EnumerateArray())
                        info.Assets.Add(new Asset
                        {
                            Name = a.GetProperty("name").GetString() ?? "",
                            Url = a.GetProperty("browser_download_url").GetString() ?? ""
                        });
                info.HasUpdate = !string.IsNullOrEmpty(info.Tag) &&
                                 !string.Equals(info.Tag, info.Current, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) { info.Error = e.Message; }
            return info;
        }

        public static async Task<bool> UpdateAsync(string gameDir, ReleaseInfo info, Action<string> log)
        {
            var dst = Path.Combine(gameDir, "BepInEx", "plugins", "CarMecaSim");
            Directory.CreateDirectory(dst);

            // Prefer individual plugin DLL assets; fall back to a plugins.zip asset if present.
            var dllAssets = info.Assets.Where(a => PluginDlls.Contains(a.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            var zipAsset = info.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            using var http = Http();
            try
            {
                if (dllAssets.Count > 0)
                {
                    foreach (var a in dllAssets)
                    {
                        log($"Téléchargement {a.Name}…");
                        var bytes = await http.GetByteArrayAsync(a.Url);
                        await File.WriteAllBytesAsync(Path.Combine(dst, a.Name), bytes);
                    }
                    // Optional data assets (the mechanic AssetBundle), when the release ships them.
                    foreach (var a in info.Assets.Where(a => DataAssets.Contains(a.Name, StringComparer.OrdinalIgnoreCase)))
                    {
                        log($"Téléchargement {a.Name} (personnages 3D)…");
                        var bytes = await http.GetByteArrayAsync(a.Url);
                        await File.WriteAllBytesAsync(Path.Combine(dst, a.Name), bytes);
                    }
                }
                else if (zipAsset != null)
                {
                    log($"Téléchargement {zipAsset.Name}…");
                    var tmp = Path.Combine(Path.GetTempPath(), "cms_update.zip");
                    await File.WriteAllBytesAsync(tmp, await http.GetByteArrayAsync(zipAsset.Url));
                    var ex = Path.Combine(Path.GetTempPath(), "cms_update");
                    if (Directory.Exists(ex)) Directory.Delete(ex, true);
                    System.IO.Compression.ZipFile.ExtractToDirectory(tmp, ex);
                    foreach (var dll in Directory.GetFiles(ex, "*.dll", SearchOption.AllDirectories))
                        if (PluginDlls.Contains(Path.GetFileName(dll), StringComparer.OrdinalIgnoreCase))
                            File.Copy(dll, Path.Combine(dst, Path.GetFileName(dll)), true);
                }
                else { log("Aucun plugin trouvé dans la release."); return false; }

                log($"Mis à jour en v{info.Tag}.");
                return true;
            }
            catch (Exception e) { log("Échec mise à jour : " + e.Message); return false; }
        }

        public static string InstalledVersion(string gameDir)
        {
            try
            {
                var dll = Path.Combine(gameDir, "BepInEx", "plugins", "CarMecaSim", "CarMecaSim.Mp.dll");
                if (!File.Exists(dll)) return "—";
                var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(dll).ProductVersion ?? "—";
                var plus = v.IndexOf('+'); if (plus > 0) v = v.Substring(0, plus);   // strip git hash
                return v;
            }
            catch { return "—"; }
        }
    }
}
