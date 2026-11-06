using UnityEngine;

namespace CarMecaSim.Mp.UI
{
    /// <summary>Car Mechanic Simulator-style IMGUI theme: dark steel panels, orange accents.</summary>
    public static class Theme
    {
        // CMS palette
        public static readonly Color Bg       = new Color(0.10f, 0.10f, 0.11f, 0.96f);
        public static readonly Color Panel    = new Color(0.15f, 0.15f, 0.17f, 0.98f);
        public static readonly Color PanelAlt = new Color(0.19f, 0.19f, 0.21f, 1f);
        public static readonly Color Orange   = new Color(0.96f, 0.58f, 0.11f, 1f);   // CMS signature
        public static readonly Color OrangeHi = new Color(1.00f, 0.66f, 0.18f, 1f);
        public static readonly Color Text     = new Color(0.92f, 0.92f, 0.93f, 1f);
        public static readonly Color Dim      = new Color(0.62f, 0.62f, 0.65f, 1f);
        public static readonly Color Money     = new Color(0.52f, 0.86f, 0.46f, 1f);
        public static readonly Color Xp        = new Color(0.36f, 0.72f, 1.00f, 1f);
        public static readonly Color Scraps    = new Color(0.80f, 0.70f, 0.45f, 1f);

        public static GUIStyle Panel_, Header, Chip, Btn, BtnPrimary, Label, LabelDim,
                               Value, Title, ChatLine, Field, BarBg, BarFill, OrangeFill;
        private static bool _ready;

        // RectOffset's (l,r,t,b) ctor isn't exposed by Il2CppInterop; build via properties.
        private static RectOffset RO(int l, int r, int t, int b)
        {
            var o = new RectOffset();
            o.left = l; o.right = r; o.top = t; o.bottom = b;
            return o;
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Repeat;
            return t;
        }

        public static void EnsureInit()
        {
            if (_ready) return;
            _ready = true;

            var bg = Tex(Bg); var panel = Tex(Panel); var panelAlt = Tex(PanelAlt);
            var orange = Tex(Orange); var orangeHi = Tex(OrangeHi);
            var clear = Tex(new Color(0, 0, 0, 0));
            var barbg = Tex(new Color(0.08f, 0.08f, 0.09f, 1f));
            var xpfill = Tex(Xp);

            Panel_ = new GUIStyle { padding = RO(10, 10, 8, 10) };
            Panel_.normal.background = panel;

            Header = new GUIStyle { padding = RO(10, 10, 6, 6), fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            Header.normal.background = orange;
            Header.normal.textColor = new Color(0.08f, 0.06f, 0.03f, 1f);

            Title = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            Title.normal.textColor = Orange;

            Chip = new GUIStyle { padding = RO(8, 8, 3, 3), fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Chip.normal.background = panelAlt; Chip.normal.textColor = Text;

            Label = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            Label.normal.textColor = Text;

            LabelDim = new GUIStyle(Label); LabelDim.normal.textColor = Dim; LabelDim.fontStyle = FontStyle.Italic;

            Value = new GUIStyle { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            Value.normal.textColor = Text;

            ChatLine = new GUIStyle { fontSize = 11, alignment = TextAnchor.UpperLeft, wordWrap = true };
            ChatLine.normal.textColor = Text;

            Btn = new GUIStyle { padding = RO(10, 10, 7, 7), fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Btn.normal.background = panelAlt; Btn.normal.textColor = Text;
            Btn.hover.background = orangeHi; Btn.hover.textColor = Color.black;
            Btn.active.background = orange; Btn.active.textColor = Color.black;

            BtnPrimary = new GUIStyle(Btn);
            BtnPrimary.normal.background = orange; BtnPrimary.normal.textColor = Color.black;
            BtnPrimary.hover.background = orangeHi; BtnPrimary.hover.textColor = Color.black;

            Field = new GUIStyle { padding = RO(7, 7, 5, 5), fontSize = 12, alignment = TextAnchor.MiddleLeft };
            Field.normal.background = barbg; Field.normal.textColor = Text;
            Field.focused.background = barbg; Field.focused.textColor = Color.white;

            BarBg = new GUIStyle(); BarBg.normal.background = barbg;
            BarFill = new GUIStyle(); BarFill.normal.background = xpfill;
            OrangeFill = new GUIStyle(); OrangeFill.normal.background = orange;
        }
    }
}
