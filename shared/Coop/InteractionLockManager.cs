using System.Collections.Generic;

namespace CarMecaSim.Shared.Coop
{
    /// <summary>
    /// Host-side per-resource locks (§10): two players may work on different parts of the same car, but
    /// not on the SAME part. Leases expire so a crashed/disconnected owner can't hold a part forever.
    /// Pure logic — the caller supplies "now" (seconds) so it is deterministically testable.
    /// </summary>
    public sealed class InteractionLockManager
    {
        public readonly struct Lease
        {
            public readonly byte Owner; public readonly float Expires;
            public Lease(byte owner, float expires) { Owner = owner; Expires = expires; }
        }

        private readonly Dictionary<string, Lease> _locks = new();
        private readonly float _leaseSeconds;

        public InteractionLockManager(float leaseSeconds = 5f) { _leaseSeconds = leaseSeconds; }

        /// <summary>Try to acquire/refresh a lock. Succeeds if free, expired, or already owned by us.</summary>
        public bool TryAcquire(string resource, byte player, float now)
        {
            if (string.IsNullOrEmpty(resource)) return false;
            if (_locks.TryGetValue(resource, out var l) && l.Owner != player && l.Expires > now)
                return false;                                   // held by someone else, not expired
            _locks[resource] = new Lease(player, now + _leaseSeconds);
            return true;
        }

        /// <summary>Who holds this resource right now (0 = nobody / expired).</summary>
        public byte OwnerOf(string resource, float now)
            => _locks.TryGetValue(resource, out var l) && l.Expires > now ? l.Owner : (byte)0;

        public bool IsLockedByOther(string resource, byte player, float now)
        {
            var o = OwnerOf(resource, now);
            return o != 0 && o != player;
        }

        public void Release(string resource, byte player)
        {
            if (_locks.TryGetValue(resource, out var l) && l.Owner == player) _locks.Remove(resource);
        }

        /// <summary>Release every lock held by a player (on disconnect / crash).</summary>
        public void ReleaseAll(byte player)
        {
            var drop = new List<string>();
            foreach (var kv in _locks) if (kv.Value.Owner == player) drop.Add(kv.Key);
            foreach (var k in drop) _locks.Remove(k);
        }

        /// <summary>Drop expired leases (call periodically). Returns how many were dropped.</summary>
        public int PruneExpired(float now)
        {
            var drop = new List<string>();
            foreach (var kv in _locks) if (kv.Value.Expires <= now) drop.Add(kv.Key);
            foreach (var k in drop) _locks.Remove(k);
            return drop.Count;
        }

        public int ActiveCount(float now)
        {
            int n = 0;
            foreach (var kv in _locks) if (kv.Value.Expires > now) n++;
            return n;
        }
    }

    /// <summary>Origin of a state mutation (§11.2) — lets Harmony patches tell a user action apart from a
    /// replicated/snapshot/reconciliation apply, so applied-remote mutations never re-emit commands.</summary>
    public enum MutationOrigin
    {
        LocalPlayer,
        RemoteReplication,
        SnapshotApplication,
        Reconciliation,
        SaveRestoration
    }

    /// <summary>Thread-local-ish replication context (the mod runs mutations on the Unity main thread, so a
    /// simple scoped field is sufficient and avoids async/coroutine pitfalls called out in §11.2).</summary>
    public static class ReplicationContext
    {
        public static MutationOrigin Current = MutationOrigin.LocalPlayer;

        public readonly struct Scope : System.IDisposable
        {
            private readonly MutationOrigin _prev;
            public Scope(MutationOrigin o) { _prev = Current; Current = o; }
            public void Dispose() { Current = _prev; }
        }

        public static Scope As(MutationOrigin o) => new Scope(o);
        public static bool IsLocal => Current == MutationOrigin.LocalPlayer;
    }
}
