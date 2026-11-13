using System;
using System.IO;
using UnityEngine;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace CarMecaSim.Mp.Core
{
    /// <summary>
    /// Remote-player avatar from a prebuilt Unity AssetBundle (<c>player.assets</c>, MIT, from CMS21-Together):
    /// a skinned mechanic mesh with a baked Animator (a 2D locomotion blend tree driven by the floats
    /// "Vertical"/"Horizontal"). This unblocks a real animated body without the Unity-license bundle build our
    /// own <see cref="CharacterModel"/> (mechanic.cmch) needs. The bundle ships next to the mod DLL; if it's
    /// absent or fails to load we return null and RemotePlayer falls back to mechanic.cmch, then primitives.
    /// The bundle's baked material uses a build-time shader that won't resolve in the game's HDRP, so we rebuild
    /// the material with the live HDRP shader + the bundle's own textures (tex_base / tex_normal).
    /// </summary>
    public static class BundleAvatar
    {
        private static bool _tried;
        private static GameObject _prefab;      // loaded once, cloned per peer
        private static Material _mat;

        public static bool Available => Load();

        private static bool Load()
        {
            if (_tried) return _prefab != null;
            _tried = true;
            try
            {
                var dir = Path.GetDirectoryName(typeof(BundleAvatar).Assembly.Location);
                var path = Path.Combine(dir ?? ".", "player.assets");
                if (!File.Exists(path))
                {
                    CarMecaPlugin.Logger?.LogWarning("[avatar] player.assets not found next to the mod; trying mechanic.cmch/primitives.");
                    return false;
                }
                var bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null) { CarMecaPlugin.Logger?.LogWarning("[avatar] AssetBundle.LoadFromFile returned null"); return false; }

                var pObj = bundle.LoadAsset("playerModel", Il2CppType.Of<GameObject>());
                _prefab = pObj != null ? pObj.Cast<GameObject>() : null;
                if (_prefab == null) { CarMecaPlugin.Logger?.LogWarning("[avatar] 'playerModel' not in bundle"); return false; }
                UnityEngine.Object.DontDestroyOnLoad(_prefab);

                // Rebuild the material with the game's own HDRP shader + the bundle's textures.
                Texture baseTex = null, normTex = null;
                try { var t = bundle.LoadAsset("tex_base", Il2CppType.Of<Texture>()); baseTex = t != null ? t.Cast<Texture>() : null; } catch { }
                try { var t = bundle.LoadAsset("tex_normal", Il2CppType.Of<Texture>()); normTex = t != null ? t.Cast<Texture>() : null; } catch { }
                _mat = BuildMaterial(baseTex, normTex);

                CarMecaPlugin.Logger?.LogInfo($"[avatar] player.assets loaded (playerModel, base={(baseTex != null)}, normal={(normTex != null)}).");
                return true;
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[avatar] bundle load error: " + e.Message); return false; }
        }

        private static Material BuildMaterial(Texture baseTex, Texture normTex)
        {
            try
            {
                var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("HDRP/Unlit");
                var m = sh != null ? new Material(sh) : null;
                if (m == null) return null;
                if (baseTex != null) { try { m.SetTexture("_BaseColorMap", baseTex); } catch { } try { m.mainTexture = baseTex; } catch { } }
                if (normTex != null) { try { m.SetTexture("_NormalMap", normTex); } catch { } }
                try { m.SetColor("_BaseColor", Color.white); } catch { }
                return m;
            }
            catch { return null; }
        }

        /// <summary>Clone the mechanic; returns its root and the baked Animator (null = fall back).</summary>
        public static GameObject Instantiate(out Animator animator)
        {
            animator = null;
            if (!Available) return null;
            try
            {
                var go = UnityEngine.Object.Instantiate(_prefab).Cast<GameObject>();
                go.name = "CMS_BundleMechanic";
                go.transform.localScale = new Vector3(0.095f, 0.095f, 0.095f);  // model authored ~10x (fork value)

                if (_mat != null)
                {
                    try
                    {
                        var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                        if (smr != null) smr.material = _mat;
                    }
                    catch { }
                }
                try { animator = go.GetComponent<Animator>() ?? go.GetComponentInChildren<Animator>(); } catch { }
                return go;
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[avatar] instantiate error: " + e.Message); return null; }
        }
    }
}
