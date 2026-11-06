using System;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace CarMecaSim.Mp.Game
{
    /// <summary>Access to the car currently being worked on, and its parts, for sync.</summary>
    public static class CarRefs
    {
        private static GameManager _gm;
        public static GameManager Mgr()
        {
            try { if (_gm == null) _gm = UnityEngine.Object.FindObjectOfType<GameManager>(); } catch { }
            return _gm;
        }
        public static GameDataManager Gdm()
        {
            try { var gm = Mgr(); return gm != null ? gm.GameDataManager : null; } catch { return null; }
        }

        public static CarLoader CurrentCar()
        {
            try { var gs = GameScript.Get(); return gs != null ? gs.GetLastCarLoader() : null; }
            catch { return null; }
        }

        public static Il2CppReferenceArray<PartScript> Parts(CarLoader car)
        {
            if (car == null) return null;
            try
            {
                var cache = car.GetPartScriptCache();
                if (cache == null || cache.Length == 0) { car.CachePartScripts(); cache = car.GetPartScriptCache(); }
                return cache;
            }
            catch { return null; }
        }

        public static string PathOf(PartScript p)
        {
            try { return p != null ? p.GetGameObjectPath() : null; } catch { return null; }
        }

        /// <summary>Find a part on the current car by its (machine-stable) GameObject path.</summary>
        public static PartScript FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var parts = Parts(CurrentCar());
            if (parts == null) return null;
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                if (p != null && PathOf(p) == path) return p;
            }
            return null;
        }

        /// <summary>Find which part owns a bolt (MountObject) and the bolt index within it.</summary>
        public static bool FindBolt(MountObject mo, out PartScript owner, out int index)
        {
            owner = null; index = -1;
            if (mo == null) return false;
            var parts = Parts(CurrentCar());
            if (parts == null) return false;
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                if (p == null) continue;
                var mos = p.MountObjects;
                if (mos == null) continue;
                for (int j = 0; j < mos.Length; j++)
                {
                    var b = mos[j];
                    if (b != null && b.Pointer == mo.Pointer) { owner = p; index = j; return true; }
                }
            }
            return false;
        }
    }
}
