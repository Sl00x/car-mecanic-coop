using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using CarMecaSim.Mp.Core;

namespace CarMecaSim.Mp.Game
{
    /// <summary>
    /// Rebuild a Car Mechanic Simulator 2027 scene at RUNTIME inside CMS2021 (2027 = Unity 6, 2021 = Unity 2020.3,
    /// so AssetBundles are incompatible — reconstructed from raw geometry + textures extracted with UnityPy).
    /// For a FULL-town map (thousands of instances / tens of millions of verts) the load is SPREAD over frames
    /// (driven by <see cref="Tick"/> from the plugin Update) so the game never freezes. On completion the CMS2021
    /// world is auto-hidden so only the 2027 map is visible. Files: garage2027/meshes/*.cmsh (pos+uv+tris),
    /// textures/*.png, materials.txt (matId r g b a texIdx sclX sclY offX offY transp alpha), scene.manifest
    /// (meshId matId + 12 affine floats). Toggle from the F8 trainer.
    /// </summary>
    public static class GarageProp
    {
        private enum Phase { Idle, Meshes, Textures, Materials, Combine, Colliders, Finish }
        private const int BatchSize = 256;
        private const int MeshesPerTick = 40, TexPerTick = 6, BatchesPerTick = 3, CollidersPerTick = 1;

        private static Phase _phase = Phase.Idle;
        private static GameObject _root;
        private static Shader _hdrp;

        // job state (spread load)
        private static string _dir;
        private static Vector3 _centre;
        private static Queue<string> _meshQ, _texQ;
        private static Dictionary<int, Mesh> _meshes;
        private static Dictionary<int, Texture2D> _textures;
        private static Dictionary<int, Material> _materials;
        private static HashSet<int> _transparent;
        private static Material _fallback;
        private static Dictionary<int, List<(int mesh, Matrix4x4 m)>> _byMat;
        private static Queue<(int mat, int start)> _combineQ;
        private static int _built, _batches, _totalInst;
        private static float _lastReHide;

        public static bool AnySpawned => _root != null;
        public static bool Loading => _phase != Phase.Idle;
        public static int Progress => _totalInst > 0 ? (int)(100f * _built / _totalInst) : 0;

        private static string Dir()
        {
            var baseDir = Path.GetDirectoryName(typeof(GarageProp).Assembly.Location) ?? ".";
            return Path.Combine(baseDir, "garage2027");
        }

        /// <summary>Toggle: start building the 2027 map (spread over frames), or tear it down.</summary>
        public static int SpawnTest()
        {
            if (_root != null || Loading) { Clear(); return 0; }
            _dir = Dir();
            var manifest = Path.Combine(_dir, "scene.manifest");
            if (!File.Exists(manifest)) { CarMecaPlugin.Logger?.LogWarning("[map2027] no scene.manifest"); return 0; }

            var eye = GameRefs.Eye;
            Vector3 origin = eye != null ? eye.position + Vector3.down * 1.6f : Vector3.zero;
            float yaw = eye != null ? eye.eulerAngles.y : 0f;

            _byMat = new Dictionary<int, List<(int, Matrix4x4)>>();
            Vector3 sum = Vector3.zero; int cN = 0;
            foreach (var ln in File.ReadAllLines(manifest))
            {
                var p = ln.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 14) continue;
                int mesh = I(p[0]); int mat = I(p[1]);
                var m = new Matrix4x4();
                m.m00 = F(p[2]); m.m01 = F(p[3]); m.m02 = F(p[4]); m.m03 = F(p[5]);
                m.m10 = F(p[6]); m.m11 = F(p[7]); m.m12 = F(p[8]); m.m13 = F(p[9]);
                m.m20 = F(p[10]); m.m21 = F(p[11]); m.m22 = F(p[12]); m.m23 = F(p[13]);
                m.m30 = 0; m.m31 = 0; m.m32 = 0; m.m33 = 1;
                sum += new Vector3(m.m03, 0, m.m23); cN++;
                if (!_byMat.TryGetValue(mat, out var l)) { l = new(); _byMat[mat] = l; }
                l.Add((mesh, m));
            }
            _centre = cN > 0 ? sum / cN : Vector3.zero;
            _totalInst = cN; _built = 0; _batches = 0;

            _meshes = new(); _textures = new();
            var md = Path.Combine(_dir, "meshes"); var xd = Path.Combine(_dir, "textures");
            _meshQ = new Queue<string>(Directory.Exists(md) ? Directory.GetFiles(md, "*.cmsh") : Array.Empty<string>());
            _texQ = new Queue<string>(Directory.Exists(xd) ? Directory.GetFiles(xd, "*.png") : Array.Empty<string>());

            _root = new GameObject("CMS2027_Scene");
            _root.transform.position = origin;
            _root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            // flat walkable ground at the player's feet (full per-mesh colliders over 32M verts choke the physics and
            // you can't move). This lets you roam the city; buildings stay walk-through.
            try
            {
                var ground = new GameObject("CMS2027_Ground");
                ground.transform.SetParent(_root.transform, false);
                ground.transform.localPosition = new Vector3(0f, -1f, 0f);
                var bc = ground.AddComponent<BoxCollider>();
                bc.size = new Vector3(4000f, 2f, 4000f);
            }
            catch { }
            _phase = Phase.Meshes;
            CarMecaPlugin.Logger?.LogInfo($"[map2027] building: {cN} instances, {_byMat.Count} materials, {_meshQ.Count} meshes, {_texQ.Count} textures (spread load)");
            return cN;
        }

        /// <summary>Per-frame: advance the spread load. Called from the plugin Update.</summary>
        public static void Tick()
        {
            // once built & 2021 hidden, KEEP it hidden every frame (fight the game re-enabling its renderers for
            // culling/LOD), and periodically catch newly-spawned objects (traffic cars, etc.).
            if (_phase == Phase.Idle)
            {
                if (_hiddenR != null && _root != null)
                {
                    var lr = _hiddenR; for (int i = 0; i < lr.Count; i++) { var r = lr[i]; try { if (r != null && r.enabled) r.enabled = false; } catch { } }
                    var lc = _hiddenC; if (lc != null) for (int i = 0; i < lc.Count; i++) { var c = lc[i]; try { if (c != null && c.enabled) c.enabled = false; } catch { } }
                    if (Time.unscaledTime - _lastReHide > 3f) { _lastReHide = Time.unscaledTime; ReHideNew(); }
                }
                return;
            }
            try
            {
                switch (_phase)
                {
                    case Phase.Meshes:
                        for (int i = 0; i < MeshesPerTick && _meshQ.Count > 0; i++)
                        {
                            var f = _meshQ.Dequeue();
                            if (int.TryParse(Path.GetFileNameWithoutExtension(f), out var id)) { var m = LoadMesh(f); if (m != null) _meshes[id] = m; }
                        }
                        if (_meshQ.Count == 0) _phase = Phase.Textures;
                        break;

                    case Phase.Textures:
                        for (int i = 0; i < TexPerTick && _texQ.Count > 0; i++)
                        {
                            var f = _texQ.Dequeue();
                            if (!int.TryParse(Path.GetFileNameWithoutExtension(f), out var id)) continue;
                            try
                            {
                                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                                if (ImageConversion.LoadImage(tex, (Il2CppStructArray<byte>)File.ReadAllBytes(f))) _textures[id] = tex;
                            }
                            catch { }
                        }
                        if (_texQ.Count == 0) _phase = Phase.Materials;
                        break;

                    case Phase.Materials:
                    {
                        (_materials, _transparent) = LoadMaterials(Path.Combine(_dir, "materials.txt"), _textures);
                        _fallback = BuildMat(new Color(0.72f, 0.73f, 0.75f, 1f), null, Vector2.one, Vector2.zero, false);
                        _combineQ = new Queue<(int, int)>();
                        foreach (var kv in _byMat)
                        {
                            if (_transparent.Contains(kv.Key)) continue;
                            for (int s = 0; s < kv.Value.Count; s += BatchSize) _combineQ.Enqueue((kv.Key, s));
                        }
                        _phase = Phase.Combine;
                        break;
                    }

                    case Phase.Combine:
                        for (int b = 0; b < BatchesPerTick && _combineQ.Count > 0; b++)
                        {
                            var (mat, start) = _combineQ.Dequeue();
                            BuildBatch(mat, start);
                        }
                        if (_combineQ.Count == 0) { Hide2021(); _phase = Phase.Finish; }   // hide 2021 as soon as the map is built
                        break;

                    case Phase.Finish:
                        Hide2021();                                  // safety re-hide (no-op if already hidden at combine end)
                        CarMecaPlugin.Logger?.LogInfo($"[map2027] done: {_built} instances in {_batches} batches, {_meshes.Count} meshes, {_textures.Count} textures");
                        _phase = Phase.Idle;
                        _meshQ = null; _texQ = null; _byMat = null; _combineQ = null;
                        break;
                }
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[map2027] tick(" + _phase + "): " + e.Message); _phase = Phase.Idle; }
        }

        private static void BuildBatch(int mat, int start)
        {
            if (!_byMat.TryGetValue(mat, out var list)) return;
            var material = _materials.TryGetValue(mat, out var mm) ? mm : _fallback;
            int end = Math.Min(start + BatchSize, list.Count);
            var cis = new List<CombineInstance>(end - start);
            for (int i = start; i < end; i++)
            {
                if (!_meshes.TryGetValue(list[i].mesh, out var mesh)) continue;
                var m = list[i].m; m.m03 -= _centre.x; m.m23 -= _centre.z;
                cis.Add(new CombineInstance { mesh = mesh, transform = m });
            }
            if (cis.Count == 0) return;
            try
            {
                var arr = new Il2CppStructArray<CombineInstance>(cis.Count);
                for (int i = 0; i < cis.Count; i++) arr[i] = cis[i];
                var cm = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                cm.CombineMeshes(arr, true, true);
                cm.RecalculateBounds();
                var go = new GameObject("b" + _batches);
                go.transform.SetParent(_root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = cm;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                _batches++; _built += cis.Count;
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[map2027] combine: " + e.Message); }
        }

        public static void Clear()
        {
            Restore2021();
            _phase = Phase.Idle;
            _meshQ = null; _texQ = null; _byMat = null; _combineQ = null; _meshes = null; _textures = null; _materials = null;
            try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
            _root = null;
        }

        // ---- hide the CMS2021 world (renderers + colliders/barriers + terrains + traffic) ----
        private static List<Renderer> _hiddenR;
        private static List<Behaviour> _hiddenT;
        private static List<Collider> _hiddenC;
        public static bool Map2021Hidden => _hiddenR != null;
        public static int HiddenCount => (_hiddenR?.Count ?? 0) + (_hiddenC?.Count ?? 0);
        public static void ToggleHide2021() { if (_hiddenR != null) Restore2021(); else Hide2021(); }

        private static bool Mine(Transform t) { try { return _root != null && t.IsChildOf(_root.transform); } catch { return false; } }

        private static void Hide2021()
        {
            if (_hiddenR != null || _root == null) return;
            _hiddenR = new(); _hiddenT = new(); _hiddenC = new();
            try
            {
                var rs = UnityEngine.Object.FindObjectsOfType<Renderer>();
                for (int i = 0; i < rs.Length; i++)
                { var r = rs[i]; if (r == null || Mine(r.transform)) continue; try { if (r.enabled) { r.enabled = false; _hiddenR.Add(r); } } catch { } }
                // colliders: kill the 2021 invisible barriers / walls (the 2027 map has its own MeshColliders)
                var cs = UnityEngine.Object.FindObjectsOfType<Collider>();
                for (int i = 0; i < cs.Length; i++)
                { var c = cs[i]; if (c == null || Mine(c.transform)) continue; try { if (c.enabled) { c.enabled = false; _hiddenC.Add(c); } } catch { } }
                var ter = UnityEngine.Object.FindObjectsOfType<Terrain>();
                for (int i = 0; i < ter.Length; i++) { var t = ter[i]; if (t == null) continue; try { if (t.enabled) { t.enabled = false; _hiddenT.Add(t); } } catch { } }
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[map2027] hide: " + e.Message); }
            _lastReHide = Time.unscaledTime;
            CarMecaPlugin.Logger?.LogInfo($"[map2027] 2021 masqué: {_hiddenR.Count} renderers, {_hiddenC.Count} colliders, {_hiddenT.Count} terrains");
        }

        /// <summary>Re-hide anything in the 2021 world that re-appeared since (dynamically spawned traffic cars, etc.).</summary>
        private static void ReHideNew()
        {
            try
            {
                var rs = UnityEngine.Object.FindObjectsOfType<Renderer>();
                for (int i = 0; i < rs.Length; i++)
                { var r = rs[i]; if (r == null || !r.enabled || Mine(r.transform)) continue; r.enabled = false; _hiddenR.Add(r); }
                var cs = UnityEngine.Object.FindObjectsOfType<Collider>();
                for (int i = 0; i < cs.Length; i++)
                { var c = cs[i]; if (c == null || !c.enabled || Mine(c.transform)) continue; c.enabled = false; _hiddenC.Add(c); }
            }
            catch { }
        }

        private static void Restore2021()
        {
            if (_hiddenR != null) foreach (var r in _hiddenR) { try { if (r != null) r.enabled = true; } catch { } }
            if (_hiddenC != null) foreach (var c in _hiddenC) { try { if (c != null) c.enabled = true; } catch { } }
            if (_hiddenT != null) foreach (var t in _hiddenT) { try { if (t != null) t.enabled = true; } catch { } }
            _hiddenR = null; _hiddenC = null; _hiddenT = null;
        }

        private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);
        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private static Mesh LoadMesh(string path)
        {
            try
            {
                using var r = new BinaryReader(File.OpenRead(path));
                if (new string(r.ReadChars(4)) != "CMSH") return null;
                int ver = r.ReadInt32(); int vtx = r.ReadInt32();
                var vr = new Il2CppStructArray<Vector3>(vtx);
                for (int i = 0; i < vtx; i++) vr[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                Il2CppStructArray<Vector3> nr = null; Il2CppStructArray<Vector2> uv = null;
                if (ver == 1) { nr = new Il2CppStructArray<Vector3>(vtx); for (int i = 0; i < vtx; i++) nr[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
                if (ver == 3) { uv = new Il2CppStructArray<Vector2>(vtx); for (int i = 0; i < vtx; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle()); }
                int ic = r.ReadInt32();
                var tr = new Il2CppStructArray<int>(ic);
                for (int i = 0; i < ic; i++) tr[i] = r.ReadInt32();
                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.vertices = vr; if (uv != null) mesh.uv = uv; mesh.triangles = tr;
                if (nr != null) mesh.normals = nr; else mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
            catch { return null; }
        }

        private static (Dictionary<int, Material>, HashSet<int>) LoadMaterials(string path, Dictionary<int, Texture2D> tex)
        {
            var d = new Dictionary<int, Material>(); var transp = new HashSet<int>();
            if (!File.Exists(path)) return (d, transp);
            foreach (var ln in File.ReadAllLines(path))
            {
                var p = ln.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 6) continue;
                int id = I(p[0]);
                var col = new Color(F(p[1]), F(p[2]), F(p[3]), F(p[4]));
                int ti = I(p[5]);
                var scale = p.Length >= 10 ? new Vector2(F(p[6]), F(p[7])) : Vector2.one;
                var offset = p.Length >= 10 ? new Vector2(F(p[8]), F(p[9])) : Vector2.zero;
                bool tp = p.Length >= 11 && p[10] == "1";
                bool al = p.Length >= 12 && p[11] == "1";
                if (tp && ti < 0) { transp.Add(id); continue; }
                Texture2D t = (ti >= 0 && tex.TryGetValue(ti, out var tt)) ? tt : null;
                d[id] = BuildMat(col, t, scale, offset, al || tp);
            }
            return (d, transp);
        }

        private static Material BuildMat(Color col, Texture2D tex, Vector2 scale, Vector2 offset, bool alphaClip)
        {
            try
            {
                if (_hdrp == null) _hdrp = Shader.Find("HDRP/Lit");
                var m = _hdrp != null ? new Material(_hdrp) : new Material(Shader.Find("Standard"));
                if (tex != null)
                {
                    try { m.SetTexture("_BaseColorMap", tex); } catch { }
                    try { m.mainTexture = tex; } catch { }
                    try { m.SetTextureScale("_BaseColorMap", scale); m.SetTextureOffset("_BaseColorMap", offset); } catch { }
                    try { m.mainTextureScale = scale; m.mainTextureOffset = offset; } catch { }
                    col = new Color(1f, 1f, 1f, 1f);
                    if (alphaClip)
                    {
                        try
                        {
                            m.SetFloat("_AlphaCutoffEnable", 1f); m.SetFloat("_AlphaCutoff", 0.4f); m.SetFloat("_Cutoff", 0.4f);
                            m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450;
                        }
                        catch { }
                    }
                }
                else if (col.r + col.g + col.b < 0.1f || (col.r > 0.92f && col.g > 0.92f && col.b > 0.92f))
                    col = new Color(0.72f, 0.73f, 0.75f, 1f);   // degenerate black OR pure-white (DefaultHDMaterial) -> neutral grey
                try { m.SetColor("_BaseColor", col); } catch { }
                try { m.color = col; } catch { }
                try
                {
                    m.SetFloat("_DoubleSidedEnable", 1f); m.SetFloat("_DoubleSidedNormalMode", 1f);
                    m.SetFloat("_CullMode", 0f); m.SetFloat("_CullModeForward", 0f);
                    m.EnableKeyword("_DOUBLESIDED_ON"); m.doubleSidedGI = true;
                }
                catch { }
                return m;
            }
            catch { return new Material(Shader.Find("Standard")); }
        }
    }
}
