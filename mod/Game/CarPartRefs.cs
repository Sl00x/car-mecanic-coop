using System.Collections.Generic;
using UnityEngine;

namespace CarMecaSim.Mp.Game
{
    /// <summary>
    /// Positional part identity, ported from CMS21-Together (Fozkais, MIT). A part is identified by its
    /// ordinal position inside fixed structural roots of the car model — stable across machines because
    /// both load the same prefab → same child order. Far more robust than fragile string names.
    /// identity = (type, key, index).
    /// </summary>
    public sealed class CarPartRefs
    {
        public const byte TEngine = 0, TSuspension = 1, TOther = 2, TDriveshaft = 3, TBody = 4;

        public readonly Dictionary<int, PartScript> Engine = new();
        public readonly Dictionary<int, PartScript> Driveshaft = new();
        public readonly Dictionary<int, List<PartScript>> Suspension = new();
        public readonly Dictionary<int, List<PartScript>> Other = new();
        public readonly Dictionary<int, CarPart> Body = new();

        public static CarPartRefs Build(CarLoader cl)
        {
            var r = new CarPartRefs();
            if (cl == null) return r;
            try { AddSingles(cl.e_engine_h, r.Engine); } catch { }
            try { AddSingles(cl.ds_h, r.Driveshaft); } catch { }
            try
            {
                var susp = new[] { cl.s_frontCenter_h, cl.s_frontLeft_h, cl.s_frontRight_h, cl.s_rearCenter_h, cl.s_rearLeft_h, cl.s_rearRight_h };
                for (int i = 0; i < susp.Length; i++) AddGroup(susp[i], i, r.Suspension);
            }
            catch { }
            try
            {
                var parts = cl.Parts;
                if (parts != null) for (int i = 0; i < parts.Count; i++) AddGroup(parts[i].p_handle, i, r.Other);
            }
            catch { }
            try
            {
                var bp = cl.carParts;
                if (bp != null) for (int i = 0; i < bp.Count; i++) r.Body[i] = bp[i];
            }
            catch { }
            return r;
        }

        private static void AddSingles(GameObject root, Dictionary<int, PartScript> dict)
        {
            if (root == null) return;
            var ps = root.GetComponentsInChildren<PartScript>();
            for (int i = 0; i < ps.Length; i++) dict[i] = ps[i];
        }

        private static void AddGroup(GameObject root, int key, Dictionary<int, List<PartScript>> dict)
        {
            if (root == null) return;
            var ps = root.GetComponentsInChildren<PartScript>();
            var list = new List<PartScript>(ps.Length);
            for (int i = 0; i < ps.Length; i++) list.Add(ps[i]);
            dict[key] = list;
        }

        /// <summary>Reverse lookup: a PartScript → its stable (type, key, index). False if not found.</summary>
        public bool Find(PartScript ps, out byte type, out int key, out int index)
        {
            type = 0; key = 0; index = -1;
            if (ps == null) return false;
            var p = ps.Pointer;
            foreach (var kv in Engine) if (kv.Value != null && kv.Value.Pointer == p) { type = TEngine; key = kv.Key; return true; }
            foreach (var kv in Driveshaft) if (kv.Value != null && kv.Value.Pointer == p) { type = TDriveshaft; key = kv.Key; return true; }
            foreach (var kv in Suspension) { var l = kv.Value; for (int i = 0; i < l.Count; i++) if (l[i] != null && l[i].Pointer == p) { type = TSuspension; key = kv.Key; index = i; return true; } }
            foreach (var kv in Other) { var l = kv.Value; for (int i = 0; i < l.Count; i++) if (l[i] != null && l[i].Pointer == p) { type = TOther; key = kv.Key; index = i; return true; } }
            return false;
        }

        /// <summary>Forward lookup: (type, key, index) → the PartScript on OUR car (null if absent).</summary>
        public PartScript Get(byte type, int key, int index)
        {
            try
            {
                switch (type)
                {
                    case TEngine: return Engine.TryGetValue(key, out var e) ? e : null;
                    case TDriveshaft: return Driveshaft.TryGetValue(key, out var d) ? d : null;
                    case TSuspension: return Suspension.TryGetValue(key, out var s) && index >= 0 && index < s.Count ? s[index] : null;
                    case TOther: return Other.TryGetValue(key, out var o) && index >= 0 && index < o.Count ? o[index] : null;
                }
            }
            catch { }
            return null;
        }

        public CarPart GetBody(int key) => Body.TryGetValue(key, out var b) ? b : null;

        private int Count(Dictionary<int, List<PartScript>> d) { int n = 0; foreach (var kv in d) n += kv.Value.Count; return n; }
        public int Total => Engine.Count + Driveshaft.Count + Count(Suspension) + Count(Other) + Body.Count;
        public override string ToString() => $"engine={Engine.Count} driveshaft={Driveshaft.Count} suspension={Count(Suspension)} other={Count(Other)} body={Body.Count}";
    }
}
