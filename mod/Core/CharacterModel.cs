using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CarMecaSim.Mp.Core
{
    /// <summary>
    /// Loads the "mechanics" AssetBundle (CMS 2026/2027 character models ripped + rebuilt for Unity
    /// 2020.3) once, and instantiates a rigged mechanic prefab for a remote player. Materials are
    /// rebuilt with HDRP/Lit at runtime (the game is HDRP; the bundle ships Standard + textures).
    /// Falls back silently (Available == false) so RemotePlayer keeps its primitive avatar if the
    /// bundle is missing.
    /// </summary>
    public static class CharacterModel
    {
        private static bool _tried;
        private static AssetBundle _bundle;
        private static List<GameObject> _prefabs;

        public static bool Available => Load() && _prefabs != null && _prefabs.Count > 0;
        public static int VariantCount => _prefabs != null ? _prefabs.Count : 0;

        private static bool Load()
        {
            if (_tried) return _bundle != null;
            _tried = true;
            try
            {
                var dir = Path.GetDirectoryName(typeof(CharacterModel).Assembly.Location);
                var path = Path.Combine(dir ?? ".", "mechanics");
                if (!File.Exists(path))
                {
                    CarMecaPlugin.Logger?.LogWarning("[char] bundle 'mechanics' not found next to the mod; using primitive avatars.");
                    return false;
                }
                _bundle = AssetBundle.LoadFromFile(path);
                if (_bundle == null) { CarMecaPlugin.Logger?.LogWarning("[char] AssetBundle.LoadFromFile returned null"); return false; }

                _prefabs = new List<GameObject>();
                foreach (var n in _bundle.GetAllAssetNames())
                {
                    if (!n.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                    var go = _bundle.LoadAsset<GameObject>(n);
                    if (go != null) _prefabs.Add(go);
                }
                CarMecaPlugin.Logger?.LogInfo($"[char] loaded bundle with {_prefabs.Count} mechanic prefab(s).");
                return true;
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[char] bundle load error: " + e.Message); return false; }
        }

        /// <summary>Instantiate a mechanic; variant picks one of the outfits deterministically.</summary>
        public static GameObject Instantiate(int variant, out Animator animator)
        {
            animator = null;
            if (!Available) return null;
            try
            {
                int i = ((variant % _prefabs.Count) + _prefabs.Count) % _prefabs.Count;
                var go = UnityEngine.Object.Instantiate(_prefabs[i]);
                FixMaterials(go);
                animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) { animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
                return go;
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[char] instantiate error: " + e.Message); return null; }
        }

        private static Shader _hdrpLit;

        private static void FixMaterials(GameObject go)
        {
            try
            {
                if (_hdrpLit == null) _hdrpLit = Shader.Find("HDRP/Lit");
                if (_hdrpLit == null) return;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.materials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var src = mats[i];
                        if (src == null) continue;
                        Texture albedo = src.mainTexture;
                        if (albedo == null && src.HasProperty("_MainTex")) albedo = src.GetTexture("_MainTex");
                        var m = new Material(_hdrpLit);
                        if (albedo != null) { try { m.SetTexture("_BaseColorMap", albedo); } catch { } m.mainTexture = albedo; }
                        if (src.HasProperty("_BumpMap"))
                        {
                            var nrm = src.GetTexture("_BumpMap");
                            if (nrm != null) { try { m.SetTexture("_NormalMap", nrm); } catch { } }
                        }
                        mats[i] = m;
                    }
                    r.materials = mats;
                }
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[char] material fix: " + e.Message); }
        }
    }
}
