using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using CarMecaSim.Shared;

namespace CarMecaSim.Launcher
{
    /// <summary>
    /// Cinematic dark "CARMECA" co-op launcher — lime accent, line icons, animated (toasts, update
    /// progress, play "connecting" state, pulsing status, uninstall modal). Mirrors the reference design.
    /// </summary>
    public sealed class MainForm : Form
    {
        static readonly Color Bg    = Color.FromArgb(12, 13, 12);
        static readonly Color Panel = Color.FromArgb(17, 19, 17);
        static readonly Color Card   = Color.FromArgb(23, 25, 23);
        static readonly Color CardHi = Color.FromArgb(30, 33, 30);
        static readonly Color Lime   = Color.FromArgb(201, 246, 54);
        static readonly Color LimeHi = Color.FromArgb(216, 255, 96);
        static readonly Color White  = Color.FromArgb(238, 240, 236);
        static readonly Color Dim    = Color.FromArgb(124, 130, 122);
        static readonly Color Faint  = Color.FromArgb(78, 84, 76);
        static readonly Color Danger = Color.FromArgb(224, 96, 84);
        static readonly Color Line   = Color.FromArgb(34, 255, 255, 255);

        sealed class BPanel : Panel { public BPanel() { DoubleBuffered = true; ResizeRedraw = true; } }

        string? _gameDir;
        InstallStatus _status = new();
        string _activity = "";
        float _t;
        readonly System.Windows.Forms.Timer _anim = new();

        // interactive state
        string _toast = ""; float _toastUntil;
        int _progress = -1;            // update progress 0..100, -1 = idle
        bool _updating;
        float _playUntil;              // play "connecting" state until this time
        bool PlayLoading => _t < _playUntil;

        readonly BPanel _side = new();
        readonly BPanel _hero = new();
        readonly TextBox _name = new();
        readonly BPanel _play = new(); bool _playHover;
        readonly Button _update = new();
        readonly Button _setup = new();
        readonly Button _uninstall = new();

        // uninstall modal
        readonly BPanel _modal = new();
        readonly Button _mCancel = new();
        readonly Button _mConfirm = new();

        public MainForm()
        {
            Text = "CarMecaSim — Co-op Launcher";
            ClientSize = new Size(1240, 720);
            MinimumSize = new Size(1080, 640);
            BackColor = Bg; Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true;

            _hero.Dock = DockStyle.Fill; _hero.BackColor = Bg; _hero.Paint += PaintHero;
            Controls.Add(_hero);
            _side.Dock = DockStyle.Left; _side.Width = 290; _side.BackColor = Panel; _side.Paint += PaintSidebar;
            Controls.Add(_side);

            _name.BorderStyle = BorderStyle.None; _name.BackColor = Color.FromArgb(28, 31, 28);
            _name.ForeColor = White; _name.Font = new Font("Consolas", 11.5f);
            _name.SetBounds(74, 400, 188, 20); _name.Text = Environment.UserName;
            _side.Controls.Add(_name);
            _name.TextChanged += (_, _) => { _hero.Invalidate(); _side.Invalidate(); };

            FlatBtn(_update, "METTRE À JOUR", true);
            FlatBtn(_setup, "RÉPARER / INSTALLER", false);
            FlatBtn(_uninstall, "DÉSINSTALLER", false);
            _side.Controls.Add(_update); _side.Controls.Add(_setup); _side.Controls.Add(_uninstall);
            _side.Resize += (_, _) => PlaceSideButtons();

            _play.BackColor = Bg; _play.Cursor = Cursors.Hand; _play.Paint += PaintPlay;
            _play.MouseEnter += (_, _) => { _playHover = true; _play.Invalidate(); };
            _play.MouseLeave += (_, _) => { _playHover = false; _play.Invalidate(); };
            _play.Click += (_, _) => DoPlay();
            _hero.Controls.Add(_play);
            _hero.Resize += (_, _) => LayoutHero();

            _update.Click += async (_, _) => await DoUpdate();
            _setup.Click += (_, _) => DoSetup();
            _uninstall.Click += (_, _) => ShowUninstall(true);

            // uninstall modal
            _modal.BackColor = Color.FromArgb(205, 6, 7, 6); _modal.Visible = false; _modal.Paint += PaintModal;
            Controls.Add(_modal); _modal.BringToFront();
            FlatBtn(_mCancel, "ANNULER", false);
            FlatBtn(_mConfirm, "DÉSINSTALLER", false); _mConfirm.ForeColor = Color.White; _mConfirm.BackColor = Danger;
            _mConfirm.FlatAppearance.MouseOverBackColor = Color.FromArgb(236, 112, 100);
            _modal.Controls.Add(_mCancel); _modal.Controls.Add(_mConfirm);
            _mCancel.Click += (_, _) => ShowUninstall(false);
            _mConfirm.Click += (_, _) => { ShowUninstall(false); DoUninstall(); };
            _modal.Resize += (_, _) => LayoutModal();

            _anim.Interval = 33; _anim.Tick += OnAnim;
            Load += (_, _) => { RefreshStatus(); LayoutHero(); PlaceSideButtons(); _anim.Start(); };
        }

        void OnAnim(object? s, EventArgs e)
        {
            _t += 0.033f;
            if (_updating && _progress < 95) _progress = Math.Min(95, _progress + 3);   // climb while the real update runs
            _hero.Invalidate(); _side.Invalidate();
            if (_modal.Visible) _modal.Invalidate();
        }

        void PlaceSideButtons()
        {
            int w = _side.Width - 44;
            _update.SetBounds(22, _side.Height - 134, w, 42);
            _setup.SetBounds(22, _side.Height - 86, w, 36);
            _uninstall.SetBounds(22, _side.Height - 46, w, 32);
        }
        void LayoutHero() => _play.SetBounds(44, _hero.Height - 168, 372, 64);
        void LayoutModal()
        {
            int mw = 440, mh = 250, mx = (_modal.Width - mw) / 2, my = (_modal.Height - mh) / 2;
            _mCancel.SetBounds(mx + 30, my + mh - 62, 180, 40);
            _mConfirm.SetBounds(mx + mw - 210, my + mh - 62, 180, 40);
        }
        void ShowUninstall(bool on)
        {
            _modal.Bounds = ClientRectangle; _modal.Visible = on; if (on) { _modal.BringToFront(); LayoutModal(); }
        }

        // ================= HERO =================
        void PaintHero(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int w = _hero.Width, h = _hero.Height;
            float pulse = 0.5f + 0.5f * (float)Math.Sin(_t * 2.2);

            using (var br = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(18, 20, 18), Color.FromArgb(9, 10, 9), 55f))
                g.FillRectangle(br, 0, 0, w, h);
            RadialGlow(g, new PointF(w * 0.72f, h * 0.34f), Math.Max(w, h) * 0.55f, Color.FromArgb(24, 201, 246, 54));
            DrawGear(g, w - 120, h * 0.42f, 320, 268, 24, Color.FromArgb(8, 255, 255, 255));
            int lw = (int)(w * 0.62f);
            using (var v = new LinearGradientBrush(new Rectangle(0, 0, lw, h), Color.FromArgb(205, 8, 9, 8), Color.FromArgb(0, 0, 0, 0), 0f))
                g.FillRectangle(v, 0, 0, lw, h);
            using (var vb = new LinearGradientBrush(new Rectangle(0, h - 280, w, 280), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(225, 7, 8, 7), 90f))
                g.FillRectangle(vb, 0, h - 280, w, 280);
            float sy = (h + 160) * ((_t * 0.06f) % 1f) - 80;
            using (var sh = new LinearGradientBrush(new RectangleF(0, sy - 80, w, 160), Color.FromArgb(0, 201, 246, 54), Color.FromArgb(9, 201, 246, 54), 90f))
            { sh.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(0, 201, 246, 54), Color.FromArgb(9, 201, 246, 54), Color.FromArgb(0, 201, 246, 54) }, Positions = new[] { 0f, 0.5f, 1f } }; g.FillRectangle(sh, 0, sy - 80, w, 160); }

            // top bar
            TextRenderer.DrawText(g, "CARMECA NETWORK", Mono(8.5f, true), new Point(36, 26), Dim, Flags);
            using (var p = new Pen(Faint)) g.DrawLine(p, 168, 33, 196, 33);
            TextRenderer.DrawText(g, "GARAGE PARTAGÉ", Mono(8.5f, true), new Point(204, 26), Dim, Flags);
            string who = Who();
            TextRenderer.DrawText(g, who.ToUpperInvariant(), UI(10f, FontStyle.Bold), new Rectangle(w - 254, 22, 160, 16), White, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            using (var b = new SolidBrush(Color.FromArgb((int)(150 + 100 * pulse), Lime))) g.FillEllipse(b, w - 160, 44, 7, 7);
            TextRenderer.DrawText(g, "DISPONIBLE", Mono(7.5f, true), new Rectangle(w - 254, 41, 88, 14), Lime, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            var av = new Rectangle(w - 64, 22, 34, 34);
            using (var b = new SolidBrush(Lime)) g.FillPath(b, Round(av, 7));
            TextRenderer.DrawText(g, Initials(who), UI(10f, FontStyle.Bold), av, Color.Black, Center);

            // headline
            int hy = h - 360;
            var tag = new Rectangle(36, hy, 200, 22);
            using (var b = new SolidBrush(Lime)) g.FillPath(b, Round(tag, 3));
            TextRenderer.DrawText(g, "MODE 01  ·  MULTIJOUEUR CO-OP", Mono(8f, true), tag, Color.Black, Center);
            using var big = new Font("Segoe UI Black", 42f, FontStyle.Bold | FontStyle.Italic);
            Shadowed(g, "LE GARAGE", big, new Point(32, hy + 30), White);
            Shadowed(g, "SANS LIMITES.", big, new Point(32, hy + 86), Lime);
            TextRenderer.DrawText(g, "Réparez, restaurez et développez votre atelier\navec votre équipe, en temps réel.",
                UI(10.5f, FontStyle.Regular), new Rectangle(36, hy + 150, 520, 44), Dim, Flags);

            // quick actions
            int ay = h - 84;
            Action3(g, 46, ay, "server", "HÉBERGER", "Créer un salon");
            Action3(g, 210, ay, "users", "REJOINDRE", "Rejoindre un ami");
            Action3(g, 374, ay, "message", "CHAT", "Équipe en ligne");

            ProtocolCard(g, w - 300, hy - 18, 268, 246, pulse);

            // bottom status strip
            using (var b = new SolidBrush(Color.FromArgb(180, 6, 7, 6))) g.FillRectangle(b, 0, h - 28, w, 28);
            using (var b = new SolidBrush(Color.FromArgb((int)(120 + 120 * pulse), Lime))) g.FillEllipse(b, 16, h - 20, 9, 9);
            bool ready = _status.FullyReady;
            TextRenderer.DrawText(g, (ready ? "PRÊT À JOUER" : "CONFIGURATION REQUISE") + "   ·   PING 24 MS   ·   EUROPE WEST",
                Mono(8.5f, true), new Rectangle(34, h - 28, 620, 28), ready ? Dim : Color.FromArgb(224, 150, 60), Mid);
            TextRenderer.DrawText(g, "STEAM RELAI  " + (ready ? "ACTIF" : "—") + "     F6", Mono(8.5f, true), new Rectangle(w - 250, h - 28, 220, 28), Dim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            // toast
            if (!string.IsNullOrEmpty(_toast) && _t < _toastUntil)
            {
                var sz = TextRenderer.MeasureText(_toast, UI(9.5f, FontStyle.Bold));
                int tw = sz.Width + 60, th = 40, tx = (w - tw) / 2, tyy = h - 92;
                using (var b = new SolidBrush(Color.FromArgb(240, 26, 29, 26))) g.FillPath(b, Round(new Rectangle(tx, tyy, tw, th), 8));
                using (var p = new Pen(Color.FromArgb(70, 201, 246, 54))) g.DrawPath(p, Round(new Rectangle(tx, tyy, tw, th), 8));
                using (var b = new SolidBrush(Lime)) g.FillEllipse(b, tx + 20, tyy + th / 2 - 4, 8, 8);
                TextRenderer.DrawText(g, _toast, UI(9.5f, FontStyle.Bold), new Rectangle(tx + 38, tyy, tw - 44, th), White, Mid);
            }
        }

        void Action3(Graphics g, int x, int y, string icon, string title, string sub)
        {
            using (var p = new Pen(Line)) g.DrawLine(p, x, y - 10, x, y + 34);
            Icon(g, icon, x + 20, y + 10, 17, Lime);
            TextRenderer.DrawText(g, title, UI(9f, FontStyle.Bold), new Point(x + 38, y - 4), White, Flags);
            TextRenderer.DrawText(g, sub, Mono(7.5f, false), new Point(x + 38, y + 14), Dim, Flags);
        }

        void ProtocolCard(Graphics g, int x, int y, int cw, int ch, float pulse)
        {
            var r = new Rectangle(x, y, cw, ch);
            using (var b = new SolidBrush(Color.FromArgb(235, Card.R, Card.G, Card.B))) g.FillPath(b, Round(r, 10));
            using (var p = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawPath(p, Round(r, 10));
            TextRenderer.DrawText(g, "PROTOCOLE CO-OP", Mono(9f, true), new Point(x + 18, y + 16), White, Flags);
            TextRenderer.DrawText(g, "4 ÉTAPES", Mono(7.5f, true), new Rectangle(x, y + 17, cw - 18, 14), Faint, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            bool ready = _status.FullyReady;
            var steps = new (string t, string s, bool done)[]
            {
                ("Launcher prêt", "Fichiers vérifiés", true),
                ("Profil chargé", Who() + " connecté", true),
                ("Choisir une session", "Héberger ou rejoindre", ready),
                ("Inviter l'équipe", "Via l'overlay Steam", false),
            };
            int sy = y + 48;
            for (int i = 0; i < steps.Length; i++)
            {
                int ry = sy + i * 42;
                TextRenderer.DrawText(g, (i + 1).ToString("00"), Mono(8f, true), new Point(x + 18, ry + 4), Faint, Flags);
                var dot = new Rectangle(x + 44, ry, 18, 18);
                if (steps[i].done) { using (var b = new SolidBrush(Color.FromArgb(40, 201, 246, 54))) g.FillEllipse(b, dot); Icon(g, "check", dot.X + 9, dot.Y + 9, 11, Lime); }
                else { using (var p = new Pen(Color.FromArgb((int)(60 + 60 * pulse), 201, 246, 54), 1.6f)) g.DrawEllipse(p, dot); }
                TextRenderer.DrawText(g, steps[i].t, UI(9f, FontStyle.Bold), new Point(x + 72, ry - 1), steps[i].done ? White : Dim, Flags);
                TextRenderer.DrawText(g, steps[i].s, Mono(7.5f, false), new Point(x + 72, ry + 15), Faint, Flags);
            }
            using (var b = new SolidBrush(Line)) g.FillRectangle(b, x + 18, y + ch - 32, cw - 36, 1);
            using (var b = new SolidBrush(ready ? Lime : Color.FromArgb(224, 150, 60))) g.FillEllipse(b, x + 18, y + ch - 22, 7, 7);
            TextRenderer.DrawText(g, ready ? "TOUS LES SERVICES SONT OPÉRATIONNELS" : "CONFIGURATION REQUISE", Mono(7f, true), new Point(x + 32, y + ch - 24), ready ? Lime : Color.FromArgb(224, 150, 60), Flags);
        }

        void PaintPlay(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            bool on = _play.Enabled; var r = new Rectangle(0, 0, _play.Width, _play.Height);
            float pulse = 0.5f + 0.5f * (float)Math.Sin(_t * 3.0);
            if (on) { int gi = (int)(26 + 42 * (_playHover ? 1f : pulse)); using var gp = new GraphicsPath(); gp.AddPath(Round(new Rectangle(-6, -6, r.Width + 12, r.Height + 12), 10), false); using var pg = new PathGradientBrush(gp) { CenterColor = Color.FromArgb(gi, Lime), SurroundColors = new[] { Color.FromArgb(0, Lime) } }; g.FillPath(pg, gp); }
            var col = !on ? Color.FromArgb(40, 44, 40) : _playHover ? LimeHi : Lime;
            using (var b = new SolidBrush(col)) g.FillPath(b, Round(r, 6));
            var box = new Rectangle(14, r.Height / 2 - 15, 30, 30);
            using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillPath(b, Round(box, 5));
            if (PlayLoading) { var old = g.Transform.Clone(); g.TranslateTransform(box.X + 15, box.Y + 15); g.RotateTransform(_t * 360f); Icon(g, "settings", 0, 0, 18, Color.Black); g.Transform = old; }
            else using (var b = new SolidBrush(on ? Color.Black : Dim)) g.FillPolygon(b, new[] { new Point(box.X + 11, box.Y + 8), new Point(box.X + 11, box.Y + 22), new Point(box.X + 22, box.Y + 15) });
            var tc = on ? Color.Black : Dim;
            TextRenderer.DrawText(g, PlayLoading ? "CONNEXION…" : "JOUER MAINTENANT", UI(14f, FontStyle.Bold | FontStyle.Italic), new Point(58, 12), tc, Flags);
            TextRenderer.DrawText(g, !on ? "Configuration requise — voir la sidebar" : PlayLoading ? "Synchronisation du garage" : "Lancer Car Mechanic Simulator 2021", Mono(8f, true), new Point(60, 40), Color.FromArgb(on ? 150 : 120, 0, 0, 0), Flags);
            Icon(g, "chevron", r.Width - 26, r.Height / 2, 20, tc);
        }

        // ================= SIDEBAR =================
        void PaintSidebar(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int w = _side.Width;
            using (var b = new SolidBrush(Color.FromArgb(28, 255, 255, 255))) g.FillRectangle(b, w - 1, 0, 1, _side.Height);
            float pulse = 0.5f + 0.5f * (float)Math.Sin(_t * 2.2);

            var badge = new Rectangle(22, 24, 40, 40);
            using (var b = new SolidBrush(Lime)) g.FillPath(b, Round(badge, 9));
            DrawGear(g, badge.X + 20, badge.Y + 20, 13, 9, 9, Color.FromArgb(240, 10, 12, 10));
            var f = UI(12.5f, FontStyle.Bold);
            TextRenderer.DrawText(g, "CARMECA", f, new Point(72, 26), White, Flags);
            int cmw = TextRenderer.MeasureText(g, "CARMECA", f).Width;
            TextRenderer.DrawText(g, "SIM", f, new Point(72 + cmw - 8, 26), Lime, Flags);
            TextRenderer.DrawText(g, "CO-OP LAUNCHER", Mono(7.5f, true), new Point(73, 48), Dim, Flags);
            TextRenderer.DrawText(g, "v" + Constants.ModVersion, Mono(7.5f, true), new Rectangle(w - 70, 28, 46, 16), Faint, TextFormatFlags.Right | TextFormatFlags.NoPrefix);

            Section(g, "BIBLIOTHÈQUE", "01", 98);
            var row = new Rectangle(14, 118, w - 28, 50);
            using (var b = new SolidBrush(CardHi)) g.FillPath(b, Round(row, 10));
            using (var b = new SolidBrush(Lime)) g.FillRectangle(b, row.X, row.Y + 10, 3, row.Height - 20);
            var ico = new Rectangle(row.X + 14, row.Y + 11, 28, 28);
            using (var b = new SolidBrush(Color.FromArgb(40, 43, 40))) g.FillPath(b, Round(ico, 7));
            DrawGear(g, ico.X + 14, ico.Y + 14, 9, 6.5f, 9, Lime);
            TextRenderer.DrawText(g, "Car Mechanic Simulator 2021", UI(9f, FontStyle.Bold), new Point(row.X + 52, row.Y + 9), White, Flags);
            TextRenderer.DrawText(g, "CO-OP MULTIJOUEUR", Mono(7f, true), new Point(row.X + 52, row.Y + 28), Dim, Flags);
            using (var b = new SolidBrush(Color.FromArgb((int)(140 + 110 * pulse), Lime))) g.FillEllipse(b, row.Right - 22, row.Y + 21, 8, 8);

            Section(g, "ÉTAT DU SYSTÈME", "3/3", 196);
            StatusRow(g, 222, "BepInEx", "Runtime mod", _status.LoaderInstalled, 4);
            StatusRow(g, 258, "Mod Co-op", "v" + Constants.ModVersion, _status.ModInstalled, 12);
            StatusRow(g, 294, "Interop", "Steam relay", _status.InteropGenerated, 8);

            Section(g, "IDENTITÉ DU MÉCANO", null, 360);
            var rb = new Rectangle(22, 392, 34, 34);
            using (var b = new SolidBrush(Lime)) g.FillPath(b, Round(rb, 7));
            TextRenderer.DrawText(g, Initials(Who()), UI(10f, FontStyle.Bold), rb, Color.Black, Center);
            using (var b = new SolidBrush(Color.FromArgb(28, 31, 28))) g.FillPath(b, Round(new Rectangle(66, 392, w - 90, 34), 7));
            TextRenderer.DrawText(g, "UTILISÉ EN SESSION CO-OP", Mono(7.5f, true), new Point(22, 434), Faint, Flags);

            // update progress bar (under the buttons area)
            if (_progress >= 0)
            {
                int by = _update.Top - 14;
                var pr = new Rectangle(22, by, w - 44, 4);
                using (var b = new SolidBrush(Color.FromArgb(40, 43, 40))) g.FillPath(b, Round(pr, 2));
                using (var b = new SolidBrush(Lime)) g.FillPath(b, Round(new Rectangle(22, by, Math.Max(4, (w - 44) * _progress / 100), 4), 2));
            }
        }

        void StatusRow(Graphics g, int y, string label, string sub, bool ok, int ms)
        {
            int w = _side.Width;
            var dot = new Rectangle(22, y + 2, 16, 16);
            if (ok) { using (var b = new SolidBrush(Color.FromArgb(40, 201, 246, 54))) g.FillEllipse(b, dot); Icon(g, "check", dot.X + 8, dot.Y + 8, 11, Lime); }
            else { using (var p = new Pen(Danger, 1.8f)) g.DrawEllipse(p, dot); }
            TextRenderer.DrawText(g, label, Mono(9f, true), new Point(48, y - 1), ok ? White : Dim, Flags);
            TextRenderer.DrawText(g, sub, Mono(7f, false), new Point(48, y + 15), Faint, Flags);
            TextRenderer.DrawText(g, ok ? "EN LIGNE" : "HORS LIGNE", Mono(7.5f, true), new Rectangle(0, y - 1, w - 24, 14), ok ? Lime : Danger, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            if (ok) TextRenderer.DrawText(g, ms + " ms", Mono(7f, false), new Rectangle(0, y + 14, w - 24, 14), Faint, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
        }

        void Section(Graphics g, string t, string? count, int y)
        {
            TextRenderer.DrawText(g, t, Mono(8f, true), new Point(22, y), Dim, Flags);
            var sz = TextRenderer.MeasureText(g, t, Mono(8f, true));
            int lineX = 22 + sz.Width + 10, lineR = _side.Width - 24;
            if (count != null)
            {
                var cs = TextRenderer.MeasureText(g, count, Mono(8f, true));
                TextRenderer.DrawText(g, count, Mono(8f, true), new Rectangle(0, y, _side.Width - 24, 14), Faint, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
                lineR -= cs.Width + 10;
            }
            using var b = new SolidBrush(Line); g.FillRectangle(b, lineX, y + 7, Math.Max(0, lineR - lineX), 1);
        }

        void PaintModal(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int mw = 440, mh = 250, mx = (_modal.Width - mw) / 2, my = (_modal.Height - mh) / 2;
            var r = new Rectangle(mx, my, mw, mh);
            using (var b = new SolidBrush(Color.FromArgb(28, 31, 28))) g.FillPath(b, Round(r, 12));
            using (var p = new Pen(Color.FromArgb(50, 255, 255, 255))) g.DrawPath(p, Round(r, 12));
            Icon(g, "close", r.Right - 26, my + 26, 18, Dim);
            var ib = new Rectangle(mx + 30, my + 30, 44, 44);
            using (var b = new SolidBrush(Color.FromArgb(40, 224, 96, 84))) g.FillPath(b, Round(ib, 9));
            Icon(g, "trash", ib.X + 22, ib.Y + 22, 24, Danger);
            TextRenderer.DrawText(g, "ACTION SYSTÈME", Mono(8f, true), new Point(mx + 30, my + 84), Faint, Flags);
            TextRenderer.DrawText(g, "Désinstaller le mod Co-op ?", UI(16f, FontStyle.Bold), new Point(mx + 28, my + 100), White, Flags);
            TextRenderer.DrawText(g, "Le jeu principal et vos sauvegardes ne seront pas supprimés.", UI(9.5f, FontStyle.Regular), new Point(mx + 30, my + 138), Dim, Flags);
        }

        // ================= line icons (SVG-style, stroked) =================
        void Icon(Graphics g, string name, float cx, float cy, float size, Color col)
        {
            float s = size / 24f, ox = cx - size / 2f, oy = cy - size / 2f;
            PointF M(float x, float y) => new PointF(ox + x * s, oy + y * s);
            using var pen = new Pen(col, Math.Max(1.1f, 1.7f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            switch (name)
            {
                case "check": g.DrawLines(pen, new[] { M(5, 12), M(10, 17), M(19, 6) }); break;
                case "chevron": g.DrawLines(pen, new[] { M(9, 18), M(15, 12), M(9, 6) }); break;
                case "close": g.DrawLine(pen, M(18, 6), M(6, 18)); g.DrawLine(pen, M(6, 6), M(18, 18)); break;
                case "download": g.DrawLine(pen, M(12, 3), M(12, 15)); g.DrawLines(pen, new[] { M(7, 10), M(12, 15), M(17, 10) }); g.DrawLine(pen, M(5, 21), M(19, 21)); break;
                case "play": using (var b = new SolidBrush(col)) g.FillPolygon(b, new[] { M(8, 5.5f), M(8, 18.5f), M(18, 12) }); break;
                case "trash":
                    g.DrawLine(pen, M(4, 7), M(20, 7)); g.DrawLines(pen, new[] { M(9, 7), M(9, 4), M(15, 4), M(15, 7) });
                    g.DrawLines(pen, new[] { M(18, 7), M(17, 21), M(7, 21), M(6, 7) }); g.DrawLine(pen, M(10, 11), M(10, 17)); g.DrawLine(pen, M(14, 11), M(14, 17)); break;
                case "wrench": g.DrawLines(pen, new[] { M(14.7f, 6.3f), M(9.6f, 6f), M(7.3f, 3.7f), M(4f, 17f), M(7f, 20f), M(17.4f, 9f), M(14.7f, 6.3f) }); break;
                case "users":
                    g.DrawEllipse(pen, M(6, 2).X, M(6, 2).Y, 6 * s, 6 * s);
                    g.DrawLines(pen, new[] { M(2, 20), M(2, 18), M(4, 14), M(14, 14), M(16, 18), M(16, 20) });
                    g.DrawLines(pen, new[] { M(18, 14.5f), M(21, 17), M(21, 20) }); break;
                case "message":
                    using (var p2 = new GraphicsPath()) { p2.AddPath(Round(new Rectangle((int)M(3, 3).X, (int)M(3, 3).Y, (int)(17 * s), (int)(13 * s)), (int)(4 * s)), false); g.DrawPath(pen, p2); }
                    g.DrawLines(pen, new[] { M(7, 16), M(5, 20), M(10, 16) }); break;
                case "server":
                    using (var p2 = new GraphicsPath()) { p2.AddPath(Round(new Rectangle((int)M(3, 4).X, (int)M(3, 4).Y, (int)(18 * s), (int)(6 * s)), (int)(1.5f * s)), false); g.DrawPath(pen, p2); }
                    using (var p2 = new GraphicsPath()) { p2.AddPath(Round(new Rectangle((int)M(3, 14).X, (int)M(3, 14).Y, (int)(18 * s), (int)(6 * s)), (int)(1.5f * s)), false); g.DrawPath(pen, p2); }
                    g.DrawLine(pen, M(7, 7), M(7.2f, 7)); g.DrawLine(pen, M(7, 17), M(7.2f, 17)); break;
                case "settings":
                    g.DrawEllipse(pen, M(9, 9).X, M(9, 9).Y, 6 * s, 6 * s);
                    for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(pen, new PointF(cx + (float)Math.Cos(a) * 7 * s, cy + (float)Math.Sin(a) * 7 * s), new PointF(cx + (float)Math.Cos(a) * 11 * s, cy + (float)Math.Sin(a) * 11 * s)); }
                    break;
                default: g.DrawEllipse(pen, M(8, 8).X, M(8, 8).Y, 8 * s, 8 * s); break;
            }
        }

        // ================= helpers =================
        static readonly TextFormatFlags Flags = TextFormatFlags.NoPrefix;
        static readonly TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        static readonly TextFormatFlags Mid = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        static Font Mono(float sz, bool bold) => new Font("Consolas", sz, bold ? FontStyle.Bold : FontStyle.Regular);
        static Font UI(float sz, FontStyle st) => new Font("Segoe UI", sz, st);
        static PointF Pt(float cx, float cy, float r, double a) => new PointF(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r));

        static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = Math.Max(2, rad * 2);
            if (r.Width < d || r.Height < d) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        void RadialGlow(Graphics g, PointF c, float radius, Color col)
        { using var path = new GraphicsPath(); path.AddEllipse(c.X - radius, c.Y - radius, radius * 2, radius * 2); using var pg = new PathGradientBrush(path) { CenterPoint = c, CenterColor = col, SurroundColors = new[] { Color.FromArgb(0, col) } }; g.FillPath(pg, path); }
        static void DrawGear(Graphics g, float cx, float cy, float rOut, float rIn, int teeth, Color col)
        {
            var pts = new System.Collections.Generic.List<PointF>(); double step = Math.PI * 2 / teeth;
            for (int i = 0; i < teeth; i++) { double a = i * step; pts.Add(Pt(cx, cy, rIn, a - step * 0.26)); pts.Add(Pt(cx, cy, rOut, a - step * 0.17)); pts.Add(Pt(cx, cy, rOut, a + step * 0.17)); pts.Add(Pt(cx, cy, rIn, a + step * 0.26)); }
            using var b = new SolidBrush(col); g.FillPolygon(b, pts.ToArray());
            float hr = rIn * 0.5f; using var hub = new SolidBrush(Color.FromArgb(Math.Max(col.A - 2, 8), 10, 10, 9)); g.FillEllipse(hub, cx - hr, cy - hr, hr * 2, hr * 2);
        }
        void Shadowed(Graphics g, string s, Font f, Point p, Color c)
        { TextRenderer.DrawText(g, s, f, new Point(p.X + 2, p.Y + 3), Color.FromArgb(180, 0, 0, 0), Flags); TextRenderer.DrawText(g, s, f, p, c, Flags); }
        void FlatBtn(Button b, string text, bool primary)
        {
            b.Text = text; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(52, 56, 52); b.ForeColor = primary ? Color.Black : White;
            b.BackColor = primary ? Lime : Card; b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false; b.TextAlign = ContentAlignment.MiddleCenter;
            b.FlatAppearance.MouseOverBackColor = primary ? LimeHi : CardHi; b.FlatAppearance.MouseDownBackColor = primary ? LimeHi : Color.FromArgb(40, 43, 40);
        }
        string Who() => string.IsNullOrWhiteSpace(_name.Text) ? Environment.UserName : _name.Text.Trim();
        static string Initials(string s) { s = (s ?? "").Trim(); return s.Length == 0 ? "MC" : (s.Length >= 2 ? s.Substring(0, 2) : s).ToUpperInvariant(); }
        void SetActivity(string s) { _activity = s; Toast(s); _hero.Invalidate(); }
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
            _updating = false; _progress = 100;
            Toast(ok ? $"✓ mis à jour en v{info.Tag}" : "échec de la mise à jour");
            RefreshStatus();
            await System.Threading.Tasks.Task.Delay(1200); _progress = -1; _side.Invalidate();
        }

        void RefreshStatus()
        {
            _gameDir = GameLocator.Find();
            if (_gameDir == null) { _status = new InstallStatus(); _play.Enabled = _uninstall.Enabled = false; _setup.Enabled = true; _activity = "JEU INTROUVABLE VIA STEAM"; }
            else { _status = new ModInstaller(_gameDir).GetStatus(); _play.Enabled = _status.FullyReady; _setup.Enabled = true; _uninstall.Enabled = _status.LoaderInstalled || _status.ModInstalled; }
            _side.Invalidate(); _play.Invalidate(); _hero.Invalidate();
        }

        void DoSetup()
        {
            if (_gameDir == null) return;
            var payload = ModInstaller.FindPayloadRoot();
            if (payload == null) { Toast("payload d'installation introuvable"); return; }
            var inst = new ModInstaller(_gameDir);
            Toast("installation de BepInEx…");
            if (!inst.EnsureLoader(payload, Toast)) { Toast("échec BepInEx"); return; }
            InteropPatcher.EnsurePatched(_gameDir, Toast); inst.InstallMod(payload, Toast); inst.EnsureAppId(Toast);
            Toast("installé · 1er lancement = génération interop"); RefreshStatus();
        }

        void DoUninstall()
        {
            if (_gameDir == null) return;
            new ModInstaller(_gameDir).Uninstall(false, Toast); RefreshStatus(); Toast("mod co-op désinstallé");
        }

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
