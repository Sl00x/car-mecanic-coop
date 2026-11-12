using System;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;
using UnityEngine;

namespace CarMecaSim.Mp.Sync
{
    /// <summary>
    /// Mechanical part sync by POSITIONAL identity (ported from CMS21-Together, MIT): engine / suspension /
    /// other / driveshaft parts are captured on PartScript.DoMount (mount) and PartScript.Hide (unmount),
    /// identified by (type, key, index) via <see cref="CarPartRefs"/>, and reproduced on the peer with the
    /// game's own visual mount/unmount sequence. Body panels keep the string-key path in <see cref="PartSync"/>.
    /// </summary>
    public static class CarPartSync
    {
        public static bool Applying;   // guard so our visual apply doesn't re-trigger the capture hooks

        private static MpSession S => CarMecaPlugin.Session;
        private static bool Active => S != null && S.InLobby && GameRefs.InGameSession;

        private static CarPartRefs _refs;
        private static CarLoader _refsCar;
        private static int _capN;

        /// <summary>The car to sync parts against. Prefer the loader that OWNS the fired part; else any loader
        /// that actually has parts cached; else the last-car fallback. This is the fix for GetLastCarLoader()
        /// returning an empty '#CarLoaderN' placeholder (cache=0, empty roots).</summary>
        private static CarLoader ResolveCar(PartScript hint)
        {
            var owner = CarRefs.OwnerOf(hint);
            if (owner != null) return owner;
            var withParts = CarRefs.CarWithParts();
            if (withParts != null) return withParts;
            return CarRefs.LoadedCar();
        }

        /// <summary>(Re)build the positional reference table if the car changed.</summary>
        public static CarPartRefs EnsureRefs(CarLoader cl)
        {
            if (cl == null) return null;
            if (_refs == null || _refsCar == null || !_refsCar.Pointer.Equals(cl.Pointer))
            {
                _refs = CarPartRefs.Build(cl);
                _refsCar = cl;
                CarMecaPlugin.Logger?.LogInfo($"[cpart] built refs for current car: {_refs}");
            }
            return _refs;
        }

        public static void Invalidate() { _refs = null; _refsCar = null; }

        // ---- capture (Harmony postfix) ----
        public static void OnDoMount(PartScript ps) => Capture(ps, true);
        public static void OnHide(PartScript ps) => Capture(ps, false);

        private static void Capture(PartScript ps, bool mounted)
        {
            if (Applying || !Active || ps == null) return;
            var car = ResolveCar(ps);

            // Diagnostic (first dozen): the part's real hierarchy + which loader we resolved, so if the owner
            // scheme still misses we can see exactly where the parts live.
            if (_capN++ < 12)
            {
                string path = CarRefs.PathOf(ps);
                string owner = "null"; int ocache = 0;
                try { if (car != null) owner = car.name; } catch { }
                try { var c = CarRefs.Parts(car); ocache = c != null ? c.Length : 0; } catch { }
                CarMecaPlugin.Logger?.LogInfo($"[cpart] capture ps='{path}' resolvedCar='{owner}' cache={ocache} mounted={mounted}");
            }

            var refs = EnsureRefs(car);
            if (refs == null) return;
            if (!refs.Find(ps, out var type, out var key, out var index)) return;   // body parts handled elsewhere
            S.SendPartState(type, key, index, mounted);
            CarMecaPlugin.Logger?.LogInfo($"[cpart] local {(mounted ? "MOUNT" : "UNMOUNT")} type={type} key={key} idx={index} -> broadcast");
        }

        // ---- apply a peer's mechanical part change on OUR car ----
        public static void ApplyRemote(byte type, int key, int index, bool mounted)
        {
            var refs = EnsureRefs(ResolveCar(null));
            if (refs == null) { CarMecaPlugin.Logger?.LogWarning("[cpart] remote part but no car/refs"); return; }
            var reference = refs.Get(type, key, index);
            if (reference == null) { CarMecaPlugin.Logger?.LogWarning($"[cpart] remote type={type} key={key} idx={index} not found"); return; }
            Applying = true;
            try
            {
                bool isUnmounted = false;
                try { isUnmounted = reference.IsUnmounted; } catch { }
                if (mounted && isUnmounted)
                {
                    // visual mount sequence (CMS21-Together PartsUpdater)
                    reference.ShowBySaveGame();
                    reference.Show();
                    try { reference.ShowMountAnimation(); } catch { }
                    CarMecaPlugin.Logger?.LogInfo($"[cpart] applied remote MOUNT type={type} key={key} idx={index}");
                }
                else if (!mounted && !isUnmounted)
                {
                    reference.HideBySavegame(false, _refsCar);
                    CarMecaPlugin.Logger?.LogInfo($"[cpart] applied remote UNMOUNT type={type} key={key} idx={index}");
                }
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning($"[cpart] apply failed: " + e.Message); }
            Applying = false;
        }
    }
}
