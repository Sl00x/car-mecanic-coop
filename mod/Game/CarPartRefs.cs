using System.Collections.Generic;
using UnityEngine;

namespace CarMecaSim.Mp.Game
{
    /// <summary>
    /// Positional part identity. A part is identified by its ordinal position in the car model — stable
    /// across machines because both load the same prefab → same part order. identity = (type, key, index).
    ///
    /// Two population schemes, tried in order:
    ///  1. Structural roots (ported from CMS21-Together, MIT): e_engine_h / ds_h / s_*_h / Parts / carParts.
    ///  2. FLAT fallback: CarLoader.GetPartScriptCache() — the game's own cached, deterministic list of EVERY
    ///     PartScript on the car (this is what the working string-path uses). If the roots come back empty
    ///     (null roots, or IL2CPP's generic GetComponentsInChildren returning nothing), the flat cache still
    ///     gives every mechanical part a stable index. Both machines run the same code+prefab, so whichever
    ///     scheme populates, it populates identically on both → the identity agrees.
    /// </summary>
    public sealed class CarPartRefs
    {
        public const byte TEngine = 0, TSuspension = 1, TOther = 2, TDriveshaft = 3, TBody = 4, TFlat = 5;

        public readonly Dictionary<int, PartScript> Engine = new();
        public readonly Dictionary<int, PartScript> Driveshaft = new();
        public readonly Dictionary<int, List<PartScript>> Suspension = new();
        public readonly Dictionary<int, List<PartScript>> Other = new();
        public readonly Dictionary<int, CarPart> Body = new();
        public readonly Dictionary<int, PartScript> Flat = new();   // fallback: index in GetPartScriptCache()

        public static GameObject CarRootOf(PartScript ps)
        {
            if (ps == null) return null;
            try { var t = ps.transform; while (t != null && t.parent != null) t = t.parent; return t != null ? t.gameObject : null; }
            catch { return null; }
        }

        public static CarPartRefs Build(CarLoader cl, PartScript hint = null)
        {
            var r = new CarPartRefs();
            if (cl == null && hint == null) { Log("build: cl is null"); return r; }

            string clName = "?"; bool loaded = false; string cfg = "?";
            try { if (cl != null) clName = cl.name; } catch { }
            try { if (cl != null) loaded = cl.IsCarLoaded(); } catch { }
            try { if (cl != null) cfg = cl.carToLoad; } catch { }
            // Garage loader SLOTS are reused across cars (same #CarLoaderN pointer, different car) and the
            // loader's carToLoad/part-cache go STALE — the reliable source of THIS car's parts is the fired
            // part's own hierarchy root (the car GameObject), not the loader's cache. Force a cache refresh
            // too, best-effort.
            try { cl?.CachePartScripts(); } catch { }
            var carRoot = CarRootOf(hint);
            try { if (carRoot != null) cfg = carRoot.name; } catch { }

            // --- scheme 1: structural roots ---
            r.Engine.Clear();
            AddSingles("e_engine_h", Safe(() => cl.e_engine_h), r.Engine);
            AddSingles("ds_h", Safe(() => cl.ds_h), r.Driveshaft);
            var suspRoots = new (string, GameObject)[]
            {
                ("s_frontCenter_h", Safe(() => cl.s_frontCenter_h)),
                ("s_frontLeft_h",   Safe(() => cl.s_frontLeft_h)),
                ("s_frontRight_h",  Safe(() => cl.s_frontRight_h)),
                ("s_rearCenter_h",  Safe(() => cl.s_rearCenter_h)),
                ("s_rearLeft_h",    Safe(() => cl.s_rearLeft_h)),
                ("s_rearRight_h",   Safe(() => cl.s_rearRight_h)),
            };
            for (int i = 0; i < suspRoots.Length; i++) AddGroup(suspRoots[i].Item1, suspRoots[i].Item2, i, r.Suspension);
            try
            {
                var parts = cl.Parts;
                if (parts != null) for (int i = 0; i < parts.Count; i++) AddGroup($"Parts[{i}]", Safe(() => parts[i].p_handle), i, r.Other);
            }
            catch (System.Exception e) { Log("Parts walk threw: " + e.Message); }
            try
            {
                var bp = cl.carParts;
                if (bp != null) for (int i = 0; i < bp.Count; i++) r.Body[i] = bp[i];
            }
            catch (System.Exception e) { Log("carParts walk threw: " + e.Message); }

            // --- scheme 2: FLAT by the part's own car-root hierarchy (reliable across loader-slot reuse),
            // falling back to the loader's part cache when we have no part hint. ---
            int cacheLen = 0; string flatSrc = "none";
            try
            {
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<PartScript> comps = null;
                if (carRoot != null) { comps = carRoot.GetComponentsInChildren<PartScript>(true); flatSrc = "car-root"; }
                if (comps == null || comps.Length == 0) { comps = CarRefs.Parts(cl); flatSrc = "loader-cache"; }
                if (comps != null)
                {
                    cacheLen = comps.Length;
                    for (int i = 0; i < comps.Length; i++) { var p = comps[i]; if (p != null) r.Flat[i] = p; }
                }
            }
            catch (System.Exception e) { Log("flat build threw: " + e.Message); }

            Log($"diag cl='{clName}' car='{cfg}' loaded={loaded} flat={cacheLen}({flatSrc}) | {r}");
            return r;
        }

        private static GameObject Safe(System.Func<GameObject> f) { try { return f(); } catch { return null; } }
        private static void Log(string m) => CarMecaPlugin.Logger?.LogInfo("[cpart] " + m);

        private static void AddSingles(string tag, GameObject root, Dictionary<int, PartScript> dict)
        {
            if (root == null) { return; }
            try
            {
                var ps = root.GetComponentsInChildren<PartScript>();
                for (int i = 0; i < ps.Length; i++) dict[i] = ps[i];
            }
            catch (System.Exception e) { Log($"{tag}.GetComponentsInChildren threw: " + e.Message); }
        }

        private static void AddGroup(string tag, GameObject root, int key, Dictionary<int, List<PartScript>> dict)
        {
            if (root == null) return;
            try
            {
                var ps = root.GetComponentsInChildren<PartScript>();
                var list = new List<PartScript>(ps.Length);
                for (int i = 0; i < ps.Length; i++) list.Add(ps[i]);
                dict[key] = list;
            }
            catch (System.Exception e) { Log($"{tag}.GetComponentsInChildren threw: " + e.Message); }
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
            foreach (var kv in Flat) if (kv.Value != null && kv.Value.Pointer == p) { type = TFlat; key = kv.Key; return true; }
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
                    case TFlat: return Flat.TryGetValue(key, out var f) ? f : null;
                }
            }
            catch { }
            return null;
        }

        public CarPart GetBody(int key) => Body.TryGetValue(key, out var b) ? b : null;

        private int Count(Dictionary<int, List<PartScript>> d) { int n = 0; foreach (var kv in d) n += kv.Value.Count; return n; }
        public int Total => Engine.Count + Driveshaft.Count + Count(Suspension) + Count(Other) + Body.Count + Flat.Count;
        public override string ToString() => $"engine={Engine.Count} driveshaft={Driveshaft.Count} suspension={Count(Suspension)} other={Count(Other)} body={Body.Count} flat={Flat.Count}";
    }
}
