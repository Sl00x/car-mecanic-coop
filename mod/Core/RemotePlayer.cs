using System;
using UnityEngine;
using Steamworks;

namespace CarMecaSim.Mp.Core
{
    /// <summary>
    /// A visible 3D avatar for another player (the game has no body mesh, so we build a simple
    /// mechanic humanoid out of primitives): head, torso, arms, legs, a cap, per-player colour.
    /// Oriented by yaw; head tilts by pitch; idle bob + arm swing when the peer is working.
    /// </summary>
    public sealed class RemotePlayer
    {
        public CSteamID Id;
        public byte PlayerId;
        public string Name;

        public Vector3 TargetPos;   // eye/head position
        public float TargetYaw;
        public float TargetPitch;
        public byte Tool;
        public byte Mode;
        public float LastUpdate;

        private GameObject _root, _head, _torso, _armL, _armR;
        private GameObject _body;          // ripped mechanic model (when mechanic.cmch is present)
        private CharacterRig _rig;         // its skeleton, for procedural posing/animation
        private Transform _tr;
        private Vector3 _pos;
        private float _yaw;

        // eye (presence) position is ~this high above the feet, where the character model is rooted.
        private const float EyeHeight = 1.65f;

        public Vector3 WorldPos => _tr != null ? _tr.position : TargetPos;

        public void EnsureSpawned()
        {
            if (_root != null) return;
            try
            {
                _root = new GameObject("CMS_Avatar_" + PlayerId);
                _tr = _root.transform;
                _pos = TargetPos; _yaw = TargetYaw;

                // Prefer the real mechanic model (mechanic.cmch); fall back to primitives.
                _body = CharacterModel.Instantiate(PlayerId, out _rig);
                if (_body != null)
                {
                    var bt = _body.transform;
                    bt.SetParent(_tr, false);
                    bt.localPosition = new Vector3(0f, -EyeHeight, 0f);  // model rooted at feet; eye is up high
                    bt.localRotation = Quaternion.identity;
                    return;
                }

                BuildPrimitiveAvatar();
            }
            catch { /* rendering unavailable */ }
        }

        private void BuildPrimitiveAvatar()
        {
            var col = ColorFor(PlayerId);
            var skin = new Color(0.86f, 0.70f, 0.56f);
            var dark = new Color(0.14f, 0.14f, 0.16f);

            // head is near the eye position; body hangs below.
            _head  = Part(PrimitiveType.Sphere,   new Vector3(0f, -0.14f, 0.04f), new Vector3(0.26f, 0.26f, 0.26f), skin);
            Part(PrimitiveType.Cube,   new Vector3(0f, -0.02f, 0.02f), new Vector3(0.30f, 0.14f, 0.30f), col, _head.transform); // cap
            _torso = Part(PrimitiveType.Capsule, new Vector3(0f, -0.56f, 0f), new Vector3(0.42f, 0.34f, 0.30f), col);
            _armL  = Part(PrimitiveType.Capsule, new Vector3(-0.27f, -0.52f, 0.02f), new Vector3(0.13f, 0.26f, 0.13f), col);
            _armR  = Part(PrimitiveType.Capsule, new Vector3( 0.27f, -0.52f, 0.02f), new Vector3(0.13f, 0.26f, 0.13f), col);
            Part(PrimitiveType.Capsule, new Vector3(-0.12f, -1.04f, 0f), new Vector3(0.15f, 0.30f, 0.15f), dark); // legs
            Part(PrimitiveType.Capsule, new Vector3( 0.12f, -1.04f, 0f), new Vector3(0.15f, 0.30f, 0.15f), dark);
        }

        private static Shader _hdrpLit;

        private GameObject Part(PrimitiveType type, Vector3 localPos, Vector3 scale, Color color, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type);
            var c = go.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.Destroy(c);
            var t = go.transform;
            t.SetParent(parent != null ? parent : _root.transform, false);
            t.localPosition = localPos;
            t.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (r != null) ApplyColor(r, color);
            return go;
        }

        // CMS2021 renders with HDRP: the default primitive (Standard shader) shows up magenta/invisible.
        // Build an HDRP/Lit material and set its _BaseColor so the avatar is actually visible.
        private static void ApplyColor(Renderer r, Color color)
        {
            try
            {
                if (_hdrpLit == null) _hdrpLit = Shader.Find("HDRP/Lit");
                Material m = _hdrpLit != null ? new Material(_hdrpLit) : r.material;
                try { m.SetColor("_BaseColor", color); } catch { }
                try { m.color = color; } catch { }
                r.material = m;
            }
            catch { try { r.material.color = color; } catch { } }
        }

        public void Despawn()
        {
            try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
            _root = _head = _torso = _armL = _armR = _body = null; _rig = null; _tr = null;
        }

        public void Update(float dt)
        {
            if (_tr == null) return;
            var prev = _pos;
            // smooth follow
            _pos = Vector3.Lerp(_pos, TargetPos, Mathf.Clamp01(dt * 12f));
            _yaw = Mathf.LerpAngle(_yaw, TargetYaw, Mathf.Clamp01(dt * 12f));

            // ground speed (horizontal) from how far the avatar moved this frame
            float speed = 0f;
            if (dt > 0f) { var d = _pos - prev; d.y = 0f; speed = d.magnitude / dt; }

            if (_body != null) { UpdateModel(speed); return; }   // real mechanic model → drive its Animator
            UpdatePrimitive();                                    // fallback capsule avatar
        }

        private void UpdateModel(float speed)
        {
            _tr.position = _pos;                                  // model rooted at feet; no fake bob
            _tr.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (_rig == null) return;
            bool working = Mode is 1 or 2 or 3 or 4 or 9 or 10 or 11 or 12 or 13 or 23;
            try { _rig.Animate(speed, working, Time.unscaledTime); } catch { }
        }

        private void UpdatePrimitive()
        {
            float t = Time.unscaledTime;
            float bob = Mathf.Sin(t * 2f) * 0.02f;
            _tr.position = _pos + new Vector3(0f, bob, 0f);
            _tr.rotation = Quaternion.Euler(0f, _yaw, 0f);

            if (_head != null) _head.transform.localRotation = Quaternion.Euler(Mathf.Clamp(TargetPitch, -60f, 60f), 0f, 0f);

            // working animation: swing arms if the peer is in an assemble/mount mode
            bool working = Mode is 1 or 2 or 3 or 4 or 9 or 10 or 11 or 12 or 13 or 23;
            float swing = working ? Mathf.Sin(t * 8f) * 35f : Mathf.Sin(t * 1.5f) * 4f;
            if (_armR != null) _armR.transform.localRotation = Quaternion.Euler(working ? -60f + swing : swing, 0f, 0f);
            if (_armL != null) _armL.transform.localRotation = Quaternion.Euler(working ? -60f - swing : -swing, 0f, 0f);
        }

        public void SetVisible(bool v) { try { if (_root != null) _root.SetActive(v); } catch { } }

        private static Color ColorFor(byte id) => id switch
        {
            1 => new Color(0.26f, 0.62f, 1f),
            2 => new Color(1f, 0.55f, 0.2f),
            3 => new Color(0.4f, 0.85f, 0.4f),
            4 => new Color(0.9f, 0.4f, 0.8f),
            5 => new Color(0.95f, 0.85f, 0.3f),
            _ => new Color(0.7f, 0.7f, 0.74f),
        };
    }
}
