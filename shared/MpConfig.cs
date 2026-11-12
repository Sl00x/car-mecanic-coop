using System;
using System.IO;

namespace CarMecaSim.Shared
{
    /// <summary>Launch role chosen in the launcher and read by the mod at startup.</summary>
    public enum MpRole
    {
        /// <summary>Just launch; use the in-game overlay to host/join later.</summary>
        Menu = 0,
        /// <summary>Create a lobby and host on reaching the garage.</summary>
        Host = 1,
        /// <summary>Join an existing lobby (by LobbyId or Steam invite).</summary>
        Join = 2,
    }

    public enum MpLobbyType
    {
        Private = 0,
        FriendsOnly = 1,
        Public = 2,
    }

    /// <summary>
    /// The launcher writes this file; the mod reads it once on startup.
    /// Kept dependency-free (plain POCO) so both net6 (mod) and net8 (launcher) can share it.
    /// </summary>
    [Serializable]
    public sealed class MpConfig
    {
        public int SchemaVersion { get; set; } = 1;
        public MpRole Role { get; set; } = MpRole.Menu;
        public string DisplayName { get; set; } = Environment.UserName ?? "Mechanic";
        public ulong LobbyId { get; set; } = 0;           // for Join-by-id
        public int MaxPlayers { get; set; } = 4;
        public MpLobbyType LobbyType { get; set; } = MpLobbyType.FriendsOnly;
        public bool AutoStart { get; set; } = true;        // act on Role automatically on reaching garage
        public string ModVersion { get; set; } = Constants.ModVersion;

        /// <summary>TEST ONLY: when &gt; 0, the mod uses a local TCP loopback transport on
        /// 127.0.0.1:&lt;port&gt; instead of Steam P2P, so a headless peer (tools/FakePeer) can act as a
        /// second player on the same machine. 0 = normal Steam networking.</summary>
        public int LoopbackPort { get; set; } = 0;

        /// <summary>Where the config lives, relative to the game dir (BepInEx config convention).</summary>
        public static string RelativePath => Path.Combine("BepInEx", "config", "CarMecaSim", "mp_config.json");

        public static string FullPathFor(string gameDir) => Path.Combine(gameDir, RelativePath);
    }

    public static class Constants
    {
        public const uint AppId = 1190000;          // Car Mechanic Simulator 2021 (retail)
        public const uint AppIdPrologueDemo = 1585850;
        public const string ModName = "CarMecaSim.Mp";
        public const string ModVersion = "0.1.0";
        public const byte SaveVersion = 5;          // ProfileData.CurrentSaveVersion (must match host/client)

        public const string GameExeName = "Car Mechanic Simulator 2021.exe";
        public const string GameFolderName = "Car Mechanic Simulator 2021";
    }
}
