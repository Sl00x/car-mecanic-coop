using System;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;
using UnityEngine;

namespace CarMecaSim.Mp.Sync
{
    /// <summary>Part DATA carried with a mechanical mount (condition/paint/tuning/…) — ported field set from
    /// CMS21-Together's ModPartScript (MIT). All optional; a field the interop lacks is simply skipped.</summary>
    public struct MechData
    {
        public float Condition;
        public float Dust;
        public int Quality;
        public bool Examined;
        public bool Painted;
        public byte PaintType;
        public float R, G, B, A;
        public string TunedId;

        public static MechData Default => new MechData { Condition = 1f, A = 1f, TunedId = "" };
    }

    /// <summary>
    /// Mechanical part sync by POSITIONAL identity (ported from CMS21-Together, MIT). Engine / suspension /
    /// other / driveshaft parts are captured on PartScript.DoMount (mount) + PartScript.Hide (unmount),
    /// identified by (type,key,index) via <see cref="CarPartRefs"/>, and reproduced on the peer with the
    /// game's own visual mount/unmount sequence. A MOUNT also carries the part's DATA (condition/paint/
    /// tuning/dust) so the installed part looks identical on both machines. Every change is also recorded
    /// into the host-authoritative canonical <see cref="VehiclePartState"/> so the desync hash is accurate.
    /// </summary>
    public static class CarPartSync
    {
        public static bool Applying;   // guard so our visual apply doesn't re-trigger the capture hooks

        private static MpSession S => CarMecaPlugin.Session;
        private static bool Active => S != null && S.InLobby && GameRefs.InGameSession;

        private static CarPartRefs _refs;
        private static CarLoader _refsCar;
        private static string _refsConfig;
        private static int _capN;

        private static CarLoader ResolveCar(PartScript hint)
        {
            var owner = CarRefs.OwnerOf(hint);
            if (owner != null) return owner;
            var withParts = CarRefs.CarWithParts();
            if (withParts != null) return withParts;
            return CarRefs.LoadedCar();
        }

        public static CarPartRefs EnsureRefs(CarLoader cl, PartScript hint = null)
        {
            // CAPTURE (we have the fired part): key the ref cache by the part's car-root GameObject NAME —
            // the reliable car identity. Loader carToLoad/pointer are unreliable (slots are reused and the
            // field goes stale). Rebuild when that car id changes.
            if (hint != null)
            {
                string carId = null;
                var root = CarPartRefs.CarRootOf(hint);
                if (root != null) { try { carId = root.name; } catch { } }
                if (string.IsNullOrEmpty(carId)) { try { carId = cl?.carToLoad ?? ""; } catch { } }
                if (_refs == null || _refsConfig != carId)
                {
                    _refs = CarPartRefs.Build(cl, hint);
                    _refsCar = cl; _refsConfig = carId;
                    CarMecaPlugin.Logger?.LogInfo($"[cpart] built refs for car '{carId}': {_refs}");
                }
                return _refs;
            }
            // APPLY (no part hint): reuse the refs the last local capture built (correct car). Only build from
            // the loader as a last resort when we have none yet.
            if (_refs == null && cl != null)
            {
                _refs = CarPartRefs.Build(cl, null); _refsCar = cl;
                CarMecaPlugin.Logger?.LogInfo($"[cpart] built refs (apply, no hint): {_refs}");
            }
            return _refs;
        }

        public static void Invalidate() { _refs = null; _refsCar = null; _refsConfig = null; }

        // ---- capture (Harmony postfix) ----
        public static void OnDoMount(PartScript ps) => Capture(ps, true);
        public static void OnHide(PartScript ps) => Capture(ps, false);

        private static void Capture(PartScript ps, bool mounted)
        {
            if (Applying || !Active || ps == null) return;
            var car = ResolveCar(ps);
            if (_capN++ < 12)
            {
                string path = CarRefs.PathOf(ps);
                string owner = "null"; int ocache = 0;
                try { if (car != null) owner = car.name; } catch { }
                try { var c = CarRefs.Parts(car); ocache = c != null ? c.Length : 0; } catch { }
                CarMecaPlugin.Logger?.LogInfo($"[cpart] capture ps='{path}' resolvedCar='{owner}' cache={ocache} mounted={mounted}");
            }

            var refs = EnsureRefs(car, ps);
            if (refs == null) return;
            if (!refs.Find(ps, out var type, out var key, out var index))
            {
                // Not found — the refs may be stale (car changed in a reused loader slot). Force one rebuild
                // from THIS part's hierarchy and retry (a genuine body part still won't be found → elsewhere).
                Invalidate();
                refs = EnsureRefs(car, ps);
                if (refs == null || !refs.Find(ps, out type, out key, out index)) return;
            }

            MechData data = mounted ? ReadData(ps) : default;
            S.SendPartState(type, key, index, mounted, data);
            PartSync.RecordMechanical(car, type, key, index, mounted, data);   // keep the canonical hash accurate
            CarMecaPlugin.Logger?.LogInfo($"[cpart] local {(mounted ? "MOUNT" : "UNMOUNT")} type={type} key={key} idx={index}{(mounted ? $" cond={data.Condition:0.00} tuned={data.TunedId}" : "")} -> broadcast");
        }

        // ---- apply a peer's mechanical part change on OUR car ----
        public static void ApplyRemote(byte type, int key, int index, bool mounted, MechData data)
        {
            var car = ResolveCar(null);
            var refs = EnsureRefs(car);
            if (refs == null) { CarMecaPlugin.Logger?.LogWarning("[cpart] remote part but no car/refs"); return; }
            var reference = refs.Get(type, key, index);
            if (reference == null) { CarMecaPlugin.Logger?.LogWarning($"[cpart] remote type={type} key={key} idx={index} not found"); return; }
            Applying = true;
            try
            {
                bool isUnmounted = false;
                try { isUnmounted = reference.IsUnmounted; } catch { }
                if (mounted)
                {
                    ApplyData(reference, data, car);          // paint/tuning/condition BEFORE showing
                    if (isUnmounted)
                    {
                        reference.ShowBySaveGame();
                        reference.Show();
                        MountVisual(reference);               // the renderer re-enable the mesh needs to REAPPEAR
                        try { reference.ShowMountAnimation(); } catch { }
                    }
                    ApplyConditionDust(reference, data);      // condition/dust after it's shown (per reference)
                    CarMecaPlugin.Logger?.LogInfo($"[cpart] applied remote MOUNT type={type} key={key} idx={index} cond={data.Condition:0.00} tuned={data.TunedId}");
                }
                else if (!isUnmounted)
                {
                    reference.HideBySavegame(false, car);
                    CarMecaPlugin.Logger?.LogInfo($"[cpart] applied remote UNMOUNT type={type} key={key} idx={index}");
                }
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[cpart] apply failed: " + e.Message); }
            Applying = false;
            PartSync.RecordMechanical(car, type, key, index, mounted, data);
        }

        // ---- part-data read/apply (each member guarded: a missing interop field is skipped, not fatal) ----
        private static MechData ReadData(PartScript ps)
        {
            var d = MechData.Default;
            try { d.Condition = ps.Condition; } catch { }
            try { d.Dust = ps.Dust; } catch { }
            try { d.Quality = ps.Quality; } catch { }
            try { d.Examined = ps.IsExamined; } catch { }
            try { d.Painted = ps.IsPainted; } catch { }
            try { d.PaintType = (byte)(int)ps.CurrentPaintType; } catch { }
            try { var c = ps.currentColor; d.R = c.r; d.G = c.g; d.B = c.b; d.A = c.a; } catch { }
            try { d.TunedId = ps.tunedID ?? ""; } catch { }
            return d;
        }

        private static void ApplyData(PartScript ps, MechData d, CarLoader cl)
        {
            try { ps.IsExamined = d.Examined; } catch { }
            try { ps.Quality = d.Quality; } catch { }
            try
            {
                if (!string.IsNullOrEmpty(d.TunedId))
                {
                    string cur = ""; try { cur = ps.tunedID ?? ""; } catch { }
                    if (cur != d.TunedId) { string id = ""; try { id = ps.id; } catch { } try { cl?.TunePart(id, d.TunedId); } catch { } }
                }
            }
            catch { }
            try
            {
                ps.IsPainted = d.Painted;
                if (d.Painted)
                {
                    ps.CurrentPaintType = (PaintType)d.PaintType;
                    ps.SetColor(new Color(d.R, d.G, d.B, d.A));
                }
            }
            catch { }
        }

        private static void ApplyConditionDust(PartScript ps, MechData d)
        {
            try { ps.SetCondition(d.Condition); } catch { }
            try { ps.SetConditionNormal(d.Condition); } catch { }
            try { ps.UpdateDust(d.Dust, true); } catch { }
        }

        /// <summary>Make a re-mounted part REAPPEAR (ported from CMS21-Together CustomPartScriptMethod.ShowMounted,
        /// MIT). ShowBySaveGame/Show alone don't re-enable the mesh — this toggles the renderers/colliders and
        /// hides the "removed" placeholders. Done synchronously (the reference's 0.5s coroutine wait is cosmetic).</summary>
        private static void MountVisual(PartScript ps)
        {
            try { ps.IsUnmounted = false; } catch { }
            try { if (ps.ShouldUnmountWith()) { var uw = ps.unmountWith; if (uw != null) for (int i = 0; i < uw.Count; i++) { try { uw[i].MountByGroup(true); } catch { } } } } catch { }
            try { ps.UnblockBlockParts(false); } catch { }
            try { var a = ps.enableOnUnmount; if (a != null) for (int i = 0; i < a.Length; i++) { try { if (a[i] != null) a[i].SetActive(false); } catch { } } } catch { }
            try
            {
                var a = ps.disableOnUnmount;
                if (a != null) for (int i = 0; i < a.Length; i++)
                {
                    var go = a[i]; if (go == null) continue;
                    try { var mr = go.GetComponent<MeshRenderer>(); if (mr != null) mr.enabled = true; } catch { }
                    try { var mc = go.GetComponent<MeshCollider>(); if (mc != null) mc.enabled = true; } catch { }
                }
            }
            catch { }
            try
            {
                var a = ps.hideWhenUnmontingMounting;
                if (a != null) for (int i = 0; i < a.Length; i++)
                {
                    var go = a[i]; if (go == null) continue;
                    try { var rr = go.GetComponent<Renderer>(); if (rr != null) rr.enabled = true; } catch { }
                }
            }
            catch { }
            try { ps.OnMountFinished?.Invoke(); } catch { }
        }
    }
}
