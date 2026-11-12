using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Steamworks;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;
using MainMenuButton = CMS.MainMenu.Controls.MainMenuButton;

namespace CarMecaSim.Mp.UI
{
    /// <summary>
    /// Adds a NATIVE main-menu button ("MULTIJOUEUR" / "REJOINDRE &lt;ami&gt;") by cloning one of the
    /// game's own menu buttons — so it matches the native style exactly, no custom IMGUI panels.
    /// Clicking joins a hosting friend directly (or opens Steam's friends list to pick).
    /// </summary>
    public static class NativeCoopMenu
    {
        private static GameObject _btn;
        private static Text _label;
        private static float _lastRefresh;

        public static void Tick(MpSession s)
        {
            try
            {
                if (!GameRefs.OnMainMenu) { _btn = null; _label = null; return; }
                if (_btn == null) TryCreate(s);     // Unity's overridden == is true after a scene rebuild
                if (_btn != null) RefreshLabel(s);
            }
            catch { }
        }

        private static void TryCreate(MpSession s)
        {
            // Avoid duplicates if our reference was cleared but the object still lives.
            var existing = GameObject.Find("CMS_CoopButton");
            if (existing != null) { _btn = existing; _label = existing.GetComponentInChildren<Text>(true); return; }

            // Gather the native menu buttons (fixed anchored positions, no layout group). The column is
            // already full, so to fit a 7th cleanly we TIGHTEN the existing rows a bit and place ours at
            // the bottom of that compacted stack — all above the footer.
            var rts = new List<RectTransform>();
            MainMenuButton bottom = null; RectTransform bottomRt = null; float spacing = 0f, topY = float.MinValue;
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<MainMenuButton>();
                if (all == null || all.Length == 0) return;
                Transform parent = null;
                foreach (var b in all)
                {
                    if (b == null || !b.gameObject.activeInHierarchy || b.transform.parent == null) continue;
                    var rt = b.GetComponent<RectTransform>();
                    if (rt == null) continue;
                    if (parent == null) parent = b.transform.parent;
                    if (b.transform.parent != parent) continue;
                    rts.Add(rt);
                    if (bottom == null || rt.anchoredPosition.y < bottomRt.anchoredPosition.y) { bottom = b; bottomRt = rt; }
                    if (rt.anchoredPosition.y > topY) topY = rt.anchoredPosition.y;
                }
                if (bottom == null || rts.Count == 0) return;
                for (int i = 0; i < rts.Count; i++)
                    for (int j = i + 1; j < rts.Count; j++)
                    {
                        float dy = Mathf.Abs(rts[i].anchoredPosition.y - rts[j].anchoredPosition.y);
                        if (dy > 1f && (spacing == 0f || dy < spacing)) spacing = dy;
                    }
                if (spacing <= 1f) spacing = bottomRt.rect.height > 1f ? bottomRt.rect.height * 1.1f : 90f;
            }
            catch { return; }
            if (bottom == null || bottomRt == null) return;

            try
            {
                int n = rts.Count;                       // native buttons
                float tight = spacing * (float)n / (n + 1);   // squeeze n+1 rows into the n-row span
                // re-space the native buttons from the top using the tighter gap
                rts.Sort((a, b) => b.anchoredPosition.y.CompareTo(a.anchoredPosition.y));   // top -> bottom
                for (int i = 0; i < n; i++)
                    rts[i].anchoredPosition = new Vector2(rts[i].anchoredPosition.x, topY - i * tight);

                var clone = UnityEngine.Object.Instantiate(bottom.gameObject, bottom.transform.parent);
                clone.name = "CMS_CoopButton";
                var crt = clone.GetComponent<RectTransform>();
                if (crt != null)
                {
                    crt.anchorMin = bottomRt.anchorMin; crt.anchorMax = bottomRt.anchorMax;
                    crt.pivot = bottomRt.pivot; crt.sizeDelta = bottomRt.sizeDelta;
                    crt.anchoredPosition = new Vector2(bottomRt.anchoredPosition.x, topY - n * tight);  // last row of the compacted stack
                }

                _label = clone.GetComponentInChildren<Text>(true);
                if (_label != null) _label.text = "MULTIJOUEUR";

                var mmb = clone.GetComponent<MainMenuButton>();
                if (mmb != null && mmb.OnClick != null)
                {
                    mmb.OnClick.RemoveAllListeners();
                    mmb.OnClick.AddListener((UnityAction)(() => OnClick(s)));
                }
                _btn = clone;
                CarMecaPlugin.Logger?.LogInfo("[menu] native MULTIJOUEUR button added");
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[menu] native button failed: " + e.Message); }
        }

        private static void RefreshLabel(MpSession s)
        {
            if (_label == null || Time.unscaledTime - _lastRefresh < 2f) return;
            _lastRefresh = Time.unscaledTime;
            int n = 0; string name = null;
            try { foreach (var f in s.JoinableFriends()) { n++; name = f.Name; } } catch { }
            _label.text = n == 1 ? ("REJOINDRE " + (name ?? "").ToUpper())
                        : n > 1 ? ("MULTIJOUEUR (" + n + ")")
                        : "MULTIJOUEUR";
        }

        private static void OnClick(MpSession s)
        {
            try
            {
                var hosts = new List<Net.SteamNet.JoinableFriend>();
                foreach (var f in s.JoinableFriends()) hosts.Add(f);
                if (hosts.Count == 1) { s.JoinLobby(hosts[0].Lobby); CarMecaPlugin.Logger?.LogInfo("[menu] joining " + hosts[0].Name); }
                else { try { SteamFriends.ActivateGameOverlay("friends"); } catch { } }   // pick among several / none yet
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[menu] join click failed: " + e.Message); }
        }
    }
}
