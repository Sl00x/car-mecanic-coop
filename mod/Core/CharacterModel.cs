using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace CarMecaSim.Mp.Core
{
    /// <summary>
    /// Builds the real CMS 2026/2027 mechanic at runtime from a compact binary (mechanic.cmch:
    /// skeleton + skinned mesh + diffuse), so no Unity Editor / AssetBundle / license is needed.
    /// Reconstructs a SkinnedMeshRenderer with its bones and bindposes; RemotePlayer then poses it
    /// (arms down from the T-pose bind) and animates it procedurally. Falls back to primitives if the
    /// file is absent or anything fails.
    /// </summary>
    public static class CharacterModel
    {
        private static bool _tried;
        private static CharData _data;   // parsed once, instanced per peer

        public static bool Available => Load() && _data != null;

        private sealed class CharData
        {
            public float Scale;
            public int BoneCount, RootIndex, VertexCount;
            public string[] BoneName;
            public int[] BoneParent;
            public Vector3[] BonePos, BoneScale;
            public Quaternion[] BoneRot;
            public Matrix4x4[] BindPose;
            public Vector3[] Verts, Norms;
            public Vector2[] UV;
            public int[,] BoneIdx;        // [v,4]
            public float[,] BoneWt;       // [v,4]
            public int[] Tris;
            public byte[] DiffusePng;
        }

        private static bool Load()
        {
            if (_tried) return _data != null;
            _tried = true;
            try
            {
                var dir = Path.GetDirectoryName(typeof(CharacterModel).Assembly.Location);
                var path = Path.Combine(dir ?? ".", "mechanic.cmch");
                if (!File.Exists(path))
                {
                    CarMecaPlugin.Logger?.LogWarning("[char] mechanic.cmch not found next to the mod; using primitive avatars.");
                    return false;
                }
                using var r = new BinaryReader(File.OpenRead(path));
                if (new string(r.ReadChars(4)) != "CMCH") { CarMecaPlugin.Logger?.LogWarning("[char] bad magic"); return false; }
                r.ReadInt32();                              // version
                var d = new CharData { Scale = r.ReadSingle() };
                d.BoneCount = r.ReadInt32();
                d.BoneName = new string[d.BoneCount];
                d.BoneParent = new int[d.BoneCount];
                d.BonePos = new Vector3[d.BoneCount];
                d.BoneRot = new Quaternion[d.BoneCount];
                d.BoneScale = new Vector3[d.BoneCount];
                d.BindPose = new Matrix4x4[d.BoneCount];
                for (int i = 0; i < d.BoneCount; i++)
                {
                    int len = r.ReadUInt16();
                    d.BoneName[i] = System.Text.Encoding.UTF8.GetString(r.ReadBytes(len));
                    d.BoneParent[i] = r.ReadInt32();
                    d.BonePos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    d.BoneRot[i] = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    d.BoneScale[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    var m = new Matrix4x4();
                    float[] e = new float[16];
                    for (int k = 0; k < 16; k++) e[k] = r.ReadSingle();   // row-major e00..e33
                    for (int c = 0; c < 4; c++) m.SetColumn(c, new Vector4(e[0 * 4 + c], e[1 * 4 + c], e[2 * 4 + c], e[3 * 4 + c]));
                    d.BindPose[i] = m;
                }
                d.RootIndex = r.ReadInt32();
                int vc = d.VertexCount = r.ReadInt32();
                d.Verts = new Vector3[vc]; d.Norms = new Vector3[vc]; d.UV = new Vector2[vc];
                for (int i = 0; i < vc; i++) d.Verts[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < vc; i++) d.Norms[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < vc; i++) d.UV[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                d.BoneIdx = new int[vc, 4]; d.BoneWt = new float[vc, 4];
                for (int i = 0; i < vc; i++) for (int k = 0; k < 4; k++) d.BoneIdx[i, k] = r.ReadInt32();
                for (int i = 0; i < vc; i++) for (int k = 0; k < 4; k++) d.BoneWt[i, k] = r.ReadSingle();
                int ic = r.ReadInt32();
                d.Tris = new int[ic];
                for (int i = 0; i < ic; i++) d.Tris[i] = r.ReadInt32();
                int png = r.ReadInt32();
                d.DiffusePng = png > 0 ? r.ReadBytes(png) : null;

                _data = d;
                CarMecaPlugin.Logger?.LogInfo($"[char] loaded mechanic.cmch: {d.BoneCount} bones, {vc} verts, {ic / 3} tris.");
                return true;
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[char] load error: " + e); return false; }
        }

        private static Shader _hdrpLit;
        private static Texture2D _diffuse;

        /// <summary>Instantiate the mechanic; returns the root GameObject and a rig for animation.</summary>
        public static GameObject Instantiate(int variant, out CharacterRig rig)
        {
            rig = null;
            if (!Available) return null;
            try
            {
                var d = _data;
                var root = new GameObject("CMS_Mechanic");
                var bones = new Transform[d.BoneCount];
                for (int i = 0; i < d.BoneCount; i++)
                {
                    var go = new GameObject(d.BoneName[i]);
                    bones[i] = go.transform;
                }
                for (int i = 0; i < d.BoneCount; i++)
                {
                    var parent = d.BoneParent[i] >= 0 ? bones[d.BoneParent[i]] : root.transform;
                    bones[i].SetParent(parent, false);
                    bones[i].localPosition = d.BonePos[i];
                    bones[i].localRotation = d.BoneRot[i];
                    bones[i].localScale = d.BoneScale[i];
                }

                // build the skinned mesh
                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                int vc = d.VertexCount;
                var vr = new Il2CppStructArray<Vector3>(vc);
                var nr = new Il2CppStructArray<Vector3>(vc);
                var ur = new Il2CppStructArray<Vector2>(vc);
                var bw = new Il2CppStructArray<BoneWeight>(vc);
                for (int i = 0; i < vc; i++)
                {
                    vr[i] = d.Verts[i]; nr[i] = d.Norms[i]; ur[i] = d.UV[i];
                    var w = new BoneWeight
                    {
                        boneIndex0 = d.BoneIdx[i, 0], boneIndex1 = d.BoneIdx[i, 1],
                        boneIndex2 = d.BoneIdx[i, 2], boneIndex3 = d.BoneIdx[i, 3],
                        weight0 = d.BoneWt[i, 0], weight1 = d.BoneWt[i, 1],
                        weight2 = d.BoneWt[i, 2], weight3 = d.BoneWt[i, 3],
                    };
                    bw[i] = w;
                }
                mesh.vertices = vr; mesh.normals = nr; mesh.uv = ur; mesh.boneWeights = bw;
                var tris = new Il2CppStructArray<int>(d.Tris.Length);
                for (int i = 0; i < d.Tris.Length; i++) tris[i] = d.Tris[i];
                mesh.triangles = tris;
                var bp = new Il2CppStructArray<Matrix4x4>(d.BoneCount);
                for (int i = 0; i < d.BoneCount; i++) bp[i] = d.BindPose[i];
                mesh.bindposes = bp;
                mesh.RecalculateBounds();

                var meshGo = new GameObject("CharacterMesh");
                meshGo.transform.SetParent(root.transform, false);
                var smr = meshGo.AddComponent<SkinnedMeshRenderer>();
                var boneArr = new Il2CppReferenceArray<Transform>(d.BoneCount);
                for (int i = 0; i < d.BoneCount; i++) boneArr[i] = bones[i];
                smr.bones = boneArr;
                smr.rootBone = bones[d.RootIndex];
                smr.sharedMesh = mesh;
                smr.updateWhenOffscreen = true;
                smr.material = BuildMaterial(d);

                root.transform.localScale = new Vector3(d.Scale, d.Scale, d.Scale);

                var map = new Dictionary<string, Transform>(d.BoneCount);
                for (int i = 0; i < d.BoneCount; i++) map[d.BoneName[i]] = bones[i];
                rig = new CharacterRig(map);
                return root;
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[char] instantiate error: " + e); return null; }
        }

        private static Material BuildMaterial(CharData d)
        {
            try
            {
                if (_hdrpLit == null) _hdrpLit = Shader.Find("HDRP/Lit");
                if (_diffuse == null && d.DiffusePng != null)
                {
                    _diffuse = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    ImageConversion.LoadImage(_diffuse, (Il2CppStructArray<byte>)d.DiffusePng);
                }
                var m = _hdrpLit != null ? new Material(_hdrpLit) : new Material(Shader.Find("Standard"));
                if (_diffuse != null)
                {
                    try { m.SetTexture("_BaseColorMap", _diffuse); } catch { }
                    m.mainTexture = _diffuse;
                }
                try { m.SetColor("_BaseColor", Color.white); } catch { }
                return m;
            }
            catch { return new Material(Shader.Find("Standard")); }
        }
    }

    /// <summary>Named-bone access + simple procedural animation over the bind (T-)pose.</summary>
    public sealed class CharacterRig
    {
        private readonly Dictionary<string, Transform> _bones;
        private readonly Quaternion _bindUpperL, _bindUpperR, _bindLowerL, _bindLowerR;
        private readonly Transform _upperL, _upperR, _lowerL, _lowerR, _spine;
        private readonly Quaternion _bindSpine;
        private bool _posed;

        public CharacterRig(Dictionary<string, Transform> bones)
        {
            _bones = bones;
            _upperL = Find("CC_Base_L_Upperarm"); _upperR = Find("CC_Base_R_Upperarm");
            _lowerL = Find("CC_Base_L_Forearm");  _lowerR = Find("CC_Base_R_Forearm");
            _spine  = Find("CC_Base_Spine01") ?? Find("CC_Base_Spine02") ?? Find("CC_Base_Waist");
            _bindUpperL = _upperL ? _upperL.localRotation : Quaternion.identity;
            _bindUpperR = _upperR ? _upperR.localRotation : Quaternion.identity;
            _bindLowerL = _lowerL ? _lowerL.localRotation : Quaternion.identity;
            _bindLowerR = _lowerR ? _lowerR.localRotation : Quaternion.identity;
            _bindSpine  = _spine  ? _spine.localRotation  : Quaternion.identity;
        }

        private Transform Find(string n) => _bones.TryGetValue(n, out var t) ? t : null;

        /// <summary>Bring the arms down out of the T-pose, and idle/work animate.</summary>
        public void Animate(float speed, bool working, float t)
        {
            // T-pose arms point out along ±X; rotate ~75° down around Z (local) to rest at the sides.
            float idle = Mathf.Sin(t * 1.6f) * 3f;
            float work = working ? Mathf.Sin(t * 8f) * 22f : 0f;
            if (_upperL) _upperL.localRotation = _bindUpperL * Quaternion.Euler(0f, 0f, 72f + idle);
            if (_upperR) _upperR.localRotation = _bindUpperR * Quaternion.Euler(0f, 0f, -72f - idle);
            if (_lowerL) _lowerL.localRotation = _bindLowerL * Quaternion.Euler(0f, working ? 35f + work : 10f, 0f);
            if (_lowerR) _lowerR.localRotation = _bindLowerR * Quaternion.Euler(0f, working ? -35f - work : -10f, 0f);
            if (_spine)  _spine.localRotation  = _bindSpine  * Quaternion.Euler(working ? 14f : idle * 0.4f, 0f, 0f);
            _posed = true;
        }

        public bool Ready => _posed || _upperL != null;
    }
}
