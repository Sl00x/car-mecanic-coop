using System;
using System.Collections.Generic;
using System.Linq;
using CarMecaSim.Shared;

namespace CarMecaSim.Launcher
{
    /// <summary>Headless mode so the launcher is scriptable and testable without the GUI.</summary>
    internal static class Cli
    {
        public static int Run(string[] args)
        {
            var cmd = args[0].TrimStart('-').ToLowerInvariant();
            var opts = ParseOpts(args.Skip(1));

            var gameDir = opts.GetValueOrDefault("game") ?? GameLocator.Find();
            if (!GameLocator.IsGameDir(gameDir) && cmd != "help")
            {
                Console.WriteLine("ERROR: Car Mechanic Simulator 2021 not found. Pass --game \"<dir>\".");
                return 2;
            }

            switch (cmd)
            {
                case "status":   return CmdStatus(gameDir!);
                case "setup":    return CmdSetup(gameDir!);
                case "host":     return CmdPlay(gameDir!, MpRole.Host, opts);
                case "join":     return CmdPlay(gameDir!, MpRole.Join, opts, args);
                case "update":   return CmdUpdate(gameDir!);
                case "uninstall":return CmdUninstall(gameDir!, opts.ContainsKey("all"));
                case "help":     default: return CmdHelp();
            }
        }

        private static int CmdStatus(string gameDir)
        {
            var s = new ModInstaller(gameDir).GetStatus();
            Console.WriteLine($"Game dir            : {s.GameDir}");
            Console.WriteLine($"Game found          : {Mark(s.GameFound)}");
            Console.WriteLine($"BepInEx             : {Mark(s.LoaderInstalled)}");
            Console.WriteLine($"Interop generated   : {Mark(s.InteropGenerated)}");
            Console.WriteLine($"Co-op mod installed : {Mark(s.ModInstalled)}  {s.InstalledModVersion}");
            Console.WriteLine($"steam_appid.txt     : {Mark(s.SteamAppIdFile)}");
            Console.WriteLine($"=> {(s.FullyReady ? "READY" : "NOT READY — run: --setup")}");
            return s.FullyReady ? 0 : 1;
        }

        private static int CmdSetup(string gameDir)
        {
            var inst = new ModInstaller(gameDir);
            var payload = ModInstaller.FindPayloadRoot();
            if (payload == null) { Console.WriteLine("ERROR: install payload not found."); return 3; }
            void Log(string m) => Console.WriteLine("  " + m);
            if (!inst.EnsureLoader(payload, Log)) { Console.WriteLine("BepInEx install FAILED."); return 4; }
            InteropPatcher.EnsurePatched(gameDir, Log);   // required for class injection on this game
            if (!inst.InstallMod(payload, Log)) { Console.WriteLine("Mod install FAILED (build the mod first)."); return 5; }
            inst.EnsureAppId(Log);
            return CmdStatus(gameDir);
        }

        private static int CmdPlay(string gameDir, MpRole role, Dictionary<string, string> opts, string[]? raw = null)
        {
            var cfg = new MpConfig { Role = role };
            if (opts.TryGetValue("name", out var n)) cfg.DisplayName = n;
            if (opts.TryGetValue("max", out var m) && int.TryParse(m, out var mi)) cfg.MaxPlayers = mi;
            if (opts.TryGetValue("type", out var t) && Enum.TryParse<MpLobbyType>(t, true, out var lt)) cfg.LobbyType = lt;
            if (role == MpRole.Join)
            {
                // lobby id is the first positional after "join", or --lobby
                var lobby = opts.GetValueOrDefault("lobby")
                            ?? raw?.Skip(1).FirstOrDefault(a => !a.StartsWith("-"));
                if (!ulong.TryParse(lobby, out var id)) { Console.WriteLine("ERROR: join needs a numeric lobby id."); return 6; }
                cfg.LobbyId = id;
            }
            var runner = new GameRunner(gameDir);
            var path = runner.WriteConfig(cfg);
            Console.WriteLine($"Wrote config ({cfg.Role}) -> {path}");
            runner.Launch(viaSteam: !opts.ContainsKey("direct"));
            Console.WriteLine("Launched.");
            return 0;
        }

        private static int CmdUpdate(string gameDir)
        {
            var info = GithubUpdater.CheckAsync(gameDir).GetAwaiter().GetResult();
            Console.WriteLine($"Repo      : {GithubUpdater.Repo}");
            Console.WriteLine($"Installed : v{info.Current}");
            if (!info.Configured) { Console.WriteLine("Repo not configured."); return 2; }
            if (!string.IsNullOrEmpty(info.Error)) { Console.WriteLine("Error: " + info.Error); return 3; }
            Console.WriteLine($"Latest    : v{info.Tag}");
            if (!info.HasUpdate) { Console.WriteLine("Already up to date."); return 0; }
            Console.WriteLine("Updating...");
            var ok = GithubUpdater.UpdateAsync(gameDir, info, m => Console.WriteLine("  " + m)).GetAwaiter().GetResult();
            return ok ? 0 : 4;
        }

        private static int CmdUninstall(string gameDir, bool all)
        {
            new ModInstaller(gameDir).Uninstall(all, m => Console.WriteLine("  " + m));
            Console.WriteLine(all ? "Removed mod + BepInEx." : "Removed co-op mod (BepInEx kept; pass --all to remove it too).");
            return 0;
        }

        private static int CmdHelp()
        {
            Console.WriteLine("""
                CarMecaSim Co-op Launcher
                  (no args)                  Open the GUI
                  --status                   Show install status
                  --setup                    Install/verify BepInEx + co-op mod
                  --host [--name N] [--max N] [--type friends|public|private] [--direct]
                  --join <lobbyId> [--name N] [--direct]
                  --update                   Download & install the latest plugin from GitHub
                  --uninstall [--all]        Remove mod (--all also removes BepInEx)
                  --game "<dir>"             Override game directory
                """);
            return 0;
        }

        private static string Mark(bool b) => b ? "[ OK ]" : "[  - ]";

        private static Dictionary<string, string> ParseOpts(IEnumerable<string> args)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var list = args.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].StartsWith("-")) continue;
                var key = list[i].TrimStart('-');
                var val = (i + 1 < list.Count && !list[i + 1].StartsWith("-")) ? list[++i] : "true";
                d[key] = val;
            }
            return d;
        }
    }
}
