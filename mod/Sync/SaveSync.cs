using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;

namespace CarMecaSim.Mp.Sync
{
    /// <summary>
    /// Full save snapshot: the host serializes its profile (ProfileData.SerializeToBytes) and the
    /// joining client deserializes + reloads it, so the client's garage, cars, jobs and money become
    /// an exact NATIVE copy of the host's. This is the clean "shared garage" — no per-car/job replication.
    /// </summary>
    public static class SaveSync
    {
        /// <summary>Serialize the host's current profile to a managed byte array (null on failure).</summary>
        public static byte[] SerializeHost()
        {
            var gdm = CarRefs.Gdm();
            if (gdm == null) return null;
            try
            {
                var pd = gdm.CurrentProfileData;
                if (pd == null) return null;
                // Init() gathers the live game state (garage, inventory, jobs, economy) into the
                // ProfileData — the same "collect" step the game runs before writing a save to disk.
                try { pd.Init(); }
                catch (Exception ie) { CarMecaPlugin.Logger?.LogWarning("[save] Init() skipped: " + ie.Message); }
                var arr = pd.SerializeToBytes();
                if (arr == null) return null;
                int n = arr.Length;
                var managed = new byte[n];
                for (int i = 0; i < n; i++) managed[i] = arr[i];   // Il2CppStructArray<byte>: fast direct indexing
                CarMecaPlugin.Logger?.LogInfo($"[save] serialized host profile: {n} bytes");
                return managed;
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[save] serialize failed: " + e.Message); return null; }
        }

        private static bool _busy;

        /// <summary>Client: overwrite the in-memory profile with the host's and reload the garage.</summary>
        public static void ApplyHost(byte[] data)
        {
            var s = CarMecaPlugin.Session;
            if (s == null || s.IsHost || data == null || data.Length == 0 || _busy) return;
            var gdm = CarRefs.Gdm();
            if (gdm == null) { CarMecaPlugin.Logger?.LogWarning("[save] no GameDataManager to apply"); return; }
            _busy = true;
            try
            {
                var pd = gdm.CurrentProfileData;
                if (pd == null) { CarMecaPlugin.Logger?.LogWarning("[save] no CurrentProfileData"); return; }
                var arr = new Il2CppStructArray<byte>(data);
                pd.DeserializeFromBytes(ref arr);
                gdm.LoadInstant(true);                  // reload the garage with the host's data
                CarMecaPlugin.Logger?.LogInfo($"[save] applied host save ({data.Length} bytes) + reloaded garage");
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[save] apply failed: " + e.Message); }
            finally { _busy = false; }
        }
    }
}
