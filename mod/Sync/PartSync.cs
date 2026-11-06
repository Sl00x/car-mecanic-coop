using System;
using System.Collections.Generic;
using UnityEngine;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;

namespace CarMecaSim.Mp.Sync
{
    /// <summary>
    /// Shared-car co-op: replicates part install/remove + bolt steps across players, and enforces
    /// a per-part lock so two players can't work on the SAME part (but can work on different ones).
    /// Keyed by the part's GameObject path (stable across machines for the same car).
    /// </summary>
    public static class PartSync
    {
        public const byte ActMount = 0, ActUnmount = 1, ActBolt = 2, ActRelease = 3;

        /// <summary>True while we apply a remote action locally (so our Harmony hooks don't re-broadcast it).</summary>
        public static bool Applying;

        private static readonly Dictionary<string, byte> Locks = new();   // partKey -> owner playerId
        private static readonly Dictionary<string, float> Touched = new(); // partKey -> last activity time

        private static MpSession S => CarMecaPlugin.Session;
        private static bool Active => S != null && S.InLobby && GameRefs.InGameSession;

        public static string LockOwnerName(string key)
        {
            if (S == null || !Locks.TryGetValue(key, out var id) || id == S.LocalPlayerId) return null;
            foreach (var p in S.Peers) if (S.PlayerIdOf(p.Id) == id) return p.Name;
            return "un autre joueur";
        }

        // ---- local actions (from Harmony prefixes). Return false to BLOCK the local action. ----
        public static bool TryLocalPart(PartScript part, byte action)
        {
            if (Applying || !Active || part == null) return true;
            var key = CarRefs.PathOf(part);
            if (string.IsNullOrEmpty(key)) return true;
            if (IsLockedByOther(key)) { Notify(key); return false; }
            Claim(key);
            S.SendPartAction(action, key, -1);
            return true;
        }

        public static bool TryLocalBolt(MountObject mo)
        {
            if (Applying || !Active || mo == null) return true;
            if (!CarRefs.FindBolt(mo, out var owner, out var idx)) return true;
            var key = CarRefs.PathOf(owner);
            if (string.IsNullOrEmpty(key)) return true;
            if (IsLockedByOther(key)) { Notify(key); return false; }
            Claim(key);
            S.SendPartAction(ActBolt, key, idx);
            return true;
        }

        // ---- remote actions ----
        public static void ApplyRemote(byte fromPlayer, byte action, string key, int boltIdx)
        {
            if (action == ActRelease) { Release(key); return; }
            Locks[key] = fromPlayer; Touched[key] = Now;          // remote owns this part now

            var part = CarRefs.FindByPath(key);
            if (part == null) return;
            Applying = true;
            try
            {
                switch (action)
                {
                    case ActMount: part.FastMount(); break;
                    case ActUnmount: part.FastUnmount(); break;
                    case ActBolt:
                        var mos = part.MountObjects;
                        if (mos != null && boltIdx >= 0 && boltIdx < mos.Length && mos[boltIdx] != null)
                            mos[boltIdx].Action();
                        break;
                }
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("apply part: " + e.Message); }
            Applying = false;
        }

        // ---- lock bookkeeping ----
        private static float Now => Time.unscaledTime;
        private static void Claim(string key) { if (S != null) { Locks[key] = S.LocalPlayerId; Touched[key] = Now; } }
        public static bool IsLockedByOther(string key)
            => S != null && Locks.TryGetValue(key, out var o) && o != S.LocalPlayerId;
        private static void Release(string key) { Locks.Remove(key); Touched.Remove(key); }

        private static float _lastNotify;
        private static void Notify(string key)
        {
            if (Now - _lastNotify < 1.2f) return; _lastNotify = Now;
            var who = LockOwnerName(key) ?? "un autre joueur";
            CarMecaPlugin.Session?.LocalChat($"🔒 Pièce utilisée par {who}");
        }

        /// <summary>Release my own locks after a few seconds of inactivity.</summary>
        public static void Tick()
        {
            if (!Active) return;
            List<string> stale = null;
            foreach (var kv in Touched)
                if (Now - kv.Value > 4f) (stale ??= new()).Add(kv.Key);
            if (stale == null) return;
            foreach (var k in stale)
            {
                if (Locks.TryGetValue(k, out var o) && o == S.LocalPlayerId) S.SendPartAction(ActRelease, k, -1);
                Release(k);
            }
        }
    }
}
