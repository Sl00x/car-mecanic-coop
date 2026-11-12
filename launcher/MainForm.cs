using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CarMecaSim.Shared;

namespace CarMecaSim.Launcher
{
    /// <summary>
    /// "CARMECA" co-op launcher — faithful port of the reference web design: embedded Barlow Condensed /
    /// DM Mono fonts, Pexels garage photo background, exact lime (#DFFC38) + teal (#4CE4B5) palette,
    /// animated (reveal, pulse, spin, toasts, update progress, play "connecting", uninstall modal).
    /// </summary>
    public sealed class MainForm : Form
    {
        // palette (from the CSS)
        static readonly Color Bg     = C("#090c0d");
        static readonly Color SideBg = C("#0c0f11");
        static readonly Color Lime   = C("#dffc38");
        static readonly Color LimeHi = C("#edff79");
        static readonly Color Teal   = C("#4ce4b5");
        static readonly Color Danger = C("#ef6d64");
        static readonly Color Text   = C("#f2f4f2");
        static readonly Color Grey   = C("#b5bbbb");
        static readonly Color Dim    = C("#8f9998");
        static readonly Color Dim2   = C("#727d7e");
        static readonly Color Dim3   = C("#566061");
        static readonly Color Dim4   = C("#424b4c");
        static Color W(int a) => Color.FromArgb(a, 255, 255, 255);
        static Color C(string hex) { hex = hex.TrimStart('#'); return Color.FromArgb(Convert.ToInt32(hex.Substring(0, 2), 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16)); }

        sealed class BPanel : Panel { public BPanel() { DoubleBuffered = true; ResizeRedraw = true; } }

        // embedded assets
        static readonly PrivateFontCollection _pfc = new();
        static readonly List<IntPtr> _mem = new();
        static FontFamily? _head, _cond, _mono;
        static Image? _bg;

        string? _gameDir;
        InstallStatus _status = new();
        float _t, _reveal;                 // animation clocks
        readonly System.Windows.Forms.Timer _anim = new();
        string _toast = ""; float _toastUntil;
        int _progress = -1; bool _updating;
        float _playUntil; bool PlayLoading => _t < _playUntil;

        readonly BPanel _side = new();
        readonly BPanel _hero = new();
        readonly TextBox _name = new();
        readonly BPanel _play = new(); bool _playHover;
        readonly Button _update = new(); readonly Button _setup = new(); readonly Button _uninstall = new();
        readonly BPanel _modal = new(); readonly Button _mCancel = new(); readonly Button _mConfirm = new();

        public MainForm()
        {
            LoadAssets();
            base.Text = "CarMecaSim - Co-op Launcher";
            ClientSize = new Size(1260, 720);
            MinimumSize = new Size(1120, 650);
            BackColor = Bg; Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true;

            _hero.Dock = DockStyle.Fill; _hero.BackColor = Bg; _hero.Paint += PaintHero;
            Controls.Add(_hero);
            _side.Dock = DockStyle.Left; _side.Width = 300; _side.BackColor = SideBg; _side.Paint += PaintSidebar;
            Controls.Add(_side);

            _name.BorderStyle = BorderStyle.None; _name.BackColor = Bg; _name.ForeColor = Color.White;
            _name.Font = Mono(10.5f, false); _name.SetBounds(68, 0, 180, 18); _name.Text = Environment.UserName;
            _side.Controls.Add(_name);
            _name.TextChanged += (_, _) => { _hero.Invalidate(); _side.Invalidate(); };

            Util(_update, true); Util(_setup, false); Util(_uninstall, false);
            _side.Controls.Add(_update); _side.Controls.Add(_setup); _side.Controls.Add(_uninstall);
            _side.Resize += (_, _) => PlaceSide();

            _play.BackColor = Bg; _play.Cursor = Cursors.Hand; _play.Paint += PaintPlay;
            _play.MouseEnter += (_, _) => { _playHover = true; _play.Invalidate(); };
            _play.MouseLeave += (_, _) => { _playHover = false; _play.Invalidate(); };
            _play.Click += (_, _) => DoPlay();
            _hero.Controls.Add(_play);
            _hero.Resize += (_, _) => LayoutHero();

            _update.Click += async (_, _) => await DoUpdate();
            _setup.Click += (_, _) => DoSetup();
            _uninstall.Click += (_, _) => ShowModal(true);

            _modal.BackColor = C("#030607"); _modal.Visible = false; _modal.Paint += PaintModal;
            Controls.Add(_modal);
            Util(_mCancel, false); _mConfirm.Text = "DÉSINSTALLER"; _mConfirm.FlatStyle = FlatStyle.Flat;
            _mConfirm.FlatAppearance.BorderSize = 0; _mConfirm.BackColor = Danger; _mConfirm.ForeColor = C("#170a09");
            _mConfirm.Font = Sans(8f, FontStyle.Bold); _mConfirm.Cursor = Cursors.Hand; _mConfirm.UseVisualStyleBackColor = false;
            _mConfirm.FlatAppearance.MouseOverBackColor = C("#f2776e");
            _mCancel.Text = "ANNULER";
            _modal.Controls.Add(_mCancel); _modal.Controls.Add(_mConfirm);
            _mCancel.Click += (_, _) => ShowModal(false);
            _mConfirm.Click += (_, _) => { ShowModal(false); DoUninstall(); };
            _modal.Resize += (_, _) => LayoutModal();

            _anim.Interval = 30; _anim.Tick += OnAnim;
            Load += (_, _) => { RefreshStatus(); LayoutHero(); PlaceSide(); _anim.Start(); };
        }

        // ---------- assets ----------
        void LoadAssets()
        {
            try
            {
                var asm = typeof(MainForm).Assembly;
                foreach (var rn in asm.GetManifestResourceNames())
                {
                    if (rn.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
                    {
                        using var s = asm.GetManifestResourceStream(rn)!; var d = new byte[s.Length]; s.ReadExactly(d);
                        var p = Marshal.AllocCoTaskMem(d.Length); Marshal.Copy(d, 0, p, d.Length); _mem.Add(p);
                        _pfc.AddMemoryFont(p, d.Length);
                    }
                    else if (rn.EndsWith("garage_bg.jpg", StringComparison.OrdinalIgnoreCase))
                    { using var s = asm.GetManifestResourceStream(rn)!; _bg = Image.FromStream(s); }
                }
                foreach (var f in _pfc.Families)
                {
                    var n = f.Name;
                    if (n.IndexOf("ExtraBold", StringComparison.OrdinalIgnoreCase) >= 0) _head = f;
                    else if (n.IndexOf("SemiBold", StringComparison.OrdinalIgnoreCase) >= 0) _cond = f;
                    else if (n.IndexOf("DM Mono", StringComparison.OrdinalIgnoreCase) >= 0) _mono = f;
                }
                _head ??= Fam("Barlow"); _cond ??= _head; _mono ??= Fam("DM Mono");
            }
            catch { }
        }
        static FontFamily? Fam(string contains) { foreach (var f in _pfc.Families) if (f.Name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0) return f; return null; }
        static Font Pick(FontFamily? fam, float size, FontStyle style, string fallback)
        {
            if (fam != null) { foreach (var st in new[] { style, style | FontStyle.Italic, style & ~FontStyle.Bold, FontStyle.Italic, FontStyle.Regular }) if (fam.IsStyleAvailable(st)) return new Font(fam, size, st); }
            return new Font(fallback, size, style);
        }
        static Font Head(float size, bool italic = true) => Pick(_head, size, FontStyle.Bold | (italic ? FontStyle.Italic : 0), "Bahnschrift");
        static Font Cond(float size) => Pick(_cond, size, FontStyle.Bold, "Bahnschrift");
        static Font Mono(float size, bool medium) => Pick(_mono, size, medium ? FontStyle.Bold : FontStyle.Regular, "Consolas");
        static Font Sans(float size, FontStyle st) => new Font("Segoe UI", size, st);

        void OnAnim(object? s, EventArgs e)
        {
            _t += 0.03f; if (_reveal < 1f) _reveal = Math.Min(1f, _reveal + 0.05f);
            if (_updating && _progress < 95) _progress = Math.Min(95, _progress + 3);
            _hero.Invalidate(); _side.Invalidate(); if (_modal.Visible) _modal.Invalidate();
        }

        void PlaceSide()
        {
            int pad = 28, w = _side.Width - pad * 2;
            _update.SetBounds(pad, _side.Height - 174, w, 43);
            _setup.SetBounds(pad, _side.Height - 124, w, 43);
            _uninstall.SetBounds(pad, _side.Height - 74, w, 43);
            _name.SetBounds(68, SideIdentityY() + 13, _side.Width - 56 - 44, 18);
        }
        int SideIdentityY() => 470;
        void LayoutHero() => _play.SetBounds(Hx(), (int)(_hero.Height * 0.5) + 50, 440, 76);
        int Hx() => (int)Math.Max(38, Math.Min(100, _hero.Width * 0.06));
        void LayoutModal() { int mw = 410, mh = 250, mx = (_modal.Width - mw) / 2, my = (_modal.Height - mh) / 2; _mCancel.SetBounds(mx + 35, my + mh - 77, (mw - 70 - 8) / 2, 42); _mConfirm.SetBounds(mx + 35 + (mw - 70 - 8) / 2 + 8, my + mh - 77, (mw - 70 - 8) / 2, 42); }
        void ShowModal(bool on) { _modal.Bounds = ClientRectangle; _modal.Visible = on; if (on) { _modal.BringToFront(); LayoutModal(); } }

        // ================= HERO =================
        void PaintHero(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int w = _hero.Width, h = _hero.Height;
            float pulse = 0.69f + 0.31f * (float)Math.Sin(_t * 2.4);

            // background photo (cover), desaturated-ish via dark overlays
            if (_bg != null)
            {
                float ir = (float)_bg.Width / _bg.Height, dr = (float)w / h; RectangleF src;
                if (ir > dr) { float sw = _bg.Height * dr; src = new RectangleF((_bg.Width - sw) * 0.58f, _bg.Height * 0.10f, sw, _bg.Height * 0.9f); }
                else { float sh = _bg.Width / dr; src = new RectangleF(0, (_bg.Height - sh) * 0.48f, _bg.Width, sh); }
                g.DrawImage(_bg, new Rectangle(0, 0, w, h), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
            }
            else using (var b = new SolidBrush(Bg)) g.FillRectangle(b, 0, 0, w, h);
            // CSS ::after gradients (left→right dark, top/bottom dark)
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, w, h), C2(6, 9, 10, 0.90f), C2(6, 9, 10, 0.56f), 0f))
            { b.InterpolationColors = new ColorBlend { Colors = new[] { C2(6, 9, 10, 0.90f), C2(6, 9, 10, 0.58f), C2(6, 9, 10, 0.22f), C2(6, 9, 10, 0.56f) }, Positions = new[] { 0f, 0.42f, 0.75f, 1f } }; g.FillRectangle(b, 0, 0, w, h); }
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, 1, h), C2(5, 8, 9, 0.0f), C2(5, 8, 9, 0.0f), 90f))
            { b.InterpolationColors = new ColorBlend { Colors = new[] { C2(5, 8, 9, 0.93f), C2(5, 8, 9, 0f), C2(5, 8, 9, 0f), C2(5, 8, 9, 0.76f) }, Positions = new[] { 0f, 0.28f, 0.72f, 1f } }; g.FillRectangle(b, 0, 0, w, h); }

            // header
            using (var p = new Pen(W(20))) g.DrawLine(p, 0, 88, w, 88);
            int px = Hx();
            TextRenderer.DrawText(g, "CARMECA NETWORK", Sans(7f, FontStyle.Bold), new Point(px, 38), Dim2, LS);
            using (var p = new Pen(C("#444d4e"))) g.DrawLine(p, px + 120, 44, px + 139, 44);
            TextRenderer.DrawText(g, "GARAGE PARTAGÉ", Sans(7f, FontStyle.Bold), new Point(px + 150, 38), C("#adb4b3"), LS);
            string who = Who();
            TextRenderer.DrawText(g, who.ToUpperInvariant(), Mono(9f, false), new Rectangle(w - px - 210, 34, 138, 14), Text, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "DISPONIBLE", Sans(6f, FontStyle.Bold), new Rectangle(w - px - 210, 50, 138, 12), Teal, TextFormatFlags.Right | LS);
            Dot(g, w - px - 56, 44, 6, Teal, pulse);
            var av = new Rectangle(w - px - 37, 25, 37, 37);
            using (var b = new SolidBrush(C2(12, 16, 16, 0.7f))) g.FillRectangle(b, av);
            using (var p = new Pen(Color.FromArgb(76, 223, 252, 56))) g.DrawRectangle(p, av);
            TextRenderer.DrawText(g, Ini(who), Cond(12f), av, Lime, Center);

            // content (reveal slide-up)
            int cx = px, cyBase = (int)(h * 0.5f);
            int rise = (int)((1f - _reveal) * 14);
            var k = new Rectangle(cx, cyBase - 150 + rise, 198, 22);
            using (var b = new SolidBrush(Lime)) g.FillRectangle(b, new Rectangle(cx, k.Y, 70, 22));
            TextRenderer.DrawText(g, "MODE 01", Mono(8f, false), new Rectangle(cx, k.Y, 70, 22), C("#11150d"), Center);
            Dot(g, cx + 84, k.Y + 11, 4, Lime, 1f);
            TextRenderer.DrawText(g, "MULTIJOUEUR CO-OP", Mono(8f, false), new Point(cx + 96, k.Y + 5), C("#bdc5c4"), LS);

            using (var hf = Head(46f))
            {
                Shadowed(g, "LE GARAGE", hf, new Point(cx - 2, cyBase - 118 + rise), Text);
                Shadowed(g, "SANS LIMITES.", hf, new Point(cx - 2, cyBase - 80 + rise), Lime);
            }
            TextRenderer.DrawText(g, "Réparez, restaurez et développez votre atelier\navec votre équipe, en temps réel.",
                Sans(10f, FontStyle.Regular), new Rectangle(cx, cyBase - 4 + rise, 520, 44), C("#929c9b"), LS);

            // quick actions (below play button)
            int ay = (int)(h * 0.5) + 140;
            QAction(g, cx, ay, "server", "HÉBERGER", "Créer un salon", false);
            QAction(g, cx + 155, ay, "users", "REJOINDRE", "Rejoindre un ami", true);
            QAction(g, cx + 310, ay, "message", "CHAT", "Équipe en ligne", true);

            // protocol card
            ProtocolCard(g, w - px - 306, (int)(h * 0.5) - 158, 306, pulse);

            // footer bar
            using (var b = new SolidBrush(C2(5, 8, 9, 0.5f))) g.FillRectangle(b, 0, h - 55, w, 55);
            using (var p = new Pen(W(20))) g.DrawLine(p, 0, h - 55, w, h - 55);
            int fy = h - 28;
            Dot(g, px + 3, fy, 6, Teal, pulse);
            int fx = px + 16;
            fx = FootText(g, fx, fy, "PRÊT À JOUER", Dim3); fx = FootSep(g, fx, fy); fx = FootText(g, fx, fy, "PING 24 MS", Dim3); fx = FootSep(g, fx, fy); FootText(g, fx, fy, "EUROPE WEST", Dim3);
            var sr = "STEAM RELAY"; var szr = TextRenderer.MeasureText(g, sr, Mono(7f, false));
            int rx = w - px - 210;
            TextRenderer.DrawText(g, sr, Mono(7f, false), new Point(rx, fy - 6), Dim3, LS);
            TextRenderer.DrawText(g, "ACTIF", Mono(7f, true), new Point(rx + szr.Width + 6, fy - 6), Teal, LS);
            var kb = new Rectangle(w - px - 30, fy - 11, 26, 22);
            using (var p = new Pen(W(38))) g.DrawRectangle(p, kb);
            TextRenderer.DrawText(g, "F6", Mono(7f, false), kb, C("#aab1b0"), Center);

            // toast
            if (!string.IsNullOrEmpty(_toast) && _t < _toastUntil)
            {
                var tf = Mono(8f, false); var sz = TextRenderer.MeasureText(g, _toast, tf);
                int tw = sz.Width + 56, th = 34, tx = px + (w - px) / 2 - tw / 2, tyy = h - 55 - th - 18;
                using (var b = new SolidBrush(C2(8, 12, 12, 0.94f))) g.FillRectangle(b, tx, tyy, tw, th);
                using (var p = new Pen(Color.FromArgb(72, 76, 228, 181))) g.DrawRectangle(p, tx, tyy, tw, th);
                Dot(g, tx + 18, tyy + th / 2, 6, Teal, 1f);
                TextRenderer.DrawText(g, _toast, tf, new Rectangle(tx + 32, tyy, tw - 40, th), C("#cdd4d3"), Mid);
            }
        }

        int FootText(Graphics g, int x, int fy, string t, Color c) { TextRenderer.DrawText(g, t, Mono(7f, false), new Point(x, fy - 6), c, LS); return x + TextRenderer.MeasureText(g, t, Mono(7f, false)).Width + 8; }
        int FootSep(Graphics g, int x, int fy) { using var b = new SolidBrush(Dim3); g.FillEllipse(b, x, fy - 1, 2, 2); return x + 10; }

        void QAction(Graphics g, int x, int y, string icon, string title, string sub, bool border)
        {
            if (border) using (var p = new Pen(W(26))) g.DrawLine(p, x - 10, y - 6, x - 10, y + 30);
            IconDraw(g, icon, x + 8, y + 10, 17, Dim);
            TextRenderer.DrawText(g, title, Mono(7f, false), new Point(x + 28, y - 2), Dim, LS);
            TextRenderer.DrawText(g, sub, Sans(6.5f, FontStyle.Regular), new Point(x + 28, y + 13), Dim3, LS);
        }

        void ProtocolCard(Graphics g, int x, int y, int cw, float pulse)
        {
            int ch = 236;
            using (var b = new LinearGradientBrush(new Rectangle(x, y, cw, ch), C2(18, 22, 23, 0.92f), C2(9, 12, 13, 0.80f), 125f)) g.FillRectangle(b, x, y, cw, ch);
            using (var b = new SolidBrush(Lime)) g.FillRectangle(b, x, y, cw, 2);
            TextRenderer.DrawText(g, "PROTOCOLE CO-OP", Mono(8f, false), new Point(x + 21, y + 18), Lime, LS);
            TextRenderer.DrawText(g, "4 ÉTAPES", Mono(7f, false), new Rectangle(x, y + 19, cw - 21, 12), C("#566060"), TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            bool ready = _status.FullyReady;
            var steps = new (string t, string s, bool done)[] { ("Launcher prêt", "Fichiers vérifiés", true), ("Profil chargé", Who() + " connecté", true), ("Choisir une session", "Héberger ou rejoindre", ready), ("Inviter l'équipe", "Via l'overlay Steam", false) };
            int sy = y + 46; int connX = x + 21 + 24 + 13;
            using (var p = new Pen(W(26))) g.DrawLine(p, connX, sy + 10, connX, sy + 3 * 44 + 8);
            for (int i = 0; i < 4; i++)
            {
                int ry = sy + i * 44;
                TextRenderer.DrawText(g, (i + 1).ToString("00"), Mono(7f, false), new Point(x + 21, ry + 4), C("#727c7c"), LS);
                var ic = new Rectangle(connX - 8, ry, 17, 17);
                if (steps[i].done) { using (var b = new SolidBrush(C("#16382f"))) g.FillEllipse(b, ic); using (var p = new Pen(Teal)) g.DrawEllipse(p, ic); IconDraw(g, "check", ic.X + 8, ic.Y + 8, 10, Teal); }
                else { using (var p = new Pen(C("#4d5657"))) g.DrawEllipse(p, ic); }
                TextRenderer.DrawText(g, steps[i].t, Mono(8f, false), new Point(x + 21 + 51, ry - 1), C("#c7cccb"), LS);
                TextRenderer.DrawText(g, steps[i].s, Sans(6.5f, FontStyle.Regular), new Point(x + 21 + 51, ry + 14), Dim3, LS);
            }
            using (var p = new Pen(W(20))) g.DrawLine(p, x + 21, y + ch - 30, x + cw - 21, y + ch - 30);
            Dot(g, x + 24, y + ch - 18, 5, Teal, pulse);
            TextRenderer.DrawText(g, "TOUS LES SERVICES SONT OPÉRATIONNELS", Sans(6.5f, FontStyle.Bold), new Point(x + 34, y + ch - 24), C("#667171"), LS);
        }

        void PaintPlay(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            bool on = _play.Enabled; int w = _play.Width, h = _play.Height;
            var col = !on ? C("#2a2e2a") : _playHover ? LimeHi : Lime;
            // slanted-right-edge polygon (CSS ::after triangle)
            var poly = new[] { new Point(0, 0), new Point(w, 0), new Point(w - 10, h), new Point(0, h) };
            using (var b = new SolidBrush(col)) g.FillPolygon(b, poly);
            var box = new Rectangle(13, h / 2 - 24, 48, 48);
            using (var b = new SolidBrush(Color.FromArgb(28, 10, 15, 6))) g.FillRectangle(b, box);
            var ic = on ? C("#11150d") : Dim;
            if (PlayLoading) { var old = g.Transform.Clone(); g.TranslateTransform(box.X + 24, box.Y + 24); g.RotateTransform(_t * 300f); IconDraw(g, "settings", 0, 0, 23, ic); g.Transform = old; }
            else using (var b = new SolidBrush(ic)) g.FillPolygon(b, new[] { new Point(box.X + 18, box.Y + 14), new Point(box.X + 18, box.Y + 34), new Point(box.X + 34, box.Y + 24) });
            using (var hf = Head(20f))
                TextRenderer.DrawText(g, PlayLoading ? "CONNEXION…" : "JOUER MAINTENANT", hf, new Point(72, 14), ic, LS);
            TextRenderer.DrawText(g, !on ? "CONFIGURATION REQUISE" : PlayLoading ? "SYNCHRONISATION DU GARAGE" : "LANCER CAR MECHANIC SIMULATOR 2021", Sans(6.5f, FontStyle.Bold), new Point(74, 44), Color.FromArgb(on ? 150 : 120, 17, 21, 13), LS);
            IconDraw(g, "chevron", w - 28, h / 2, 22, ic);
        }

        // ================= SIDEBAR =================
        void PaintSidebar(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int w = _side.Width, pad = 28;
            using (var b = new SolidBrush(Color.FromArgb(76, 223, 252, 56))) g.FillRectangle(b, w - 1, 0, 1, _side.Height);  // lime border
            using (var b = new SolidBrush(Lime)) g.FillRectangle(b, w - 2, 0, 2, 85);                                        // lime tab

            // head
            using (var p = new Pen(W(20))) g.DrawLine(p, 0, 104, w, 104);
            var logo = new[] { new Point(pad + 6, 28), new Point(pad + 6 + 43, 28), new Point(pad + 43, 28 + 48), new Point(pad, 28 + 48) };
            using (var b = new SolidBrush(Lime)) g.FillPolygon(b, logo);
            DrawGear(g, pad + 24, 52, 12, 8.5f, 8, C("#101409"));
            using (var hf = Head(21f))
            {
                TextRenderer.DrawText(g, "CARMECA", hf, new Point(72, 32), Text, LS);
                int cw = TextRenderer.MeasureText(g, "CARMECA", hf).Width;
                TextRenderer.DrawText(g, "SIM", hf, new Point(72 + cw - 6, 32), Lime, LS);
            }
            TextRenderer.DrawText(g, "C O - O P   L A U N C H E R", Sans(6f, FontStyle.Bold), new Point(73, 56), C("#7e8989"), LS);
            var vt = new Rectangle(w - pad - 32, 30, 32, 16);
            using (var p = new Pen(W(30))) g.DrawRectangle(p, vt);
            TextRenderer.DrawText(g, "v" + Constants.ModVersion, Mono(7f, false), vt, C("#667071"), Center);

            // library
            int y = 104;
            y = SectionHead(g, "BIBLIOTHÈQUE", "01", y + 23);
            var row = new Rectangle(pad - 10, y, w - pad * 2 + 20, 62);
            using (var b = new LinearGradientBrush(row, Color.FromArgb(26, 223, 252, 56), W(6), 0f)) g.FillRectangle(b, row);
            using (var p = new Pen(W(20))) g.DrawRectangle(p, row);
            using (var b = new SolidBrush(Lime)) g.FillRectangle(b, row.X, row.Y, 2, row.Height);
            var gi = new Rectangle(row.X + 14, row.Y + 12, 38, 38);
            using (var b = new SolidBrush(Color.FromArgb(33, 223, 252, 56))) g.FillRectangle(b, gi);
            DrawGear(g, gi.X + 19, gi.Y + 19, 11, 8, 9, Lime);
            TextRenderer.DrawText(g, "Car Mechanic Simulator 2021", Cond(10.5f), new Point(row.X + 62, row.Y + 13), C("#e9ece9"), LS);
            TextRenderer.DrawText(g, "C O - O P   M U L T I J O U E U R", Sans(6f, FontStyle.Bold), new Point(row.X + 63, row.Y + 34), C("#747e7e"), LS);
            Dot(g, row.Right - 22, row.Y + 31, 5, Teal, 1f);
            y = row.Bottom + 23;
            using (var p = new Pen(W(18))) g.DrawLine(p, pad, y - 1, w - pad, y - 1);

            // system
            y = SectionHead(g, "ÉTAT DU SYSTÈME", "3/3", y + 23);
            y = ServiceRow(g, y, "BepInEx", "Runtime mod", _status.LoaderInstalled, "4 ms");
            y = ServiceRow(g, y + 13, "Mod Co-op", "v" + Constants.ModVersion, _status.ModInstalled, "12 ms");
            y = ServiceRow(g, y + 13, "Interop", "Steam relay", _status.InteropGenerated, "8 ms");
            y += 23; using (var p = new Pen(W(18))) g.DrawLine(p, pad, y - 1, w - pad, y - 1);

            // identity
            int iy = SideIdentityY();
            TextRenderer.DrawText(g, "IDENTITÉ DU MÉCANO", Mono(8f, false), new Point(pad, iy - 18), Dim2, LS);
            var iw = new Rectangle(pad, iy, w - pad * 2, 36);
            using (var b = new SolidBrush(Bg)) g.FillRectangle(b, iw);
            using (var p = new Pen(W(36))) g.DrawRectangle(p, iw);
            using (var b = new SolidBrush(W(6))) g.FillRectangle(b, iw.X, iw.Y, 40, iw.Height);
            using (var p = new Pen(W(26))) g.DrawLine(p, iw.X + 40, iw.Y, iw.X + 40, iw.Bottom);
            TextRenderer.DrawText(g, Ini(Who()), Cond(13f), new Rectangle(iw.X, iw.Y, 40, iw.Height), Lime, Center);
            Dot(g, iw.Right - 14, iw.Y + iw.Height / 2, 5, Teal, 1f);
            TextRenderer.DrawText(g, "U T I L I S É   E N   S E S S I O N   C O - O P", Sans(5.5f, FontStyle.Bold), new Point(pad, iy + 42), C("#414a4b"), LS);

            // update progress
            if (_progress >= 0)
            {
                var pr = new Rectangle(pad, _update.Top - 9, w - pad * 2, 2);
                using (var b = new SolidBrush(W(20))) g.FillRectangle(b, pr);
                using (var b = new SolidBrush(Lime)) g.FillRectangle(b, pr.X, pr.Y, Math.Max(2, pr.Width * _progress / 100), 2);
            }
        }

        int SectionHead(Graphics g, string t, string count, int y)
        {
            TextRenderer.DrawText(g, t, Mono(8f, false), new Point(28, y), Dim2, LS);
            TextRenderer.DrawText(g, count, Mono(8f, false), new Rectangle(0, y, _side.Width - 28, 12), Dim4, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            return y + 24;
        }

        int ServiceRow(Graphics g, int y, string name, string detail, bool ok, string ms)
        {
            int w = _side.Width, pad = 28;
            var d = new Rectangle(pad, y + 1, 13, 13);
            if (ok) { using (var b = new SolidBrush(Color.FromArgb(36, 76, 228, 181))) g.FillRectangle(b, d); IconDraw(g, "check", d.X + 6, d.Y + 6, 10, Teal); }
            else { using (var p = new Pen(Danger, 1.5f)) g.DrawRectangle(p, d); }
            TextRenderer.DrawText(g, name.ToUpperInvariant(), Mono(9f, false), new Point(pad + 23, y - 1), ok ? Text : Dim2, LS);
            TextRenderer.DrawText(g, detail, Sans(6.5f, FontStyle.Regular), new Point(pad + 23, y + 14), C("#444e4f"), LS);
            TextRenderer.DrawText(g, "EN LIGNE", Mono(7f, false), new Rectangle(0, y - 1, w - pad, 12), ok ? Teal : Danger, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            if (ok) TextRenderer.DrawText(g, ms, Mono(7f, false), new Rectangle(0, y + 13, w - pad - 56, 12), C("#596364"), TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            return y + 20;
        }

        void PaintModal(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int mw = 410, mh = 250, mx = (_modal.Width - mw) / 2, my = (_modal.Height - mh) / 2;
            using (var b = new SolidBrush(C("#111516"))) g.FillRectangle(b, mx, my, mw, mh);
            using (var p = new Pen(W(30))) g.DrawRectangle(p, mx, my, mw, mh);
            using (var b = new SolidBrush(Danger)) g.FillRectangle(b, mx, my, mw, 2);
            var cl = new Rectangle(mx + mw - 47, my + 14, 33, 33);
            using (var p = new Pen(W(25))) g.DrawRectangle(p, cl);
            IconDraw(g, "close", cl.X + 16, cl.Y + 16, 16, C("#687273"));
            var ib = new Rectangle(mx + 35, my + 35, 45, 45);
            using (var b = new SolidBrush(Color.FromArgb(26, 239, 109, 100))) g.FillRectangle(b, ib);
            IconDraw(g, "trash", ib.X + 22, ib.Y + 22, 24, Danger);
            TextRenderer.DrawText(g, "A C T I O N   S Y S T È M E", Mono(7f, false), new Point(mx + 35, my + 92), Danger, LS);
            using (var hf = Head(26f, false)) TextRenderer.DrawText(g, "Désinstaller le mod Co-op ?", hf, new Point(mx + 33, my + 104), Text, LS);
            TextRenderer.DrawText(g, "Le jeu principal et vos sauvegardes ne seront pas supprimés.", Sans(9.5f, FontStyle.Regular), new Point(mx + 35, my + 146), C("#788182"), LS);
        }

        // ================= line icons (stroked, SVG viewBox 24) =================
        void IconDraw(Graphics g, string name, float cx, float cy, float size, Color col)
        {
            float s = size / 24f, ox = cx - size / 2f, oy = cy - size / 2f;
            PointF M(float x, float y) => new PointF(ox + x * s, oy + y * s);
            using var pen = new Pen(col, Math.Max(1.1f, 1.7f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            switch (name)
            {
                case "check": g.DrawLines(pen, new[] { M(5, 12), M(10, 17), M(19, 6) }); break;
                case "chevron": g.DrawLines(pen, new[] { M(9, 18), M(15, 12), M(9, 6) }); break;
                case "close": g.DrawLine(pen, M(18, 6), M(6, 18)); g.DrawLine(pen, M(6, 6), M(18, 18)); break;
                case "play": using (var b = new SolidBrush(col)) g.FillPolygon(b, new[] { M(8, 5.5f), M(8, 18.5f), M(18, 12) }); break;
                case "trash": g.DrawLine(pen, M(4, 7), M(20, 7)); g.DrawLines(pen, new[] { M(9, 7), M(9, 4), M(15, 4), M(15, 7) }); g.DrawLines(pen, new[] { M(18, 7), M(17, 21), M(7, 21), M(6, 7) }); g.DrawLine(pen, M(10, 11), M(10, 17)); g.DrawLine(pen, M(14, 11), M(14, 17)); break;
                case "wrench": g.DrawLines(pen, new[] { M(14.7f, 6.3f), M(9.6f, 6f), M(7.3f, 3.7f), M(4f, 17f), M(7f, 20f), M(17.4f, 9f), M(14.7f, 6.3f) }); break;
                case "download": g.DrawLine(pen, M(12, 3), M(12, 15)); g.DrawLines(pen, new[] { M(7, 10), M(12, 15), M(17, 10) }); g.DrawLine(pen, M(5, 21), M(19, 21)); break;
                case "users":
                    g.DrawEllipse(pen, M(6, 2).X, M(6, 2).Y, 6 * s, 6 * s);
                    g.DrawLines(pen, new[] { M(2, 20), M(2, 18), M(4, 14), M(14, 14), M(16, 18), M(16, 20) });
                    g.DrawLines(pen, new[] { M(18, 14.5f), M(21, 17), M(21, 20) }); break;
                case "message":
                    g.DrawRectangle(pen, M(3, 3).X, M(3, 3).Y, 17 * s, 13 * s); g.DrawLines(pen, new[] { M(7, 16), M(5, 20), M(10, 16) }); break;
                case "server":
                    g.DrawRectangle(pen, M(3, 4).X, M(3, 4).Y, 18 * s, 6 * s); g.DrawRectangle(pen, M(3, 14).X, M(3, 14).Y, 18 * s, 6 * s);
                    g.DrawLine(pen, M(6.8f, 7), M(7.2f, 7)); g.DrawLine(pen, M(6.8f, 17), M(7.2f, 17)); break;
                case "settings":
                    g.DrawEllipse(pen, M(9, 9).X, M(9, 9).Y, 6 * s, 6 * s);
                    for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(pen, new PointF(cx + (float)Math.Cos(a) * 7 * s, cy + (float)Math.Sin(a) * 7 * s), new PointF(cx + (float)Math.Cos(a) * 11 * s, cy + (float)Math.Sin(a) * 11 * s)); }
                    break;
                default: g.DrawEllipse(pen, M(8, 8).X, M(8, 8).Y, 8 * s, 8 * s); break;
            }
        }

        // ================= helpers =================
        static readonly TextFormatFlags LS = TextFormatFlags.NoPrefix;
        static readonly TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        static readonly TextFormatFlags Mid = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        static Color C2(int r, int gg, int b, float a) => Color.FromArgb((int)(a * 255), r, gg, b);
        static PointF Pt(float cx, float cy, float r, double a) => new PointF(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r));
        void Dot(Graphics g, int cx, int cy, int d, Color col, float a) { using var b = new SolidBrush(Color.FromArgb((int)(Math.Min(1f, a) * col.A), col)); g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d); }
        void RadialGlow(Graphics g, PointF c, float radius, Color col) { using var path = new GraphicsPath(); path.AddEllipse(c.X - radius, c.Y - radius, radius * 2, radius * 2); using var pg = new PathGradientBrush(path) { CenterPoint = c, CenterColor = col, SurroundColors = new[] { Color.FromArgb(0, col) } }; g.FillPath(pg, path); }
        static void DrawGear(Graphics g, float cx, float cy, float rOut, float rIn, int teeth, Color col)
        {
            var pts = new List<PointF>(); double step = Math.PI * 2 / teeth;
            for (int i = 0; i < teeth; i++) { double a = i * step; pts.Add(Pt(cx, cy, rIn, a - step * 0.26)); pts.Add(Pt(cx, cy, rOut, a - step * 0.17)); pts.Add(Pt(cx, cy, rOut, a + step * 0.17)); pts.Add(Pt(cx, cy, rIn, a + step * 0.26)); }
            using var b = new SolidBrush(col); g.FillPolygon(b, pts.ToArray());
            float hr = rIn * 0.45f; using var hub = new SolidBrush(Color.FromArgb(Math.Max(col.A - 2, 8), 223, 252, 56)); g.FillEllipse(hub, cx - hr, cy - hr, hr * 2, hr * 2);
        }
        void Shadowed(Graphics g, string s, Font f, Point p, Color c) { TextRenderer.DrawText(g, s, f, new Point(p.X + 2, p.Y + 4), Color.FromArgb(90, 0, 0, 0), LS); TextRenderer.DrawText(g, s, f, p, c, LS); }
        void Util(Button b, bool primary)
        {
            b.Text = primary ? "METTRE À JOUR" : (b == _setup ? "RÉPARER / INSTALLER" : (b == _uninstall ? "DÉSINSTALLER" : "ANNULER"));
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = primary ? 0 : 1; b.FlatAppearance.BorderColor = W(33);
            b.ForeColor = primary ? C("#12170d") : C("#b5bbbb"); b.BackColor = primary ? Lime : W(6);
            b.Font = Sans(8.5f, FontStyle.Bold); b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false; b.TextAlign = ContentAlignment.MiddleCenter;
            b.FlatAppearance.MouseOverBackColor = primary ? LimeHi : W(13); b.FlatAppearance.MouseDownBackColor = primary ? LimeHi : W(16);
        }
        string Who() => string.IsNullOrWhiteSpace(_name.Text) ? Environment.UserName : _name.Text.Trim();
        static string Ini(string s) { s = (s ?? "").Trim(); return s.Length == 0 ? "MC" : (s.Length >= 2 ? s.Substring(0, 2) : s).ToUpperInvariant(); }
        void Toast(string s) { _toast = s; _toastUntil = _t + 2.8f; _hero.Invalidate(); }

        // ================= logic =================
        async System.Threading.Tasks.Task DoUpdate()
        {
            if (_gameDir == null) return;
            _updating = true; _progress = 1; Toast("vérification des mises à jour…");
            var info = await GithubUpdater.CheckAsync(_gameDir);
            if (!info.Configured || !string.IsNullOrEmpty(info.Error)) { _updating = false; _progress = -1; Toast(info.Configured ? ("MAJ: " + info.Error) : "dépôt de MAJ non configuré"); return; }
            if (!info.HasUpdate) { _updating = false; _progress = 100; Toast($"déjà à jour (v{info.Current})"); await System.Threading.Tasks.Task.Delay(900); _progress = -1; _side.Invalidate(); return; }
            Toast($"mise à jour v{info.Current} → v{info.Tag}…");
            var ok = await GithubUpdater.UpdateAsync(_gameDir, info, Toast);
            _updating = false; _progress = 100; Toast(ok ? $"✓ mis à jour en v{info.Tag}" : "échec de la mise à jour"); RefreshStatus();
            await System.Threading.Tasks.Task.Delay(1200); _progress = -1; _side.Invalidate();
        }

        void RefreshStatus()
        {
            _gameDir = GameLocator.Find();
            if (_gameDir == null) { _status = new InstallStatus(); _play.Enabled = _uninstall.Enabled = false; _setup.Enabled = true; }
            else { _status = new ModInstaller(_gameDir).GetStatus(); _play.Enabled = _status.FullyReady; _setup.Enabled = true; _uninstall.Enabled = _status.LoaderInstalled || _status.ModInstalled; }
            _side.Invalidate(); _play.Invalidate(); _hero.Invalidate();
        }

        void DoSetup()
        {
            if (_gameDir == null) return;
            var payload = ModInstaller.FindPayloadRoot();
            if (payload == null) { Toast("payload d'installation introuvable"); return; }
            var inst = new ModInstaller(_gameDir); Toast("installation de BepInEx…");
            if (!inst.EnsureLoader(payload, Toast)) { Toast("échec BepInEx"); return; }
            InteropPatcher.EnsurePatched(_gameDir, Toast); inst.InstallMod(payload, Toast); inst.EnsureAppId(Toast);
            Toast("installé · 1er lancement = génération interop"); RefreshStatus();
        }

        void DoUninstall() { if (_gameDir == null) return; new ModInstaller(_gameDir).Uninstall(false, Toast); RefreshStatus(); Toast("mod co-op désinstallé"); }

        void DoPlay()
        {
            if (_gameDir == null || !_play.Enabled || PlayLoading) return;
            var cfg = new MpConfig { Role = MpRole.Menu, DisplayName = Who(), AutoStart = true };
            new GameRunner(_gameDir).WriteConfig(cfg);
            _playUntil = _t + 3.5f; Toast("Connexion au garage partagé en tant que " + cfg.DisplayName + "…");
            new GameRunner(_gameDir).Launch();
        }
    }
}
