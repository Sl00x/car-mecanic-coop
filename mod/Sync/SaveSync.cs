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
                int inGarage = -1;
                try { inGarage = pd.carsInGarage != null ? pd.carsInGarage.Length : -1; } catch { }
                CarMecaPlugin.Logger?.LogInfo($"[save] serialized host profile '{SafeName(pd)}': {n} bytes, carsInGarage={inGarage}");
                return managed;
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[save] serialize failed: " + e.Message); return null; }
        }

        private static bool _busy;

        /// <summary>Client: overwrite the in-memory profile with the host's, apply its data, and
        /// explicitly respawn the garage cars (LoadInstant doesn't re-spawn cars when we're already
        /// standing in the garage — car spawning is normally tied to a scene load).</summary>
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
                if (pd == null) { CarMecaPlugin.Logger?.LogWarning("[save] CurrentProfileData is null"); return; }

                var arr = new Il2CppStructArray<byte>(data);
                pd.DeserializeFromBytes(ref arr);
                int inGarage = -1;
                try { inGarage = pd.carsInGarage != null ? pd.carsInGarage.Length : -1; } catch { }
                CarMecaPlugin.Logger?.LogInfo($"[save] deserialized host profile '{SafeName(pd)}' ({data.Length} bytes), carsInGarage={inGarage}");

                try { gdm.LoadInstant(true); }          // apply money / inventory / jobs / upgrades
                catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[save] LoadInstant threw: " + e.Message); }

                RespawnGarage(gdm, pd);                 // re-spawn the cars themselves (with full config)
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[save] apply failed: " + e); }
            finally { _busy = false; }
        }

        /// <summary>Delete each in-scene car and reload it from the (now host's) profile by place,
        /// using LoadCarFromFile — the real loader that restores a car's saved config.</summary>
        private static void RespawnGarage(GameDataManager gdm, ProfileData pd)
        {
            var gm = CarRefs.Mgr();
            if (gm == null) { CarMecaPlugin.Logger?.LogWarning("[save] respawn: no GameManager"); return; }
            Il2CppSystem.Collections.Generic.List<CarLoader> loaders;
            try { loaders = gm.CarLoadersInScene; } catch { loaders = null; }
            if (loaders == null) { CarMecaPlugin.Logger?.LogWarning("[save] respawn: no CarLoadersInScene"); return; }

            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<NewCarData> garage = null;
            try { garage = pd.carsInGarage; } catch { }

            int placesLoaded = 0;
            var places = new System.Text.StringBuilder();
            for (int i = 0; i < loaders.Count; i++)
            {
                var cl = loaders[i];
                if (cl == null) continue;
                int place = -1;
                try { place = cl.GetPlaceNo(); } catch { }
                bool hasCar = garage != null && place >= 0 && place < garage.Length && garage[place] != null;
                places.Append(place).Append(hasCar ? "* " : " ");
                try
                {
                    if (cl.IsCarLoaded()) cl.DeleteCar();        // clear whatever the client had
                    if (hasCar) { cl.StartCoroutine(cl.LoadCarFromFile(place, false)); placesLoaded++; }
                }
                catch (Exception e) { CarMecaPlugin.Logger?.LogWarning($"[save] respawn place {place}: {e.Message}"); }
            }
            CarMecaPlugin.Logger?.LogInfo($"[save] respawn issued for {placesLoaded}/{loaders.Count} loaders; places=[{places.ToString().Trim()}]");
        }

        private static string SafeName(ProfileData pd) { try { return pd.Name ?? "?"; } catch { return "?"; } }
    }
}
