using System;
using UnityEngine;
using Steamworks;

namespace CarMecaSim.Mp.Core
{
    /// <summary>A visible proxy for another player (the game has no body mesh, so we spawn one).</summary>
    public sealed class RemotePlayer
    {
        public CSteamID Id;
        public byte PlayerId;
        public string Name;

        public Vector3 TargetPos;
        public float TargetYaw;
        public float TargetPitch;
        public byte Tool;
        public byte Mode;
        public float LastUpdate;

        private GameObject _go;
        private Transform _tr;

        public Vector3 WorldPos => _tr != null ? _tr.position : TargetPos;

        public void EnsureSpawned()
        {
            if (_go != null) return;
            try
            {
                _go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                _go.name = "CMS_RemotePlayer_" + PlayerId;
                var col = _go.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col); // don't block the local player
                _tr = _go.transform;
                _tr.localScale = new Vector3(0.5f, 0.9f, 0.5f);
                var rend = _go.GetComponent<Renderer>();
                if (rend != null && rend.material != null)
                    rend.material.color = ColorFor(PlayerId);
            }
            catch { /* rendering unavailable; proxy stays logical only */ }
        }

        public void Despawn()
        {
            try { if (_go != null) UnityEngine.Object.Destroy(_go); } catch { }
            _go = null; _tr = null;
        }

        public void Update(float dt)
        {
            if (_tr == null) return;
            // smooth toward the latest received transform
            _tr.position = Vector3.Lerp(_tr.position, TargetPos, Mathf.Clamp01(dt * 12f));
            var euler = new Vector3(0f, TargetYaw, 0f);
            _tr.rotation = Quaternion.Slerp(_tr.rotation, Quaternion.Euler(euler), Mathf.Clamp01(dt * 12f));
        }

        public void SetVisible(bool v) { try { if (_go != null) _go.SetActive(v); } catch { } }

        private static Color ColorFor(byte id) => id switch
        {
            1 => new Color(0.26f, 0.62f, 1f),
            2 => new Color(1f, 0.55f, 0.2f),
            3 => new Color(0.4f, 0.85f, 0.4f),
            4 => new Color(0.9f, 0.4f, 0.8f),
            _ => new Color(0.8f, 0.8f, 0.2f),
        };
    }
}
