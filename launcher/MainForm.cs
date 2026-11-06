using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using CarMecaSim.Shared;

namespace CarMecaSim.Launcher
{
    /// <summary>
    /// Rockstar-launcher-inspired, CMS-themed launcher: dark sidebar + painted hero.
    /// It only installs/verifies and launches — hosting/joining & chat happen in-game (F6).
    /// </summary>
    public sealed class MainForm : Form
    {
        // palette
        static readonly Color Black   = Color.FromArgb(16, 16, 18);
        static readonly Color Side    = Color.FromArgb(20, 20, 23);
        static readonly Color Card    = Color.FromArgb(30, 30, 34);
        static readonly Color Orange  = Color.FromArgb(245, 148, 30);
        static readonly Color OrangeHi = Color.FromArgb(255, 172, 58);
        static readonly Color White   = Color.FromArgb(238, 238, 241);
        static readonly Color Dim     = Color.FromArgb(150, 150, 157);
        static readonly Color Green   = Color.FromArgb(120, 205, 110);
        static readonly Color Red     = Color.FromArgb(220, 100, 88);

        sealed class BufferedPanel : Panel { public BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; } }

        string? _gameDir;
        InstallStatus _status = new();
        string _activity = "";
        readonly BufferedPanel _side = new();
        readonly BufferedPanel _hero = new();
        readonly TextBox _name = new();
        readonly Button _play = new();
        readonly Button _setup = new();
        readonly Button _uninstall = new();

        public MainForm()
        {
            Text = "CarMecaSim — Co-op";
            ClientSize = new Size(1000, 600);
            MinimumSize = new Size(880, 540);
            BackColor = Black;
            Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            _hero.Dock = DockStyle.Fill; _hero.BackColor = Black; _hero.Paint += PaintHero;
            Controls.Add(_hero);

            _side.Dock = DockStyle.Left; _side.Width = 248; _side.BackColor = Side; _side.Paint += PaintSidebar;
            Controls.Add(_side);

            // pseudo field (sidebar)
            _name.BorderStyle = BorderStyle.FixedSingle; _name.BackColor = Color.FromArgb(14, 14, 16);
            _name.ForeColor = White; _name.Font = new Font("Segoe UI", 10f); _name.Text = Environment.UserName;
            _name.SetBounds(20, 322, 208, 26);
            _side.Controls.Add(_name);
            _name.TextChanged += (_, _) => _hero.Invalidate();

            FlatBtn(_setup, "RÉPARER / INSTALLER", false); _setup.SetBounds(20, 476, 208, 34);
            FlatBtn(_uninstall, "DÉSINSTALLER", false); _uninstall.SetBounds(20, 514, 208, 30);
            _side.Controls.Add(_setup); _side.Controls.Add(_uninstall);

            // big play button (hero)
            FlatBtn(_play, "▶   JOUER", true); _play.Size = new Size(260, 56); _play.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            _hero.Controls.Add(_play);
            _hero.Resize += (_, _) => LayoutHero();

            _setup.Click += (_, _) => DoSetup();
            _uninstall.Click += (_, _) => DoUninstall();
            _play.Click += (_, _) => DoPlay();
            Load += (_, _) => { RefreshStatus(); LayoutHero(); };
        }

        void LayoutHero()
        {
            _play.Location = new Point(56, _hero.Height - 210);
            RoundRegion(_play, 8);
        }

        // ---------------- painting ----------------
        void PaintHero(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int w = _hero.Width, h = _hero.Height;

            // warm diagonal gradient (charcoal -> CMS orange), Rockstar-style
            using (var br = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                       Color.FromArgb(28, 24, 22), Color.FromArgb(206, 116, 32), 32f))
                g.FillRectangle(br, 0, 0, w, h);
            // darken top-left for title legibility
            using (var vign = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                       Color.FromArgb(175, 8, 8, 10), Color.FromArgb(0, 0, 0, 0), 35f))
                g.FillRectangle(vign, 0, 0, w, h);

            // subtle clean cog motif, bottom-right (mostly off-canvas)
            DrawGear(g, w - 70, h - 50, 260, 212, 20, Color.FromArgb(18, 255, 255, 255));
            DrawGear(g, w - 330, h + 60, 140, 114, 16, Color.FromArgb(13, 255, 255, 255));

            // account chip (top-right)
            string who = string.IsNullOrWhiteSpace(_name.Text) ? Environment.UserName : _name.Text.Trim();
            var chip = new Rectangle(w - 190, 22, 168, 34);
            using (var b = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) FillRound(g, b, chip, 17);
            using (var b = new SolidBrush(Green)) g.FillEllipse(b, chip.X + 12, chip.Y + 12, 10, 10);
            TextRenderer.DrawText(g, who, new Font("Segoe UI", 9.5f, FontStyle.Bold), new Rectangle(chip.X + 30, chip.Y, chip.Width - 36, chip.Height), White, TextFormatFlags.VerticalCenter);

            // title block
            using var big = new Font("Segoe UI Black", 34f, FontStyle.Bold);
            using var sub = new Font("Segoe UI", 12f, FontStyle.Bold);
            int ty = h - 330;
            Shadowed(g, "CAR MECHANIC SIMULATOR", big, new Point(54, ty), White);
            TextRenderer.DrawText(g, "CO·OP   —   GARAGE PARTAGÉ MULTIJOUEUR", sub, new Point(58, ty + 54), Orange, TextFormatFlags.NoPrefix);

            // hint under the play button
            TextRenderer.DrawText(g, "Héberger · rejoindre · chat  =  EN JEU  (touche F6)",
                new Font("Segoe UI", 9.5f, FontStyle.Italic), new Point(58, _hero.Height - 150), Dim, TextFormatFlags.NoPrefix);

            // info card (right)
            var ic = new Rectangle(w - 320, h - 250, 280, 150);
            using (var b = new SolidBrush(Color.FromArgb(220, Card.R, Card.G, Card.B))) FillRound(g, b, ic, 10);
            using (var b = new SolidBrush(Orange)) g.FillRectangle(b, ic.X, ic.Y, 4, ic.Height);
            TextRenderer.DrawText(g, "COMMENT JOUER", new Font("Segoe UI", 10f, FontStyle.Bold), new Point(ic.X + 18, ic.Y + 14), Orange);
            var steps = new[] { "1.  Clique  ▶ JOUER", "2.  Charge / continue une partie", "3.  F6  →  Héberger ou Rejoindre", "4.  Invite via l'overlay Steam" };
            for (int i = 0; i < steps.Length; i++)
                TextRenderer.DrawText(g, steps[i], new Font("Segoe UI", 9.5f), new Point(ic.X + 18, ic.Y + 44 + i * 24), White);

            // activity line (bottom)
            if (!string.IsNullOrEmpty(_activity))
                TextRenderer.DrawText(g, _activity, new Font("Consolas", 9f), new Point(58, _hero.Height - 36), Dim);
        }

        void PaintSidebar(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // logo badge
            var badge = new Rectangle(20, 22, 44, 44);
            using (var b = new SolidBrush(Orange)) FillRound(g, b, badge, 10);
            DrawGear(g, badge.X + 22, badge.Y + 22, 15, 11, 10, Color.FromArgb(235, 16, 16, 18));
            TextRenderer.DrawText(g, "CarMecaSim", new Font("Segoe UI", 13f, FontStyle.Bold), new Point(74, 26), White);
            TextRenderer.DrawText(g, "CO-OP LAUNCHER", new Font("Segoe UI", 7.5f, FontStyle.Bold), new Point(75, 48), Orange);

            SectionLabel(g, "DISPONIBLE", 96);
            // game row (selected)
            var row = new Rectangle(12, 116, 224, 46);
            using (var b = new SolidBrush(Color.FromArgb(32, 32, 36))) FillRound(g, b, row, 8);
            using (var b = new SolidBrush(Orange)) g.FillRectangle(b, row.X, row.Y + 8, 3, row.Height - 16);
            var ico = new Rectangle(row.X + 12, row.Y + 9, 28, 28);
            using (var b = new SolidBrush(Color.FromArgb(48, 48, 54))) FillRound(g, b, ico, 6);
            DrawGear(g, ico.X + 14, ico.Y + 14, 9, 6.5f, 9, Orange);
            TextRenderer.DrawText(g, "Car Mechanic Sim. 2021", new Font("Segoe UI", 9.5f, FontStyle.Bold), new Point(row.X + 50, row.Y + 7), White);
            TextRenderer.DrawText(g, "Co-op multijoueur", new Font("Segoe UI", 8f), new Point(row.X + 50, row.Y + 25), Dim);

            SectionLabel(g, "ÉTAT", 188);
            StatusRow(g, 208, "BepInEx", _status.LoaderInstalled);
            StatusRow(g, 234, "Mod co-op", _status.ModInstalled);
            StatusRow(g, 260, "Interop", _status.InteropGenerated);

            SectionLabel(g, "PSEUDO", 300);

            // bottom
            TextRenderer.DrawText(g, "v" + Constants.ModVersion + "   ·   réversible", new Font("Segoe UI", 8f), new Point(20, _side.Height - 24), Dim);
        }

        void SectionLabel(Graphics g, string t, int y) =>
            TextRenderer.DrawText(g, t, new Font("Segoe UI", 8f, FontStyle.Bold), new Point(20, y), Dim);

        void StatusRow(Graphics g, int y, string label, bool ok)
        {
            using (var b = new SolidBrush(ok ? Green : Red)) g.FillEllipse(b, 22, y + 5, 9, 9);
            TextRenderer.DrawText(g, label, new Font("Segoe UI", 9.5f), new Point(40, y), ok ? White : Dim);
            TextRenderer.DrawText(g, ok ? "OK" : "—", new Font("Segoe UI", 9f, FontStyle.Bold), new Rectangle(150, y, 78, 18), ok ? Green : Dim, TextFormatFlags.Right);
        }

        // ---------------- helpers ----------------
        static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        static void FillRound(Graphics g, Brush b, Rectangle r, int rad) { using var p = Round(r, rad); g.FillPath(b, p); }
        static void RoundRegion(Control c, int rad) { c.Region = new Region(Round(new Rectangle(0, 0, c.Width, c.Height), rad)); }

        static PointF P(float cx, float cy, float r, double a) =>
            new PointF(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r));

        /// <summary>A clean cog: flat-topped teeth + a hub ring.</summary>
        static void DrawGear(Graphics g, float cx, float cy, float rOut, float rIn, int teeth, Color col)
        {
            var pts = new System.Collections.Generic.List<PointF>();
            double step = Math.PI * 2 / teeth;
            for (int i = 0; i < teeth; i++)
            {
                double a = i * step;
                pts.Add(P(cx, cy, rIn, a - step * 0.26));
                pts.Add(P(cx, cy, rOut, a - step * 0.17));
                pts.Add(P(cx, cy, rOut, a + step * 0.17));
                pts.Add(P(cx, cy, rIn, a + step * 0.26));
            }
            using var b = new SolidBrush(col);
            g.FillPolygon(b, pts.ToArray());
            // hub ring (slightly darker cut-out)
            float hr = rIn * 0.52f;
            using var hub = new SolidBrush(Color.FromArgb(Math.Max(col.A - 2, 8), 10, 8, 6));
            g.FillEllipse(hub, cx - hr, cy - hr, hr * 2, hr * 2);
            using var body = new SolidBrush(col);
            g.FillEllipse(body, cx - rIn, cy - rIn, rIn * 2, rIn * 2);
        }

        void Shadowed(Graphics g, string s, Font f, Point p, Color c)
        {
            TextRenderer.DrawText(g, s, f, new Point(p.X + 2, p.Y + 2), Color.FromArgb(160, 0, 0, 0), TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, s, f, p, c, TextFormatFlags.NoPrefix);
        }

        void FlatBtn(Button b, string text, bool primary)
        {
            b.Text = text; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 78);
            b.ForeColor = primary ? Color.Black : White;
            b.BackColor = primary ? Orange : Color.FromArgb(34, 34, 38);
            b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false;
            b.FlatAppearance.MouseOverBackColor = primary ? OrangeHi : Color.FromArgb(48, 48, 54);
            b.FlatAppearance.MouseDownBackColor = primary ? OrangeHi : Color.FromArgb(58, 58, 66);
        }

        void SetActivity(string s) { _activity = s; _hero.Invalidate(); }

        // ---------------- logic ----------------
        void RefreshStatus()
        {
            _gameDir = GameLocator.Find();
            if (_gameDir == null)
            {
                _status = new InstallStatus();
                _play.Enabled = _uninstall.Enabled = false; _setup.Enabled = true;
                SetActivity("Jeu introuvable via Steam.");
            }
            else
            {
                _status = new ModInstaller(_gameDir).GetStatus();
                _play.Enabled = _status.FullyReady;
                _setup.Enabled = true;
                _uninstall.Enabled = _status.LoaderInstalled || _status.ModInstalled;
                SetActivity(_status.FullyReady ? "Prêt à jouer." : "Clique RÉPARER / INSTALLER.");
            }
            _side.Invalidate();
        }

        void DoSetup()
        {
            if (_gameDir == null) return;
            var payload = ModInstaller.FindPayloadRoot();
            if (payload == null) { SetActivity("Payload d'installation introuvable."); return; }
            var inst = new ModInstaller(_gameDir);
            SetActivity("Installation de BepInEx…");
            if (!inst.EnsureLoader(payload, SetActivity)) { SetActivity("Échec BepInEx."); return; }
            InteropPatcher.EnsurePatched(_gameDir, SetActivity);
            inst.InstallMod(payload, SetActivity);
            inst.EnsureAppId(SetActivity);
            SetActivity("Terminé. 1er lancement = génération interop (quelques min).");
            RefreshStatus();
        }

        void DoUninstall()
        {
            if (_gameDir == null) return;
            var all = MessageBox.Show("Supprimer aussi BepInEx (retour vanilla) ?", "Désinstaller",
                MessageBoxButtons.YesNo) == DialogResult.Yes;
            new ModInstaller(_gameDir).Uninstall(all, SetActivity);
            RefreshStatus();
        }

        void DoPlay()
        {
            if (_gameDir == null) return;
            var cfg = new MpConfig
            {
                Role = MpRole.Menu,  // host/join is in-game
                DisplayName = string.IsNullOrWhiteSpace(_name.Text) ? Environment.UserName : _name.Text.Trim(),
                AutoStart = true,
            };
            new GameRunner(_gameDir).WriteConfig(cfg);
            SetActivity($"Lancement du jeu (pseudo : {cfg.DisplayName})…");
            new GameRunner(_gameDir).Launch();
        }
    }
}
