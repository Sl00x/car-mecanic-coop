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
    /// Full "TORQUE / GARAGE 2026" main-menu skin, drawn on a DEDICATED top-most Canvas
    /// (CanvasScaler reference 2000x837 = the mockup's ultrawide space, so positions map 1:1 to the
    /// reference art). Hides the native menu and wires each custom entry to the real native action.
    /// </summary>
    public static class MenuSkin
    {
        private static readonly Color Lime  = new Color(0.776f, 0.96f, 0.19f, 1f);
        private static readonly Color White = new Color(0.96f, 0.97f, 0.98f, 1f);
        private static readonly Color Grey  = new Color(0.82f, 0.84f, 0.83f, 1f);
        private static readonly Color Muted = new Color(0.55f, 0.58f, 0.56f, 1f);
        private static readonly Color Faint = new Color(0.40f, 0.43f, 0.41f, 1f);
        private static readonly Color CardBg = new Color(0.07f, 0.085f, 0.075f, 0.95f);
        private static readonly Color RowHi = new Color(0.10f, 0.12f, 0.10f, 0.95f);

        private static GameObject _canvasGo;
        private static MpSession _s;
        private static readonly List<MainMenuButton> _native = new();
        private static readonly List<RectTransform> _rows = new();   // menu hit-boxes
        private static readonly List<Image> _rowBg = new();           // their backgrounds
        private static readonly List<Image> _rowBar = new();          // lime left bar (selection)
        private static readonly List<Text> _rowChev = new();          // chevron (selection)
        private static readonly List<Text> _rowTitle = new();
        private static int _sel;

        private static bool _disabled;
        /// <summary>F8 safety hatch: turn the custom skin off and fall back to the native menu.</summary>
        public static void Toggle() { _disabled = !_disabled; CarMecaPlugin.Logger?.LogInfo("[skin] " + (_disabled ? "disabled (native menu)" : "enabled")); }

        public static void Tick(MpSession s)
        {
            _s = s;
            try
            {
                if (_disabled) { Teardown(); return; }
                // Only when the TOP-LEVEL menu is up — when a native sub-window (save selection, settings)
                // opens, hide our skin so the native screen is visible.
                if (!GameRefs.OnMainMenu || !MainSectionActive()) { Teardown(); return; }
                if (_canvasGo == null || GameObject.Find("CMS_SkinCanvas") == null) Build();
                HideGameUI();
                HandleInput();
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[skin] tick: " + e.Message); }
        }

        private static bool MainSectionActive()
        {
            try
            {
                var mm = UnityEngine.Object.FindObjectOfType<CMS.MainMenu.MainMenuManager>();
                if (mm == null) return false;
                var ms = mm.GetMainSection();
                return ms != null && ms.IsActive();
            }
            catch { return true; }
        }

        private static void Teardown()
        {
            RestoreGameUI();
            try { var mm = UnityEngine.Object.FindObjectOfType<CMS.MainMenu.MainMenuManager>(); var ms = mm != null ? mm.GetMainSection() : null; if (ms != null) ms.DisableInput(false, false); } catch { }
            try { if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo); } catch { }
            _canvasGo = null; _native.Clear(); _rows.Clear(); _rowBg.Clear(); _rowBar.Clear(); _rowChev.Clear(); _rowTitle.Clear(); _sel = 0;
        }

        // Hide the game's own menu canvases (logo, DLC cards, welcome, version, native buttons) while our
        // skin is up, and restore them when it tears down (so native sub-screens stay visible).
        private static readonly Dictionary<int, CanvasGroup> _hidden = new();
        private static readonly Dictionary<int, float> _hiddenAlpha = new();

        private static void HideGameUI()
        {
            HideNatives();
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Canvas>();
                if (all == null) return;
                foreach (var c in all)
                {
                    if (c == null) continue;
                    if (_canvasGo != null && c.gameObject == _canvasGo) continue;   // not our own
                    if (c.rootCanvas != c) continue;                                 // root canvases only
                    int id = c.gameObject.GetInstanceID();
                    if (_hidden.ContainsKey(id)) continue;
                    var cg = c.GetComponent<CanvasGroup>() ?? c.gameObject.AddComponent<CanvasGroup>();
                    _hidden[id] = cg; _hiddenAlpha[id] = cg.alpha;
                    cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false;   // don't eat our clicks
                }
            }
            catch { }
        }

        private static void RestoreGameUI()
        {
            foreach (var kv in _hidden)
            {
                try { if (kv.Value != null && _hiddenAlpha.TryGetValue(kv.Key, out var a)) { kv.Value.alpha = a; kv.Value.blocksRaycasts = true; kv.Value.interactable = true; } } catch { }
            }
            _hidden.Clear(); _hiddenAlpha.Clear();
        }

        // ---------- helpers ----------
        private static Font _font;
        private static Font MenuFont()
        {
            if (_font != null) return _font;
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Text>();
                if (all != null) foreach (var t in all) if (t != null && t.font != null) { _font = t.font; break; }
            }
            catch { }
            if (_font == null) { try { _font = Font.CreateDynamicFontFromOSFont("Arial", 16); } catch { } }
            return _font;
        }

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

        /// <summary>Place an element by TOP-LEFT pixel coords in the 2000x837 reference space.</summary>
        private static RectTransform Place(Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject("e"); go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        private static Image Img(RectTransform rt, Color c, bool raycast = false)
        {
            var im = rt.gameObject.AddComponent<Image>(); im.sprite = Solid(); im.color = c; im.raycastTarget = raycast; return im;
        }
        private static Image Box(Transform parent, float x, float y, float w, float h, Color c, bool raycast = false)
            => Img(Place(parent, x, y, w, h), c, raycast);

        private static Text Label(Transform parent, float x, float y, float w, float h, string s, int size, Color c, FontStyle style, TextAnchor anchor)
        {
            var rt = Place(parent, x, y, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = MenuFont(); t.text = s; t.fontSize = size; t.color = c; t.fontStyle = style; t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // ---------- native wiring ----------
        private static void CollectNative()
        {
            _native.Clear();
            try { var all = UnityEngine.Object.FindObjectsOfType<MainMenuButton>(); if (all != null) foreach (var b in all) if (b != null && b.transform.parent != null) _native.Add(b); }
            catch { }
        }
        private static MainMenuButton FindNative(params string[] labels)
        {
            foreach (var b in _native)
            {
                string txt = null;
                try { var t = b.GetComponentInChildren<Text>(true); txt = t != null ? t.text : null; } catch { }
                if (string.IsNullOrEmpty(txt)) continue;
                foreach (var l in labels) if (txt.IndexOf(l, StringComparison.OrdinalIgnoreCase) >= 0) return b;
            }
            return null;
        }
        private static void InvokeNative(MainMenuButton b)
        { try { if (b != null && b.OnClick != null) b.OnClick.Invoke(); } catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[skin] invoke: " + e.Message); } }

        private static void HideNatives()
        {
            foreach (var b in _native)
            {
                if (b == null) continue;
                try { var cg = b.GetComponent<CanvasGroup>() ?? b.gameObject.AddComponent<CanvasGroup>(); cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false; }
                catch { }
            }
        }

        // ---------- build ----------
        private static void Build()
        {
            CollectNative();

            _canvasGo = new GameObject("CMS_SkinCanvas");
            var cv = _canvasGo.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 32767;   // max — above every game menu canvas
            cv.overrideSorting = true;
            var sc = _canvasGo.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(2000f, 837f);
            sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            sc.matchWidthOrHeight = 0f;   // match width -> 2000 wide, height follows aspect
            _canvasGo.AddComponent<GraphicRaycaster>();
            var root = _canvasGo.transform;

            // full-screen cinematic darken (covers the native logo / DLC cards / car)
            var ov = Place(root, 0f, 0f, 2000f, 837f); ov.anchorMin = Vector2.zero; ov.anchorMax = Vector2.one; ov.offsetMin = Vector2.zero; ov.offsetMax = Vector2.zero;
            Img(ov, new Color(0.02f, 0.03f, 0.025f, 0.965f));

            // stop the native menu from also reacting to our ↑↓/Enter
            try { var mm = UnityEngine.Object.FindObjectOfType<CMS.MainMenu.MainMenuManager>(); var ms = mm != null ? mm.GetMainSection() : null; if (ms != null) ms.DisableInput(true, true); } catch { }

            BuildTopBar(root);
            BuildHeadline(root);
            BuildMenu(root);
            BuildSessionCard(root);
            BuildFooter(root);

            CarMecaPlugin.Logger?.LogInfo("[skin] TORQUE skin built");
        }

        private static void BuildTopBar(Transform root)
        {
            // bar-chart logo mark (3 lime bars)
            Box(root, 42f, 20f, 6f, 16f, Lime); Box(root, 51f, 14f, 6f, 22f, Lime); Box(root, 60f, 24f, 6f, 12f, Lime);
            Label(root, 78f, 16f, 300f, 20f, "TORQUE", 15, White, FontStyle.Bold, TextAnchor.UpperLeft);
            Label(root, 78f, 38f, 300f, 14f, "GARAGE // 2026", 9, Muted, FontStyle.Bold, TextAnchor.UpperLeft);
            // separator line under the bar
            var sep = Place(root, 0f, 66f, 2000f, 1f); Img(sep, new Color(1f, 1f, 1f, 0.06f));

            // player cluster (top-right)
            string name = "JOUEUR";
            try { if (_s != null && _s.Net.Ready) name = SteamFriends.GetPersonaName(); } catch { }
            Label(root, 1470f, 22f, 230f, 14f, "● CONNECTÉ", 9, Lime, FontStyle.Bold, TextAnchor.UpperRight);
            Label(root, 1470f, 34f, 230f, 16f, name.ToUpper(), 12, White, FontStyle.Bold, TextAnchor.UpperRight);
            Label(root, 1470f, 50f, 230f, 12f, "NIVEAU 14 · PRO", 8, Muted, FontStyle.Bold, TextAnchor.UpperRight);
            var av = Box(root, 1712f, 20f, 34f, 34f, new Color(0.14f, 0.16f, 0.13f, 1f)); // avatar
            Label(root, 1712f, 20f, 34f, 34f, "AM", 12, Lime, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        private static void BuildHeadline(Transform root)
        {
            Label(root, 80f, 400f, 300f, 16f, "S I M U L A T I O N   A U T O M O B I L E", 11, Muted, FontStyle.Bold, TextAnchor.UpperLeft);
            Box(root, 218f, 406f, 60f, 2f, Lime);
            Label(root, 76f, 418f, 760f, 64f, "VOTRE GARAGE.", 58, White, FontStyle.BoldAndItalic, TextAnchor.UpperLeft);
            Label(root, 76f, 466f, 760f, 64f, "VOS RÈGLES.", 58, Lime, FontStyle.BoldAndItalic, TextAnchor.UpperLeft);
            Label(root, 80f, 524f, 520f, 40f, "Diagnostiquez. Réparez. Transformez.\nChaque moteur raconte une histoire.", 14, Muted, FontStyle.Normal, TextAnchor.UpperLeft);
        }

        private static readonly string[,] Items =
        {
            { "Continuer",         "ATELIER PRINCIPAL" },
            { "Nouvelle carrière", "CRÉER UN GARAGE" },
            { "Multiplayer",       "JUSQU'À 4 JOUEURS" },
            { "Marché",            "VÉHICULES & PIÈCES" },
            { "Options",           "JEU & ACCESSIBILITÉ" },
        };

        private static void BuildMenu(Transform root)
        {
            _rows.Clear(); _rowBg.Clear(); _rowBar.Clear(); _rowChev.Clear(); _rowTitle.Clear();
            float x = 80f, w = 268f, h = 42f, step = 46f, top = 556f;
            for (int i = 0; i < 5; i++)
            {
                float y = top + i * step;
                var row = Place(root, x, y, w, h);
                var bg = Img(row, new Color(1f, 1f, 1f, 0f));
                var bar = Box(row, 0f, 0f, 3f, h, Lime);
                var title = Label(row, 22f, 7f, w - 40f, 20f, Items[i, 0], 17, Grey, FontStyle.Bold, TextAnchor.UpperLeft);
                Label(row, 22f, 25f, w - 40f, 14f, Items[i, 1], 9, Muted, FontStyle.Bold, TextAnchor.UpperLeft);
                var chev = Label(row, w - 26f, 0f, 20f, h, "›", 22, Lime, FontStyle.Bold, TextAnchor.MiddleCenter);
                if (i == 2) { Box(row, w + 8f, 11f, 46f, 18f, new Color(0.776f, 0.96f, 0.19f, 0.15f)); Label(row, w + 8f, 11f, 46f, 18f, "● LIVE", 9, Lime, FontStyle.Bold, TextAnchor.MiddleCenter); }
                _rows.Add(row); _rowBg.Add(bg); _rowBar.Add(bar); _rowChev.Add(chev); _rowTitle.Add(title);
            }
            ApplySelection();
        }

        private static void ApplySelection()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                bool sel = i == _sel;
                if (_rowBg[i] != null) _rowBg[i].color = sel ? RowHi : new Color(1f, 1f, 1f, 0f);
                if (_rowBar[i] != null) _rowBar[i].gameObject.SetActive(sel);
                if (_rowChev[i] != null) _rowChev[i].gameObject.SetActive(sel);
                if (_rowTitle[i] != null) _rowTitle[i].color = sel ? White : Grey;
            }
        }

        // Input without the game's module: keyboard ↑↓ + Enter (guaranteed, same API as F6) plus
        // mouse hover/click via our own rect hit-test.
        private static void HandleInput()
        {
            try
            {
                int prev = _sel;
                if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) _sel = (_sel + 1) % _rows.Count;
                if (Input.GetKeyDown(KeyCode.UpArrow)   || Input.GetKeyDown(KeyCode.W)) _sel = (_sel - 1 + _rows.Count) % _rows.Count;

                var mp = Input.mousePosition;
                for (int i = 0; i < _rows.Count; i++)
                    if (_rows[i] != null && RectTransformUtility.RectangleContainsScreenPoint(_rows[i], mp, null)) { _sel = i; break; }

                if (_sel != prev) ApplySelection();

                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetMouseButtonDown(0))
                    OnRow(_sel);
            }
            catch { }
        }

        private static void BuildSessionCard(Transform root)
        {
            float x = 1714f, y = 606f, w = 248f, h = 150f;
            var card = Place(root, x, y, w, h); Img(card, CardBg);
            Box(card, 0f, 0f, 3f, h, Lime);
            Label(card, 18f, 12f, 150f, 12f, "DERNIÈRE SESSION", 8, Muted, FontStyle.Bold, TextAnchor.UpperLeft);
            Label(card, w - 90f, 12f, 72f, 12f, "IL Y A 2H", 8, Faint, FontStyle.Bold, TextAnchor.UpperRight);
            Label(card, 18f, 32f, 150f, 12f, "PROJET EN COURS", 8, Muted, FontStyle.Bold, TextAnchor.UpperLeft);
            Label(card, 18f, 44f, 160f, 22f, "Falcon GT '88", 16, White, FontStyle.BoldAndItalic, TextAnchor.UpperLeft);
            Label(card, w - 70f, 44f, 52f, 22f, "68%", 16, Lime, FontStyle.Bold, TextAnchor.UpperRight);
            // progress
            Box(card, 18f, 78f, w - 36f, 3f, new Color(1f, 1f, 1f, 0.08f));
            Box(card, 18f, 78f, (w - 36f) * 0.68f, 3f, Lime);
            // stats
            Label(card, 18f, 100f, 80f, 10f, "CRÉDITS", 7, Faint, FontStyle.Bold, TextAnchor.UpperLeft);
            Label(card, 18f, 112f, 90f, 16f, "€ 84,250", 12, White, FontStyle.Bold, TextAnchor.UpperLeft);
            Label(card, 110f, 100f, 80f, 10f, "RÉPUTATION", 7, Faint, FontStyle.Bold, TextAnchor.UpperLeft);
            Label(card, 110f, 112f, 90f, 16f, "4,820", 12, White, FontStyle.Bold, TextAnchor.UpperLeft);
            Label(card, 200f, 100f, 48f, 10f, "JOURS", 7, Faint, FontStyle.Bold, TextAnchor.UpperRight);
            Label(card, 200f, 112f, 30f, 16f, "127", 12, White, FontStyle.Bold, TextAnchor.UpperRight);
        }

        private static void BuildFooter(Transform root)
        {
            Label(root, 40f, 806f, 700f, 16f, "↑↓  NAVIGUER        ⏎  SÉLECTIONNER        ESC  RETOUR", 10, Faint, FontStyle.Bold, TextAnchor.UpperLeft);
            Label(root, 1600f, 806f, 360f, 16f, "BUILD 26.04.18 · EUROPE WEST", 9, Faint, FontStyle.Bold, TextAnchor.UpperRight);
        }

        // ---------- actions ----------
        private static void OnRow(int index)
        {
            try
            {
                switch (index)
                {
                    case 0: InvokeNative(FindNative("Jouer", "Continuer", "Play")); break;
                    case 1: InvokeNative(FindNative("Jouer", "Play")); break;
                    case 2: CoopClick(); break;
                    case 3: InvokeNative(FindNative("Concessionnaire", "Dealer")); break;
                    case 4: InvokeNative(FindNative("Réglages", "Options", "Settings")); break;
                }
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[skin] row " + index + ": " + e.Message); }
        }

        private static void CoopClick()
        {
            try
            {
                var hosts = new List<Net.SteamNet.JoinableFriend>();
                if (_s != null) foreach (var f in _s.JoinableFriends()) hosts.Add(f);
                if (hosts.Count == 1 && _s != null) _s.JoinLobby(hosts[0].Lobby);
                else { try { SteamFriends.ActivateGameOverlay("friends"); } catch { } }
            }
            catch { }
        }
    }
}
