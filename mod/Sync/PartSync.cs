using System;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;
using CarMecaSim.Shared.Coop;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;
using CoopPartData = CarMecaSim.Shared.Coop.PartData;   // the game's interop also defines a PartData

namespace CarMecaSim.Mp.Sync
{
    /// <summary>
    /// Real-time shared-car co-op. In CMS2021 the actual removal entry point is
    /// CarLoader.TakeOffCarPart(string key) (NOT PartScript.ActionUnMount); the key is a machine-stable
    /// string ("hood", "headlight_left"). We capture it, keep the host-authoritative canonical mechanical
    /// state (<see cref="VehiclePartState"/>: removed slots + a monotonic revision + a deterministic hash),
    /// broadcast the change, and the peer replays it via the runtime CarLoader.TakeOffCarPart so the same
    /// part comes off their identical car, live. A late-joiner replays the whole canonical state.
    /// </summary>
    public static class PartSync
    {
        // action ids on the wire: 0=mount(install) 1=unmount(remove) 2=bolt 3=release-lock 4=switch(open/close)
        public const byte ActMount = 0, ActUnmount = 1, ActBolt = 2, ActRelease = 3, ActSwitch = 4;

        /// <summary>True while applying a remote action, so our Harmony hooks don't re-broadcast it.</summary>
        public static bool Applying;

        private static MpSession S => CarMecaPlugin.Session;
        private static bool Active => S != null && S.InLobby && GameRefs.InGameSession;

        // The Void and IEnumerator TakeOffCarPart overloads can both fire for one removal — dedup by key.
        private static readonly Dictionary<string, float> _lastSent = new();
        private static CarLoader _lastCar;   // the car the local player last worked on (reliable handle)

        // Host-authoritative canonical state of the car on the lift. One car at a time (CMS gameplay);
        // a different car makes a fresh state. This is the single source of truth a late-joiner replays,
        // and what the desync hash is computed from.
        private static VehiclePartState _state;
        public static VehiclePartState State => _state;

        // Per-part locks (§10): a player working a part blocks others from the SAME part. Host-authoritative;
        // advisory on clients (full distributed lock needs interaction-start hooks — see KNOWN_LIMITATIONS).
        private static readonly InteractionLockManager _locks = new(leaseSeconds: 6f);

        private static VehiclePartState Ensure(CarLoader cl)
        {
            VehicleKey key = KeyOf(cl);
            if (_state == null || !_state.Vehicle.Equals(key)) _state = new VehiclePartState(key);
            return _state;
        }

        private static VehicleKey KeyOf(CarLoader cl)
        {
            string cfg = ""; int place = 0;
            try { cfg = cl?.carToLoad ?? ""; } catch { }
            try { place = cl != null ? cl.GetPlaceNo() : 0; } catch { }
            return new VehicleKey(cfg, place);
        }

        /// <summary>The real car to key canonical state on — the garage's #CarLoaderN placeholder (empty roots,
        /// cache=0) makes carToLoad/GetPlaceNo bogus; prefer the loader that actually has parts.</summary>
        private static CarLoader RealCar(CarLoader hint)
        {
            if (hint != null) { try { if (hint.IsCarLoaded()) { var c = CarRefs.Parts(hint); if (c != null && c.Length > 0) return hint; } } catch { } }
            // The car the local player LAST worked on (set by OnLocalTakeOff / OnLocalSwitch) — the ACTIVE car.
            // Prefer it over CarWithParts(), which returns the FIRST loader with parts and so points at a stale
            // car still on another lift (symptom: a remote hood-close hit car_vallsen2040 instead of the car
            // the player was actually on).
            if (_lastCar != null) { try { if (_lastCar.IsCarLoaded()) return _lastCar; } catch { } }
            var withParts = CarRefs.CarWithParts();
            if (withParts != null) return withParts;
            return hint ?? CarRefs.LoadedCar();
        }

        /// <summary>Local player took a part off the car (Harmony postfix) -> record + broadcast to peers.</summary>
        public static void OnLocalTakeOff(CarLoader cl, string partKey)
        {
            if (cl != null) _lastCar = cl;
            if (Applying || !Active || string.IsNullOrEmpty(partKey)) return;
            float now = Time.unscaledTime;
            if (_lastSent.TryGetValue(partKey, out var t) && now - t < 0.5f) return;   // collapse double-fire
            _lastSent[partKey] = now;
            var st = Ensure(RealCar(cl));
            // claim the lock for ourselves (so a peer's concurrent attempt on this exact part is rejected)
            _locks.TryAcquire(partKey, S.LocalPlayerId, now);
            int rev = st.SetRemoved(partKey);
            S.SendPartAction(ActUnmount, partKey, -1);
            CarMecaPlugin.Logger?.LogInfo($"[part] local take-off '{partKey}' -> broadcast (veh {st.Vehicle} rev={rev})");
        }

        /// <summary>A different car is on the lift — drop the previous car's canonical state and locks.</summary>
        public static void ClearRemoved()
        {
            if (_state != null && _state.Count > 0) CarMecaPlugin.Logger?.LogInfo($"[part] reset canonical state ({_state}) — car changed");
            _state = null; _lastSent.Clear();
        }

        /// <summary>Host -> a joining client: replay the whole canonical mechanical state so their freshly
        /// loaded car (from the save snapshot) converges to the host's CURRENT disassembly.</summary>
        public static void SendCatchUp(CSteamID target)
        {
            if (S == null || _state == null) return;
            int n = 0;
            foreach (var slot in _state.RemovedSlots()) { S.SendPartActionTo(target, ActUnmount, slot, -1); n++; }
            if (n > 0) CarMecaPlugin.Logger?.LogInfo($"[part] catch-up: replayed {n} removal(s) (rev={_state.Revision}, hash={_state.CanonicalHash():X}) to {S.Net.NameOf(target)}");
        }

        /// <summary>Apply a peer's part action to OUR copy of the car (instant, no animation).</summary>
        public static void ApplyRemote(byte fromPlayer, byte action, string key, int boltIdx)
        {
            if (string.IsNullOrEmpty(key)) return;
            CarLoader cl = RealCar(null);
            if (cl == null) { CarMecaPlugin.Logger?.LogWarning($"[part] remote '{key}' but no current car loaded"); return; }
            var st = Ensure(cl);
            using (ReplicationContext.As(MutationOrigin.RemoteReplication))
            {
                Applying = true;
                try
                {
                    if (action == ActUnmount)
                    {
                        // RUNTIME removal (what a player's click calls) so the part comes off VISUALLY —
                        // the *FromSave variants only set internal state with no visual rebuild.
                        cl.TakeOffCarPart(key);
                        st.SetRemoved(key);
                        _locks.TryAcquire(key, fromPlayer, Time.unscaledTime);   // remote owns this part now
                        CarMecaPlugin.Logger?.LogInfo($"[part] applied remote TAKE-OFF '{key}' (rev={st.Revision})");
                    }
                    else if (action == ActMount)
                    {
                        // Body-part mount (per CMS21-Together, MIT): put it back on, then UpdateCarBodyPart
                        // to actually REBUILD the visual — this is the refresh call we were missing before
                        // (TakeOnCarPartFromSave alone is state-only; the hood never reappeared).
                        try
                        {
                            cl.TakeOnCarPartFromSave(key);
                            var cp = cl.GetCarPart(key);
                            if (cp != null) cl.UpdateCarBodyPart(cp);
                        }
                        catch (Exception me) { CarMecaPlugin.Logger?.LogWarning("[part] mount refresh failed: " + me.Message); }
                        st.Unset(key);   // re-mounted body part returns to stock (keeps the hash a pure removed-set)
                        _locks.Release(key, fromPlayer);
                        CarMecaPlugin.Logger?.LogInfo($"[part] applied remote PUT-ON '{key}' (+UpdateCarBodyPart, rev={st.Revision})");
                    }
                    else if (action == ActRelease) _locks.Release(key, fromPlayer);
                    else if (action == ActSwitch)
                    {
                        // Openable (hood/trunk/engine_cover/clamshell) open-close. Set the Switched state with
                        // the 3-arg (no-animation) setter, THEN UpdateCarBodyPart to actually REBUILD the visual
                        // to match — mirrors CMS21-Together's UpdateBodyPart (SwitchCarPart + UpdateCarBodyPart).
                        // Also start the animated 2-arg coroutine as a belt-and-braces visual trigger.
                        try
                        {
                            var cp = cl.GetCarPart(key);
                            if (cp != null)
                            {
                                bool open = boltIdx != 0;
                                try { cl.SwitchCarPart(cp, false, open); } catch { }
                                try { cl.UpdateCarBodyPart(cp); } catch { }
                            }
                        }
                        catch (Exception se) { CarMecaPlugin.Logger?.LogWarning("[part] switch apply failed: " + se.Message); }
                        CarMecaPlugin.Logger?.LogInfo($"[part] applied remote SWITCH '{key}' open={boltIdx != 0}");
                    }
                }
                catch (Exception e) { CarMecaPlugin.Logger?.LogWarning($"[part] apply '{key}' failed: " + e.Message); }
                Applying = false;
            }
        }

        /// <summary>Is this part currently claimed by another player? (advisory conflict check)</summary>
        public static bool LockedByOther(string key)
            => S != null && _locks.IsLockedByOther(key, S.LocalPlayerId, Time.unscaledTime);

        /// <summary>A player left — free every part they were holding (§10.3) so it isn't stuck locked.</summary>
        public static void OnPeerLeft(byte playerId) => _locks.ReleaseAll(playerId);

        /// <summary>Periodic upkeep: expire stale leases.</summary>
        public static void Tick()
        {
            if (!Active) return;
            _locks.PruneExpired(Time.unscaledTime);
        }

        /// <summary>Record a mechanical part change into the canonical state so the desync hash is accurate.
        /// Mechanical parts get a positional slot key "m:type:key:index" (body parts use their string name).</summary>
        public static void RecordMechanical(CarLoader car, byte type, int key, int index, bool mounted, MechData data)
        {
            if (S == null || !S.InLobby) return;
            var st = Ensure(RealCar(car));
            string slot = $"m:{type}:{key}:{index}";
            // A re-mounted part returns to stock (slot removed from the hash), matching a peer that drops it
            // from its removed-set. Part DATA (condition/paint/tuning) still rides the wire on mount (T1); it
            // is just not part of the desync hash, which stays a pure "set of currently-removed parts".
            if (mounted) st.Unset(slot);
            else st.SetRemoved(slot);
        }

        /// <summary>Local player opened/closed an openable (hood/trunk/…) — broadcast it (string channel).</summary>
        public static void OnLocalSwitch(CarLoader cl, CarPart part, bool open)
        {
            if (Applying || !Active || part == null) return;
            string name = null; try { name = part.name; } catch { }
            if (string.IsNullOrEmpty(name)) return;
            if (cl != null) _lastCar = cl;
            S.SendPartAction(ActSwitch, name, open ? 1 : 0);
            CarMecaPlugin.Logger?.LogInfo($"[part] local SWITCH '{name}' open={open} -> broadcast");
        }

        // ---- diagnostics (F9 overlay / desync reporting) ----
        public static int RemovedCount => _state?.Count ?? 0;
        public static int Revision => _state?.Revision ?? 0;
        public static ulong Hash => _state?.CanonicalHash() ?? 0UL;
        public static int ActiveLocks => _locks.ActiveCount(Time.unscaledTime);
    }
}
