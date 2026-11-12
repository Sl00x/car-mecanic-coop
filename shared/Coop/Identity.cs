using System;

namespace CarMecaSim.Shared.Coop
{
    /// <summary>
    /// Stable network identity for a vehicle, independent of Unity instance ids (§5.1). Derived
    /// deterministically from the car's config name + garage place so both machines agree and it survives
    /// a reconnect / scene reload. NOTE: a garage *move* changes the place → the host registry (see
    /// VehicleRegistry) is the authority that can re-key; this derivation is the default seed.
    /// </summary>
    public readonly struct VehicleKey : IEquatable<VehicleKey>
    {
        public readonly string Config;   // e.g. "car_vallsen2040"
        public readonly int Place;        // garage place / lift index

        public VehicleKey(string config, int place) { Config = config ?? ""; Place = place; }

        /// <summary>Deterministic 64-bit handle (FNV-1a) — same inputs → same value on every machine.</summary>
        public ulong Handle => Fnv.Hash64($"{Config}#{Place}");

        public bool Equals(VehicleKey o) => Place == o.Place && Config == o.Config;
        public override bool Equals(object o) => o is VehicleKey k && Equals(k);
        public override int GetHashCode() => unchecked((int)Fnv.Hash64($"{Config}#{Place}"));
        public override string ToString() => $"{Config}@{Place}";
    }

    /// <summary>
    /// Identity of a mechanical slot on a vehicle (§5.2): the vehicle + the part's stable key within it.
    /// CMS part keys are already machine-stable strings ("hood", "engine_r4_sohc_wzdluzny/r4_blok(0)").
    /// A slot's occupancy changes; the slot id itself does not.
    /// </summary>
    public readonly struct PartSlot : IEquatable<PartSlot>
    {
        public readonly VehicleKey Vehicle;
        public readonly string Slot;      // CMS part key (bare name or internal path)

        public PartSlot(VehicleKey vehicle, string slot) { Vehicle = vehicle; Slot = slot ?? ""; }

        public bool Equals(PartSlot o) => Vehicle.Equals(o.Vehicle) && Slot == o.Slot;
        public override bool Equals(object o) => o is PartSlot p && Equals(p);
        public override int GetHashCode() => unchecked((int)Fnv.Hash64($"{Vehicle}|{Slot}"));
        public override string ToString() => $"{Vehicle}/{Slot}";
    }

    /// <summary>FNV-1a 64-bit — deterministic, allocation-light, identical on every machine/runtime.</summary>
    public static class Fnv
    {
        public const ulong Offset = 14695981039346656037UL;
        public const ulong Prime = 1099511628211UL;

        public static ulong Hash64(string s)
        {
            ulong h = Offset;
            if (s != null)
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    h = (h ^ (byte)(c & 0xFF)) * Prime;
                    h = (h ^ (byte)(c >> 8)) * Prime;
                }
            return h;
        }

        public static ulong Combine(ulong h, ulong v)
        {
            for (int i = 0; i < 8; i++) { h = (h ^ (byte)(v & 0xFF)) * Prime; v >>= 8; }
            return h;
        }
    }
}
