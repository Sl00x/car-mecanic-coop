using UnityEngine;

namespace CarMecaSim.Mp.UI
{
    /// <summary>Premium dark-glass IMGUI theme for the in-game co-op panel.</summary>
    public static class Theme
    {
        public static readonly Color Orange = new Color(1.00f, 0.68f, 0.18f, 1f);
        public static readonly Color OrangeHi = new Color(1.00f, 0.77f, 0.33f, 1f);
        public static readonly Color Green  = new Color(0.44f, 0.88f, 0.50f, 1f);
        public static readonly Color Text   = new Color(0.93f, 0.94f, 0.96f, 1f);
        public static readonly Color Muted  = new Color(0.60f, 0.63f, 0.70f, 1f);

        public static GUIStyle Glass, GlassShadow, OrangeFill, Divider,
            PanelTitle, TabLabel, KeyHint, SectionLabel, MutedS, Label, ChatLine,
            RolePill, Dot, Field, Btn, BtnPrimary, Tag;
        private static bool _ready;

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c); t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave; t.wrapMode = TextureWrapMode.Repeat;
            return t;
        }
        private static RectOffset RO(int l, int r, int t, int b) { var o = new RectOffset(); o.left = l; o.right = r; o.top = t; o.bottom = b; return o; }

        public static void EnsureInit()
        {
            if (_ready) return; _ready = true;

            var glass  = Tex(new Color(0.055f, 0.06f, 0.075f, 0.94f));
            var shadow = Tex(new Color(0f, 0f, 0f, 0.55f));
            var amber  = Tex(Orange);
            var div    = Tex(new Color(1f, 1f, 1f, 0.07f));
            var fieldbg = Tex(new Color(0f, 0f, 0f, 0.45f));
            var btnbg  = Tex(new Color(0.14f, 0.15f, 0.18f, 0.95f));
            var amberHi = Tex(OrangeHi);
            var tagbg  = Tex(new Color(0.04f, 0.05f, 0.06f, 0.82f));

            Glass = new GUIStyle(); Glass.normal.background = glass;
            GlassShadow = new GUIStyle(); GlassShadow.normal.background = shadow;
            OrangeFill = new GUIStyle(); OrangeFill.normal.background = amber;
            Divider = new GUIStyle(); Divider.normal.background = div;

            PanelTitle = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            PanelTitle.normal.textColor = Orange;

            TabLabel = new GUIStyle { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            TabLabel.normal.textColor = Text;

            KeyHint = new GUIStyle { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            KeyHint.normal.textColor = Muted;

            SectionLabel = new GUIStyle { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            SectionLabel.normal.textColor = Muted;

            MutedS = new GUIStyle { fontSize = 11, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            MutedS.normal.textColor = Muted;

            Label = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            Label.normal.textColor = Text;

            ChatLine = new GUIStyle { fontSize = 11, alignment = TextAnchor.UpperLeft, wordWrap = true };
            ChatLine.normal.textColor = Text;

            RolePill = new GUIStyle { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            RolePill.normal.textColor = Orange;

            Dot = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            Dot.normal.textColor = Color.white;

            Field = new GUIStyle { padding = RO(8, 8, 6, 6), fontSize = 12, alignment = TextAnchor.MiddleLeft };
            Field.normal.background = fieldbg; Field.normal.textColor = Text;
            Field.focused.background = fieldbg; Field.focused.textColor = Color.white;

            Btn = new GUIStyle { padding = RO(10, 10, 7, 7), fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Btn.normal.background = btnbg; Btn.normal.textColor = Text;
            Btn.hover.background = amberHi; Btn.hover.textColor = Color.black;
            Btn.active.background = amber; Btn.active.textColor = Color.black;

            BtnPrimary = new GUIStyle(Btn);
            BtnPrimary.normal.background = amber; BtnPrimary.normal.textColor = Color.black;
            BtnPrimary.hover.background = amberHi; BtnPrimary.hover.textColor = Color.black;

            Tag = new GUIStyle { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = RO(6, 6, 3, 3) };
            Tag.normal.background = tagbg; Tag.normal.textColor = Text;
        }
    }
}
