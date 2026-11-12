using System;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;

namespace CarMecaSim.Mp.Sync
{
    /// <summary>
    /// Real-time shared-car co-op: replicates taking parts on/off the car across players. In CMS2021 the
    /// actual removal entry point is CarLoader.TakeOffCarPart(string key) (NOT PartScript.ActionUnMount),
    /// and the key is a clean machine-stable string ("car_vallsen2040-hood"). We broadcast the key and the
    /// peer replays it instantly via CarLoader.TakeOffCarPartFromSave / TakeOnCarPartFromSave — so the same
    /// part comes off / goes on the other player's identical car, live.
    /// </summary>
    public static class PartSync
    {
        // action ids on the wire: 0=mount(install) 1=unmount(remove) 2=bolt 3=release-lock
        public const byte ActMount = 0, ActUnmount = 1, ActBolt = 2, ActRelease = 3;

        /// <summary>True while applying a remote action, so our Harmony hooks don't re-broadcast it.</summary>
        public static bool Applying;

        private static MpSession S => CarMecaPlugin.Session;
        private static bool Active => S != null && S.InLobby && GameRefs.InGameSession;

        // The Void and IEnumerator TakeOffCarPart overloads can both fire for one removal — dedup by key.
        private static readonly Dictionary<string, float> _lastSent = new();
        private static CarLoader _lastCar;   // the car the local player last worked on (reliable handle)

        // Live mechanical-state delta for the current car: which parts are OFF beyond what the save holds.
        // This is what a late-joiner must replay so their car matches the host's CURRENT disassembly, which
        // the save snapshot alone cannot express (it only has the last-saved state). Scoped to the car on
        // the lift; cleared when a different car is loaded (see ClearRemoved via CarSync).
        private static readonly HashSet<string> _removed = new();

        /// <summary>Local player took a part off the car (Harmony postfix) -> record + broadcast to peers.</summary>
        public static void OnLocalTakeOff(CarLoader cl, string partKey)
        {
            if (cl != null) _lastCar = cl;
            if (Applying || !Active || string.IsNullOrEmpty(partKey)) return;
            float now = Time.unscaledTime;
            if (_lastSent.TryGetValue(partKey, out var t) && now - t < 0.5f) return;   // collapse double-fire
            _lastSent[partKey] = now;
            _removed.Add(partKey);
            S.SendPartAction(ActUnmount, partKey, -1);
            CarMecaPlugin.Logger?.LogInfo($"[part] local take-off '{partKey}' -> broadcast (session removed={_removed.Count})");
        }

        /// <summary>A new car was loaded/removed on the lift — the session part-delta no longer applies.</summary>
        public static void ClearRemoved()
        {
            if (_removed.Count > 0) CarMecaPlugin.Logger?.LogInfo($"[part] cleared {_removed.Count} session removal(s) (car changed)");
            _removed.Clear(); _lastSent.Clear();
        }

        /// <summary>Host -> a joining client: replay every part removed this session so their freshly-loaded
        /// car (from the save snapshot) catches up to the host's live mechanical state.</summary>
        public static void SendCatchUp(CSteamID target)
        {
            if (S == null || _removed.Count == 0) return;
            int n = 0;
            foreach (var key in _removed) { S.SendPartActionTo(target, ActUnmount, key, -1); n++; }
            CarMecaPlugin.Logger?.LogInfo($"[part] catch-up: replayed {n} session removal(s) to {S.Net.NameOf(target)}");
        }

        /// <summary>Apply a peer's part action to OUR copy of the car (instant, no animation).</summary>
        public static void ApplyRemote(byte fromPlayer, byte action, string key, int boltIdx)
        {
            if (string.IsNullOrEmpty(key)) return;
            CarLoader cl = CarRefs.LoadedCar();
            if (cl == null && _lastCar != null) { try { if (_lastCar.IsCarLoaded()) cl = _lastCar; } catch { } }
            if (cl == null) { CarMecaPlugin.Logger?.LogWarning($"[part] remote '{key}' but no current car loaded"); return; }
            Applying = true;
            try
            {
                if (action == ActUnmount)
                {
                    // Use the RUNTIME removal (what the player's own click calls) so the part actually comes
                    // off visually — the *FromSave variants only set internal state, no visual rebuild.
                    cl.TakeOffCarPart(key);
                    _removed.Add(key);               // keep authoritative state so later joiners see it too
                    CarMecaPlugin.Logger?.LogInfo($"[part] applied remote TAKE-OFF '{key}' (runtime)");
                }
                else if (action == ActMount)
                {
                    cl.TakeOnCarPartFromSave(key);   // visual mount method still TBD; state-only for now
                    _removed.Remove(key);
                    CarMecaPlugin.Logger?.LogInfo($"[part] applied remote PUT-ON '{key}' (state-only)");
                }
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning($"[part] apply '{key}' failed: " + e.Message); }
            Applying = false;
        }

        /// <summary>Periodic upkeep (lock expiry, etc.) — reserved for later.</summary>
        public static void Tick() { }
    }
}
