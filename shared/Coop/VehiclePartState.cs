using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CarMecaSim.Shared.Coop
{
    /// <summary>
    /// Canonical mechanical state of ONE part instance (§6.3). Pure data, no Unity refs. Carries the
    /// part data that must be identical on every machine — not just "installed or not".
    /// </summary>
    public sealed class PartData
    {
        public string TypeId = "";                 // part/variant identifier
        public bool Installed = true;               // on the car vs removed (to an inventory)
        public float Condition = 1f;                // 0..1
        public readonly SortedDictionary<string, string> Props = new(StringComparer.Ordinal);

        public PartData Clone()
        {
            var c = new PartData { TypeId = TypeId, Installed = Installed, Condition = Condition };
            foreach (var kv in Props) c.Props[kv.Key] = kv.Value;
            return c;
        }

        /// <summary>Append this part's state to a hash in a deterministic, culture-invariant way.</summary>
        public void HashInto(ref ulong h)
        {
            h = Fnv.Combine(h, Fnv.Hash64(TypeId));
            h = Fnv.Combine(h, Installed ? 1UL : 0UL);
            // quantize the float so tiny FP differences across machines don't cause false desyncs
            h = Fnv.Combine(h, (ulong)(uint)(int)Math.Round(Condition * 1000f));
            foreach (var kv in Props)   // SortedDictionary → stable order
            {
                h = Fnv.Combine(h, Fnv.Hash64(kv.Key));
                h = Fnv.Combine(h, Fnv.Hash64(kv.Value));
            }
        }
    }

    /// <summary>
    /// Canonical mechanical state of a vehicle: the slots whose part differs from a "fully assembled"
    /// baseline (removed, or installed with specific data). A revision increments on every mutation so
    /// clients can detect gaps (§14.1). This is the host-authoritative truth a late-joiner replays.
    /// </summary>
    public sealed class VehiclePartState
    {
        public readonly VehicleKey Vehicle;
        public int Revision { get; private set; }

        // slot key -> part data. Absence from the map = "stock/assembled, nothing to replicate".
        private readonly SortedDictionary<string, PartData> _slots = new(StringComparer.Ordinal);

        public VehiclePartState(VehicleKey vehicle) { Vehicle = vehicle; }

        public IReadOnlyDictionary<string, PartData> Slots => _slots;
        public int Count => _slots.Count;

        /// <summary>Mark a slot as empty (part removed). Returns the new revision.</summary>
        public int SetRemoved(string slot)
        {
            if (string.IsNullOrEmpty(slot)) return Revision;
            if (!_slots.TryGetValue(slot, out var p)) { p = new PartData(); _slots[slot] = p; }
            if (!p.Installed) return Revision;           // already removed → no change
            p.Installed = false;
            return ++Revision;
        }

        /// <summary>Record a part installed in a slot with its exact data. Returns the new revision.</summary>
        public int SetInstalled(string slot, PartData data)
        {
            if (string.IsNullOrEmpty(slot)) return Revision;
            _slots[slot] = (data ?? new PartData()).Clone();
            _slots[slot].Installed = true;
            return ++Revision;
        }

        public bool IsRemoved(string slot) => _slots.TryGetValue(slot, out var p) && !p.Installed;

        /// <summary>All currently-removed slot keys (what a late-joiner must take off).</summary>
        public IEnumerable<string> RemovedSlots()
        {
            foreach (var kv in _slots) if (!kv.Value.Installed) yield return kv.Key;
        }

        public void Clear() { _slots.Clear(); Revision = 0; }

        /// <summary>Deterministic hash of the whole mechanical state (§14.2) — identical inputs → identical
        /// hash on every machine. Order-stable (SortedDictionary), culture-invariant, FP-quantized.</summary>
        public ulong CanonicalHash()
        {
            ulong h = Fnv.Hash64("cms-coop-veh-v1");
            h = Fnv.Combine(h, Vehicle.Handle);
            foreach (var kv in _slots)
            {
                h = Fnv.Combine(h, Fnv.Hash64(kv.Key));
                kv.Value.HashInto(ref h);
            }
            return h;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Vehicle).Append(" rev=").Append(Revision).Append(" removed=[");
            bool first = true;
            foreach (var s in RemovedSlots()) { if (!first) sb.Append(','); sb.Append(s); first = false; }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
