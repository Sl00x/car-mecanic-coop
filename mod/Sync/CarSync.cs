using System;
using System.Collections.Generic;
using UnityEngine;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;

namespace CarMecaSim.Mp.Sync
{
    /// <summary>
    /// Host-authoritative SHARED GARAGE: the host broadcasts the full state of its garage (every
    /// car slot + the car on it). Clients mirror it — unloading their own cars and loading the
    /// host's, so everyone sees the same vehicles. (v1 — car model per slot; part state syncs via PartSync.)
    /// </summary>
    public static class CarSync
    {
        public const byte OpGarage = 2;

        public static bool Applying;   // true while applying the host's garage (don't re-broadcast)

        private static MpSession S => CarMecaPlugin.Session;
        private static bool Active => S != null && S.InLobby && GameRefs.InGameSession;
        private static GameManager _gm;
        private static float _lastBroadcast;

        private static GameManager Mgr()
        {
            try { if (_gm == null) _gm = UnityEngine.Object.FindObjectOfType<GameManager>(); } catch { }
            return _gm;
        }

        private static Il2CppSystem.Collections.Generic.List<CarLoader> Loaders()
        {
            try { var gm = Mgr(); return gm != null ? gm.CarLoadersInScene : null; } catch { return null; }
        }

        private static string CarOn(CarLoader cl)
        {
            try { return (cl != null && cl.IsCarLoaded()) ? (cl.carToLoad ?? "") : ""; } catch { return ""; }
        }

        // ---- host side ----
        /// <summary>Harmony postfix on CarLoader.LoadCar: the host re-broadcasts its garage.</summary>
        public static void OnLocalLoadCar(CarLoader cl, string name)
        {
            if (Applying || !Active || !S.IsHost) return;
            BroadcastGarage();
        }

        public static void OnPeerJoined() { if (Active && S.IsHost) BroadcastGarage(); }

        public static void Tick()
        {
            if (Active && S.IsHost && Time.unscaledTime - _lastBroadcast > 3f) BroadcastGarage();
        }

        public static void BroadcastGarage()
        {
            if (!Active || !S.IsHost) return;
            var loaders = Loaders();
            if (loaders == null) return;
            var entries = new List<(int, string)>();
            for (int i = 0; i < loaders.Count; i++)
                entries.Add((i, CarOn(loaders[i])));
            S.SendGarage(entries);
            _lastBroadcast = Time.unscaledTime;
        }

        // ---- client side ----
        public static void ApplyGarage(List<(int, string)> entries)
        {
            if (!Active || S.IsHost) return;   // only clients mirror the host
            var loaders = Loaders();
            if (loaders == null) return;
            Applying = true;
            try
            {
                foreach (var (i, name) in entries)
                {
                    if (i < 0 || i >= loaders.Count) continue;
                    var cl = loaders[i];
                    if (cl == null) continue;
                    var cur = CarOn(cl);
                    if (cur == name) continue;             // slot already matches the host
                    try
                    {
                        if (string.IsNullOrEmpty(name))
                        {
                            if (cl.IsCarLoaded()) cl.DeleteCar();          // host slot empty -> unload ours
                        }
                        else
                        {
                            if (cl.IsCarLoaded()) cl.DeleteCar();          // replace our car with the host's
                            cl.StartCoroutine(cl.LoadCar(name));
                        }
                        CarMecaPlugin.Logger?.LogInfo($"[garage] slot {i}: '{cur}' -> '{name}'");
                    }
                    catch (Exception e) { CarMecaPlugin.Logger?.LogWarning($"[garage] slot {i} failed: {e.Message}"); }
                }
            }
            finally { Applying = false; }
        }
    }
}
