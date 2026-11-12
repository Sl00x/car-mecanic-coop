using System;
using CarMecaSim.Shared.Coop;

namespace CarMecaSim.FakePeer
{
    /// <summary>
    /// Offline unit tests for the pure co-op model (§20.1): identity determinism, canonical state +
    /// revisions, deterministic hash + desync detection, concurrency locks. Run: `-- unittest`.
    /// </summary>
    internal static class CoopTests
    {
        static int _pass, _fail;

        static void Check(string name, bool ok)
        {
            if (ok) { _pass++; Console.WriteLine($"  [x] {name}"); }
            else { _fail++; Console.WriteLine($"  [FAIL] {name}"); }
        }

        public static int Run()
        {
            Console.WriteLine("==================== UNIT TESTS (pure co-op model) ====================");

            // --- Identity determinism (§5) ---
            var v1 = new VehicleKey("car_vallsen2040", 0);
            var v1b = new VehicleKey("car_vallsen2040", 0);
            var v2 = new VehicleKey("car_vallsen2040", 1);
            var v3 = new VehicleKey("car_hinatamoonmk1", 0);
            Check("VehicleKey equal same inputs", v1.Equals(v1b) && v1.Handle == v1b.Handle);
            Check("VehicleKey differs by place", v1.Handle != v2.Handle);
            Check("VehicleKey differs by config", v1.Handle != v3.Handle);
            var s1 = new PartSlot(v1, "hood");
            var s1b = new PartSlot(v1b, "hood");
            var s2 = new PartSlot(v1, "trunk");
            Check("PartSlot equal same inputs", s1.Equals(s1b) && s1.GetHashCode() == s1b.GetHashCode());
            Check("PartSlot differs by slot", !s1.Equals(s2));

            // --- Canonical state + revisions (§6, §14.1) ---
            var host = new VehiclePartState(v1);
            Check("fresh revision 0", host.Revision == 0);
            int r1 = host.SetRemoved("hood");
            Check("remove bumps revision", r1 == 1 && host.IsRemoved("hood"));
            int r1dup = host.SetRemoved("hood");
            Check("duplicate remove = no bump (no double-apply)", r1dup == 1);
            host.SetRemoved("headlight_left");
            host.SetRemoved("door_front_left");
            Check("3 removed tracked", host.Count == 3 && CountEnum(host.RemovedSlots()) == 3);

            // --- Deterministic hash + convergence/divergence (§14.2, §14.3) ---
            var clientA = new VehiclePartState(v1);
            foreach (var s in host.RemovedSlots()) clientA.SetRemoved(s);     // A replays host's removals
            Check("converged client hash == host hash", clientA.CanonicalHash() == host.CanonicalHash());
            var clientB = new VehiclePartState(v1);
            clientB.SetRemoved("hood");                                        // B missed two removals
            Check("diverged client detected (hash differs)", clientB.CanonicalHash() != host.CanonicalHash());
            // order independence: remove in a different order → same hash
            var hostReorder = new VehiclePartState(v1);
            hostReorder.SetRemoved("door_front_left"); hostReorder.SetRemoved("hood"); hostReorder.SetRemoved("headlight_left");
            Check("hash is order-independent", hostReorder.CanonicalHash() == host.CanonicalHash());

            // --- Part data identity (§6 fundamental rule) ---
            var pd = new PartData { TypeId = "battery_std", Condition = 0.43f };
            pd.Props["brand"] = "Acme";
            host.SetInstalled("battery", pd);
            var clientC = new VehiclePartState(v1);
            foreach (var s in host.RemovedSlots()) clientC.SetRemoved(s);
            clientC.SetInstalled("battery", new PartData { TypeId = "battery_std", Condition = 0.43f }.With("brand", "Acme"));
            Check("installed part with same data → same hash", clientC.CanonicalHash() == host.CanonicalHash());
            var clientD = new VehiclePartState(v1);
            foreach (var s in host.RemovedSlots()) clientD.SetRemoved(s);
            clientD.SetInstalled("battery", new PartData { TypeId = "battery_std", Condition = 0.67f }.With("brand", "Acme"));
            Check("same part, different condition → different hash", clientD.CanonicalHash() != host.CanonicalHash());

            // --- Concurrency locks (§10) ---
            var locks = new InteractionLockManager(leaseSeconds: 5f);
            Check("A acquires free part", locks.TryAcquire("hood", 1, now: 0f));
            Check("B blocked on same part", !locks.TryAcquire("hood", 2, now: 1f));
            Check("B works a different part", locks.TryAcquire("battery", 2, now: 1f));
            Check("A re-acquires own lock", locks.TryAcquire("hood", 1, now: 2f));
            Check("owner reported", locks.OwnerOf("hood", 2f) == 1 && locks.IsLockedByOther("hood", 2, 2f));
            Check("B takes part after A's lease expires", locks.TryAcquire("hood", 2, now: 100f));
            locks.Release("battery", 2);
            Check("released part is free", locks.OwnerOf("battery", 101f) == 0);
            locks.ReleaseAll(2);
            Check("ReleaseAll frees owner's locks", locks.OwnerOf("hood", 101f) == 0);
            var lm2 = new InteractionLockManager(1f);
            lm2.TryAcquire("x", 1, 0f);
            Check("PruneExpired drops stale lease", lm2.PruneExpired(10f) == 1 && lm2.ActiveCount(10f) == 0);

            // --- MutationOrigin scope (§11.2) ---
            Check("default origin is local", ReplicationContext.IsLocal);
            using (ReplicationContext.As(MutationOrigin.RemoteReplication))
                Check("scoped origin = remote (no re-emit)", !ReplicationContext.IsLocal);
            Check("origin restored after scope", ReplicationContext.IsLocal);

            Console.WriteLine("======================================================================");
            Console.WriteLine(_fail == 0 ? $"[unittest] ALL {_pass} PASSED" : $"[unittest] {_fail} FAILED, {_pass} passed");
            return _fail == 0 ? 0 : 1;
        }

        static int CountEnum(System.Collections.Generic.IEnumerable<string> e)
        { int n = 0; foreach (var _ in e) n++; return n; }
    }

    internal static class PartDataExt
    {
        public static PartData With(this PartData p, string k, string v) { p.Props[k] = v; return p; }
    }
}
