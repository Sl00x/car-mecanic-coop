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
    /// Native main-menu refactor: clones a real MainMenuButton into a "MULTIJOUEUR" entry, then
    /// re-lays-out the whole button stack as a CENTERED card over the car (per the chosen design),
    /// holding the layout each frame against the native menu. No custom IMGUI — all native uGUI.
    /// </summary>
    public static class NativeCoopMenu
    {
        private static GameObject _btn;          // our cloned MULTIJOUEUR button
        private static Text _label;
        private static float _lastRefresh;

        private static readonly List<RectTransform> _managed = new();  // all buttons we position
        private static readonly List<Vector2> _targets = new();
        private static bool _laidOut;
        private static GameObject _card;
        private static RectTransform _cardRt;
        private static readonly Color Accent = new Color(1f, 0.68f, 0.18f, 1f);

        private static Sprite _solid;
        private static Sprite Solid()
        {
            if (_solid == null)
            {
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) t.SetPixel(x, y, Color.white);
                t.Apply();
                _solid = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                _solid.hideFlags = HideFlags.HideAndDontSave;
            }
            return _solid;
        }

        public static void Tick(MpSession s)
        {
            try
            {
                if (!GameRefs.OnMainMenu) { _btn = null; _label = null; _laidOut = false; _managed.Clear(); _targets.Clear(); _card = null; _cardRt = null; return; }
                if (_btn == null) TryCreate(s);
                HoldLayout();
                if (_btn != null) RefreshLabel(s);
            }
            catch { }
        }

        /// <summary>Re-assert our centered positions every frame so the native menu can't snap them back.</summary>
        private static void HoldLayout()
        {
            if (!_laidOut) return;
            for (int i = 0; i < _managed.Count; i++)
            {
                var rt = _managed[i];
                if (rt == null) continue;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = _targets[i];
            }
        }

        private static void TryCreate(MpSession s)
        {
            var existing = GameObject.Find("CMS_CoopButton");
            if (existing != null) { _btn = existing; _label = existing.GetComponentInChildren<Text>(true); return; }

            // collect native buttons sharing a parent
            var buttons = new List<MainMenuButton>();
            var rts = new List<RectTransform>();
            MainMenuButton bottom = null; RectTransform bottomRt = null; float spacing = 0f;
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
                    buttons.Add(b); rts.Add(rt);
                    if (bottom == null || rt.anchoredPosition.y < bottomRt.anchoredPosition.y) { bottom = b; bottomRt = rt; }
                }
                if (bottom == null || rts.Count == 0) return;
                for (int i = 0; i < rts.Count; i++)
                    for (int j = i + 1; j < rts.Count; j++)
                    {
                        float dy = Mathf.Abs(rts[i].anchoredPosition.y - rts[j].anchoredPosition.y);
                        if (dy > 1f && (spacing == 0f || dy < spacing)) spacing = dy;
                    }
                if (spacing <= 1f) spacing = bottomRt.rect.height > 1f ? bottomRt.rect.height * 1.05f : 90f;
            }
            catch { return; }
            if (bottom == null) return;

            try
            {
                // clone the MULTIJOUEUR button from a native one
                var clone = UnityEngine.Object.Instantiate(bottom.gameObject, bottom.transform.parent);
                clone.name = "CMS_CoopButton";
                var crt = clone.GetComponent<RectTransform>();
                if (crt != null) { crt.sizeDelta = bottomRt.sizeDelta; }

                _label = clone.GetComponentInChildren<Text>(true);
                if (_label != null) { _label.text = "MULTIJOUEUR"; _label.color = Accent; _label.fontStyle = FontStyle.Bold; }

                var mmb = clone.GetComponent<MainMenuButton>();
                if (mmb != null && mmb.OnClick != null)
                {
                    mmb.OnClick.RemoveAllListeners();
                    mmb.OnClick.AddListener((UnityAction)(() => OnClick(s)));
                }
                _btn = clone;

                // ---- centered CARD layout over the car ----
                // Reparent every button to the ROOT CANVAS so (0,0) center-anchored == screen centre
                // (their native parent is left-anchored, which is why x=0 stayed on the left).
                var rootCanvas = bottom.GetComponentInParent<Canvas>();
                Transform root = rootCanvas != null ? rootCanvas.rootCanvas.transform : bottom.transform.parent.parent;

                var ordered = new List<RectTransform>(rts);
                ordered.Sort((a, b) => b.anchoredPosition.y.CompareTo(a.anchoredPosition.y));  // top -> bottom
                if (crt != null) ordered.Add(crt);                                             // ours at the bottom
                int m = ordered.Count;

                float btnW = bottomRt.rect.width > 10f ? bottomRt.rect.width : 460f;
                float btnH = bottomRt.rect.height > 10f ? bottomRt.rect.height : 64f;
                float step = Mathf.Max(spacing, btnH + 14f);
                float top = (m - 1) * 0.5f * step;

                // backdrop card (behind the buttons)
                _card = new GameObject("CMS_CoopCard");
                _card.transform.SetParent(root, false);
                var img = _card.AddComponent<Image>();
                img.sprite = Solid();
                img.color = new Color(0.05f, 0.06f, 0.08f, 0.80f);
                img.raycastTarget = false;
                _cardRt = _card.GetComponent<RectTransform>();
                _cardRt.anchorMin = _cardRt.anchorMax = _cardRt.pivot = new Vector2(0.5f, 0.5f);
                _cardRt.sizeDelta = new Vector2(btnW + 70f, m * step + 60f);
                _cardRt.anchoredPosition = Vector2.zero;
                // a slim accent bar on the card's left edge
                var bar = new GameObject("CMS_CoopCardBar");
                bar.transform.SetParent(_card.transform, false);
                var barImg = bar.AddComponent<Image>(); barImg.sprite = Solid(); barImg.color = Accent; barImg.raycastTarget = false;
                var barRt = bar.GetComponent<RectTransform>();
                barRt.anchorMin = new Vector2(0f, 0f); barRt.anchorMax = new Vector2(0f, 1f);
                barRt.pivot = new Vector2(0f, 0.5f); barRt.sizeDelta = new Vector2(5f, 0f); barRt.anchoredPosition = Vector2.zero;

                _managed.Clear(); _targets.Clear();
                for (int i = 0; i < m; i++)
                {
                    ordered[i].SetParent(root, false);   // onto the root canvas (render on top of the card)
                    _managed.Add(ordered[i]);
                    _targets.Add(new Vector2(0f, top - i * step));
                }
                _laidOut = true;
                HoldLayout();
                CarMecaPlugin.Logger?.LogInfo($"[menu] centered card: {m} buttons, btn {btnW}x{btnH}, step {step}");
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[menu] layout failed: " + e.Message); }
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
            _label.color = Accent;
        }

        private static void OnClick(MpSession s)
        {
            try
            {
                var hosts = new List<Net.SteamNet.JoinableFriend>();
                foreach (var f in s.JoinableFriends()) hosts.Add(f);
                if (hosts.Count == 1) { s.JoinLobby(hosts[0].Lobby); CarMecaPlugin.Logger?.LogInfo("[menu] joining " + hosts[0].Name); }
                else { try { SteamFriends.ActivateGameOverlay("friends"); } catch { } }
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[menu] join click failed: " + e.Message); }
        }
    }
}
