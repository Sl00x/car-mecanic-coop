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
        /// <summary>Serialize the host's current profile to a managed byte array (null on failure).
        /// The in-memory CurrentProfileData.SerializeToBytes() comes out nearly empty (blank profile name,
        /// no cars) because the live garage/car state is only gathered into the profile by the game's own
        /// save flow. So we trigger the game's real SaveGame() (which gathers + writes the profile file)
        /// and send the actual save-file bytes — the exact native format the client already deserializes.</summary>
        public static byte[] SerializeHost()
        {
            var gdm = CarRefs.Gdm();
            if (gdm == null) return null;
            try
            {
                // NOTE: triggering a fresh game save first (so the file reflects unsaved live changes) is
                // a follow-up — SaveGame() lives on a game type we haven't bound yet. For now we send the
                // last on-disk profile, which already contains the full garage/cars (the whole point).
                bool saved = false;
                var path = SaveFilePath();
                if (path != null && System.IO.File.Exists(path))
                {
                    byte[] bytes = null;
                    try { bytes = System.IO.File.ReadAllBytes(path); } catch (Exception re) { CarMecaPlugin.Logger?.LogWarning("[save] read file failed: " + re.Message); }
                    if (bytes != null && bytes.Length > 64)
                    {
                        CarMecaPlugin.Logger?.LogInfo($"[save] host profile file: {bytes.Length} bytes (freshSave={saved}) <- {path}");
                        return bytes;
                    }
                }
                else CarMecaPlugin.Logger?.LogWarning("[save] profile file missing: " + (path ?? "<null>"));

                // Fallback: the old in-memory path (known to be incomplete, but better than nothing).
                var pd = gdm.CurrentProfileData;
                if (pd == null) return null;
                try { pd.Init(); } catch { }
                var arr = pd.SerializeToBytes();
                if (arr == null) return null;
                var managed = new byte[arr.Length];
                for (int i = 0; i < arr.Length; i++) managed[i] = arr[i];
                CarMecaPlugin.Logger?.LogWarning($"[save] FALLBACK in-memory serialize: {managed.Length} bytes (file path failed)");
                return managed;
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[save] serialize failed: " + e.Message); return null; }
        }

        /// <summary>Path to the active profile save file (Unity persistentDataPath/Save/profile0.cms21b).</summary>
        private static string SaveFilePath()
        {
            try { return System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "Save", "profile0.cms21b"); }
            catch { return null; }
        }


        private static bool _busy;

        /// <summary>Client: overwrite the in-memory profile with the host's and sync the garage.
        /// On the INITIAL snapshot (delta=false) we also LoadInstant the money/inventory/jobs. On a
        /// DELTA (host loaded/removed a car mid-session) we only touch the changed slots and leave
        /// economy + cars-in-progress alone, so working on a car isn't disrupted.</summary>
        public static void ApplyHost(byte[] data, bool delta)
        {
            var s = CarMecaPlugin.Session;
            if (s == null || s.IsHost || data == null || data.Length == 0 || _busy) return;
            var gdm = CarRefs.Gdm();
            if (gdm == null) { CarMecaPlugin.Logger?.LogWarning("[save] no GameDataManager to apply"); return; }
            _busy = true;
            try
            {
                var pd = gdm.CurrentProfileData;
                if (pd == null) { CarMecaPlugin.Logger?.LogWarning("[save] CurrentProfileData is null"); return; }

                var arr = new Il2CppStructArray<byte>(data);
                pd.DeserializeFromBytes(ref arr);
                int inGarage = -1;
                try { inGarage = pd.carsInGarage != null ? pd.carsInGarage.Length : -1; } catch { }
                CarMecaPlugin.Logger?.LogInfo($"[save] {(delta ? "delta" : "initial")} snapshot: profile '{SafeName(pd)}' ({data.Length} bytes), carsInGarage={inGarage}");

                if (!delta)
                {
                    try { gdm.LoadInstant(true); }      // apply money / inventory / jobs / upgrades (initial only)
                    catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[save] LoadInstant threw: " + e.Message); }
                }

                RespawnGarage(pd);                      // reload only the slots whose car changed
                if (!delta) s.MarkSnapshotApplied();    // stop re-requesting; don't re-tear-down on scene dips
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[save] apply failed: " + e); }
            finally { _busy = false; }
        }

        private static string CarNameAt(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<NewCarData> garage, int place)
        {
            try
            {
                if (garage == null || place < 0 || place >= garage.Length) return "";
                var cd = garage[place];
                if (cd == null) return "";
                var n = cd.carToLoad;
                return string.IsNullOrEmpty(n) ? "" : n;
            }
            catch { return ""; }
        }

        private static string CurrentCar(CarLoader cl)
        {
            try { return (cl != null && cl.IsCarLoaded()) ? (cl.carToLoad ?? "") : ""; } catch { return ""; }
        }

        /// <summary>Reload ONLY the garage slots whose car differs from the host's profile. Slots that
        /// already match are left untouched — so a car a player is working on is never torn down.
        /// Uses LoadCarFromFile(place) — the real loader that restores a car's full saved config.</summary>
        private static void RespawnGarage(ProfileData pd)
        {
            var gm = CarRefs.Mgr();
            if (gm == null) { CarMecaPlugin.Logger?.LogWarning("[save] respawn: no GameManager"); return; }
            Il2CppSystem.Collections.Generic.List<CarLoader> loaders;
            try { loaders = gm.CarLoadersInScene; } catch { loaders = null; }
            if (loaders == null) { CarMecaPlugin.Logger?.LogWarning("[save] respawn: no CarLoadersInScene"); return; }

            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<NewCarData> garage = null;
            try { garage = pd.carsInGarage; } catch { }

            int changed = 0;
            var log = new System.Text.StringBuilder();
            for (int i = 0; i < loaders.Count; i++)
            {
                var cl = loaders[i];
                if (cl == null) continue;
                int place = -1;
                try { place = cl.GetPlaceNo(); } catch { }

                string want = CarNameAt(garage, place);
                string cur = CurrentCar(cl);
                if (want == cur) continue;              // slot already matches — don't disturb it

                try
                {
                    if (cl.IsCarLoaded()) cl.DeleteCar();
                    if (want != "") cl.StartCoroutine(cl.LoadCarFromFile(place, false));
                    changed++;
                    log.Append($"[{place}:'{cur}'->'{want}'] ");
                }
                catch (Exception e) { CarMecaPlugin.Logger?.LogWarning($"[save] respawn place {place}: {e.Message}"); }
            }
            CarMecaPlugin.Logger?.LogInfo($"[save] garage synced: {changed} slot(s) changed {log.ToString().Trim()}");
        }

        private static string SafeName(ProfileData pd) { try { return pd.Name ?? "?"; } catch { return "?"; } }
    }
}
