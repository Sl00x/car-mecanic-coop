using System.Collections.Generic;
using UnityEngine;
using Steamworks;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;

namespace CarMecaSim.Mp.Sync
{
    /// <summary>
    /// Shared garage, host-authoritative, DATA-DRIVEN. The host serializes each occupied garage place's car
    /// (the game's own <c>NewCarData</c>, via <see cref="CarCodec"/>) and the client reproduces it by calling
    /// <c>CarLoader.LoadCarFromFile(NewCarData)</c> — no whole-profile deserialize, no <c>LoadInstant</c>, and the
    /// client's own save file is never touched. This replaces the fragile save-snapshot overwrite that threw
    /// "assertion failed" on a guest. Live mechanical/paint/openable edits still ride the existing PartState /
    /// PartAction packets, so a spawned car (in its saved state) converges to the host's live disassembly.
    /// Ported from CMS21-Together's CarSpawnManager (MIT).
    /// </summary>
    public static class CarSync
    {
        public const byte OpGarage = 2;   // legacy CarOp wire id (no longer emitted; handler is a no-op)

        // ops carried inside PacketType.CarData
        public const byte OpSpawn = 0, OpDelete = 1, OpClear = 2;

        /// <summary>True while we apply a remote car op, so our LoadCar/DeleteCar Harmony hooks don't re-broadcast.</summary>
        public static bool Applying;

        private static MpSession S => CarMecaPlugin.Session;
        private static bool Active => S != null && S.InLobby && GameRefs.InGameSession;

        // ---- host: places whose car changed and must be (re)captured once the load settles ----
        private struct Dirty { public float Due; public int Tries; }
        private static readonly Dictionary<int, Dirty> _dirty = new();

        // ---- client: car ops waiting for the garage to be ready to apply ----
        private struct PendOp { public byte Op; public int Place; public byte[] Bytes; }
        private static readonly Queue<PendOp> _pending = new();

        /// <summary>Harmony postfix on CarLoader.LoadCar(string) / DeleteCar(): the host's garage changed.
        /// name!=null → a car was loaded (capture it once loaded); name==null → a car was removed.</summary>
        public static void OnLocalLoadCar(CarLoader cl, string name)
        {
            // Always refresh part refs for the new car (local-player gameplay), even off-session.
            PartSync.ClearRemoved();
            CarPartSync.Invalidate();

            if (Applying) return;                 // this load/delete is us applying a remote op — don't echo
            if (!Active || !S.IsHost) return;

            int place = -1;
            try { place = cl != null ? cl.GetPlaceNo() : -1; } catch { }
            if (place < 0) return;

            if (name == null)
            {
                _dirty.Remove(place);
                S.SendCarDelete(place);
                CarMecaPlugin.Logger?.LogInfo($"[car] local DELETE place {place} -> broadcast");
            }
            else
            {
                // The load is a coroutine; carsInGarage isn't up to date yet. Capture after it settles (Tick).
                _dirty[place] = new Dirty { Due = Time.unscaledTime + 0.75f, Tries = 0 };
            }
        }

        /// <summary>Host -> a joining client: wipe their garage, then stream every occupied car as data.</summary>
        public static void SendCatchUpCars(CSteamID target)
        {
            if (S == null || !S.IsHost) return;
            var gm = CarRefs.Mgr();
            var pd = CarRefs.Gdm()?.CurrentProfileData;
            if (gm == null || pd == null) { CarMecaPlugin.Logger?.LogWarning("[car] catch-up: no GameManager/profile"); return; }
            Il2CppSystem.Collections.Generic.List<CarLoader> loaders;
            try { loaders = gm.CarLoadersInScene; } catch { loaders = null; }
            if (loaders == null) return;

            S.SendCarClearTo(target);
            int n = 0;
            for (int i = 0; i < loaders.Count; i++)
            {
                var cl = loaders[i];
                if (cl == null) continue;
                bool loaded = false; try { loaded = cl.IsCarLoaded(); } catch { }
                if (!loaded) continue;
                int place = -1; try { place = cl.GetPlaceNo(); } catch { }
                if (place < 0) continue;
                try { cl.SaveCarToFile(); } catch { }          // push live car into carsInGarage[place]
                var cd = CarCodec.CarDataAt(pd, place);
                var bytes = CarCodec.Serialize(cd);
                if (bytes == null || bytes.Length == 0) continue;
                S.SendCarSpawnTo(target, place, bytes);
                n++;
            }
            CarMecaPlugin.Logger?.LogInfo($"[car] catch-up: sent {n} car(s) to {S.Net.NameOf(target)}");
        }

        /// <summary>Client: a car op arrived — queue it (applied in Tick once the garage is ready).</summary>
        public static void ApplyRemote(byte op, int place, byte[] bytes)
        {
            _pending.Enqueue(new PendOp { Op = op, Place = place, Bytes = bytes });
        }

        /// <summary>Per-frame upkeep: host captures settled dirty places; client drains queued car ops.</summary>
        public static void Tick()
        {
            if (!Active) return;
            if (S.IsHost) TickHostCapture();
            else TickClientApply();
        }

        private static void TickHostCapture()
        {
            if (_dirty.Count == 0) return;
            var pd = CarRefs.Gdm()?.CurrentProfileData;
            float now = Time.unscaledTime;
            List<int> done = null;
            foreach (var kv in _dirty)
            {
                int place = kv.Key; var d = kv.Value;
                if (now < d.Due) continue;
                var cl = LoaderAtPlace(place);
                bool loaded = false; try { loaded = cl != null && cl.IsCarLoaded(); } catch { }
                if (!loaded)
                {
                    d.Tries++;
                    if (d.Tries > 20) { (done ??= new()).Add(place); }     // ~give up after a while
                    else { d.Due = now + 0.25f; _dirty[place] = d; }
                    continue;
                }
                try { cl.SaveCarToFile(); } catch { }
                var cd = CarCodec.CarDataAt(pd, place);
                var bytes = CarCodec.Serialize(cd);
                if (bytes != null && bytes.Length > 0)
                {
                    S.SendCarSpawn(place, bytes);
                    CarMecaPlugin.Logger?.LogInfo($"[car] captured place {place} '{CarCodec.CarNameAt(pd, place)}' ({bytes.Length}B) -> broadcast");
                }
                (done ??= new()).Add(place);
            }
            if (done != null) foreach (var p in done) _dirty.Remove(p);
        }

        private static void TickClientApply()
        {
            if (_pending.Count == 0) return;
            if (!GameRefs.InGarage) return;                 // wait until the garage scene is live
            var gm = CarRefs.Mgr();
            if (gm == null) return;

            Applying = true;
            try
            {
                while (_pending.Count > 0)
                {
                    var op = _pending.Dequeue();
                    if (op.Op == OpClear) { ClearAllCars(); continue; }
                    var cl = LoaderAtPlace(op.Place);
                    if (cl == null) { CarMecaPlugin.Logger?.LogWarning($"[car] apply: no loader at place {op.Place}"); continue; }
                    if (op.Op == OpDelete)
                    {
                        try { if (cl.IsCarLoaded()) cl.DeleteCar(); } catch (System.Exception e) { CarMecaPlugin.Logger?.LogWarning("[car] delete failed: " + e.Message); }
                        CarMecaPlugin.Logger?.LogInfo($"[car] applied DELETE place {op.Place}");
                    }
                    else if (op.Op == OpSpawn)
                    {
                        SpawnCar(cl, op.Place, op.Bytes);
                    }
                }
            }
            finally { Applying = false; }
        }

        private static void SpawnCar(CarLoader cl, int place, byte[] bytes)
        {
            var cd = CarCodec.Deserialize(bytes);
            if (cd == null) { CarMecaPlugin.Logger?.LogWarning($"[car] spawn place {place}: bad data"); return; }
            // Skip if the same car is already on this loader (don't tear down a car being worked on).
            try
            {
                if (cl.IsCarLoaded())
                {
                    string cur = null; try { cur = cl.carToLoad; } catch { }
                    string want = null; try { want = cd.carToLoad; } catch { }
                    if (!string.IsNullOrEmpty(cur) && cur == want)
                    { CarMecaPlugin.Logger?.LogInfo($"[car] place {place} already has '{cur}' — skip respawn"); return; }
                    cl.DeleteCar();
                }
            }
            catch { }
            try
            {
                bool job = false; int jobId = -1;
                try { job = cd.customerCar; jobId = cd.orderConnection; } catch { }
                if (job && jobId >= 0) { try { cl.SetCustomerCar(true, jobId); } catch { } }
                cl.StartCoroutine(cl.LoadCarFromFile(cd));
                CarMecaPlugin.Logger?.LogInfo($"[car] applied SPAWN place {place} '{SafeName(cd)}' job={job}:{jobId}");
            }
            catch (System.Exception e) { CarMecaPlugin.Logger?.LogWarning($"[car] spawn place {place} failed: " + e.Message); }
        }

        private static void ClearAllCars()
        {
            var gm = CarRefs.Mgr();
            Il2CppSystem.Collections.Generic.List<CarLoader> loaders;
            try { loaders = gm != null ? gm.CarLoadersInScene : null; } catch { loaders = null; }
            if (loaders == null) return;
            int n = 0;
            for (int i = 0; i < loaders.Count; i++)
            {
                var cl = loaders[i];
                if (cl == null) continue;
                try { if (cl.IsCarLoaded()) { cl.DeleteCar(); n++; } } catch { }
            }
            if (n > 0) CarMecaPlugin.Logger?.LogInfo($"[car] cleared {n} local car(s) before adopting host garage");
        }

        /// <summary>The CarLoader whose garage bay == place (loaded or empty), or null.</summary>
        private static CarLoader LoaderAtPlace(int place)
        {
            var gm = CarRefs.Mgr();
            Il2CppSystem.Collections.Generic.List<CarLoader> loaders;
            try { loaders = gm != null ? gm.CarLoadersInScene : null; } catch { loaders = null; }
            if (loaders == null) return null;
            for (int i = 0; i < loaders.Count; i++)
            {
                var cl = loaders[i];
                if (cl == null) continue;
                int p = -1; try { p = cl.GetPlaceNo(); } catch { }
                if (p == place) return cl;
            }
            return null;
        }

        private static string SafeName(NewCarData cd) { try { return cd.carToLoad ?? "?"; } catch { return "?"; } }

        // ---- legacy no-ops kept so existing callers compile ----
        public static void OnPeerJoined() { }
        public static void ApplyGarage(List<(int, string)> entries) { }
    }
}
