using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using CarMecaSim.Shared;

namespace CarMecaSim.Launcher
{
    /// <summary>
    /// Cinematic, angular game-launcher UI (industrial HUD vibe). Installs/verifies + launches.
    /// Hosting/joining & chat happen in-game (F6). No host/join choice here by design.
    /// </summary>
    public sealed class MainForm : Form
    {
        static readonly Color Black  = Color.FromArgb(13, 14, 16);
        static readonly Color Steel  = Color.FromArgb(24, 26, 30);
        static readonly Color Steel2 = Color.FromArgb(32, 34, 39);
        static readonly Color Amber  = Color.FromArgb(255, 176, 40);
        static readonly Color AmberHi = Color.FromArgb(255, 198, 86);
        static readonly Color White  = Color.FromArgb(240, 241, 244);
        static readonly Color Dim    = Color.FromArgb(138, 142, 150);
        static readonly Color Green  = Color.FromArgb(96, 214, 120);
        static readonly Color Red    = Color.FromArgb(224, 96, 84);

        sealed class BPanel : Panel { public BPanel() { DoubleBuffered = true; ResizeRedraw = true; } }

        string? _gameDir;
        InstallStatus _status = new();
        string _activity = "";
        readonly BPanel _side = new();
        readonly BPanel _hero = new();
        readonly TextBox _name = new();
        readonly Button _play = new();
        readonly Button _setup = new();
        readonly Button _uninstall = new();

        public MainForm()
        {
            Text = "CarMecaSim — Co-op";
            ClientSize = new Size(1040, 620);
            MinimumSize = new Size(900, 560);
            BackColor = Black;
            Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            _hero.Dock = DockStyle.Fill; _hero.BackColor = Black; _hero.Paint += PaintHero;
            Controls.Add(_hero);

            _side.Dock = DockStyle.Left; _side.Width = 256; _side.BackColor = Steel; _side.Paint += PaintSidebar;
            Controls.Add(_side);

            _name.BorderStyle = BorderStyle.FixedSingle; _name.BackColor = Color.FromArgb(10, 11, 13);
            _name.ForeColor = White; _name.Font = new Font("Consolas", 11f);
            _name.SetBounds(22, 356, 212, 28); _name.Text = Environment.UserName;
            _side.Controls.Add(_name);
            _name.TextChanged += (_, _) => _hero.Invalidate();

            AngularBtn(_setup, "RÉPARER / INSTALLER", false); _setup.SetBounds(22, _side.Height, 212, 36);
            AngularBtn(_uninstall, "DÉSINSTALLER", false); _uninstall.SetBounds(22, _side.Height, 212, 30);
            _side.Controls.Add(_setup); _side.Controls.Add(_uninstall);
            _side.Resize += (_, _) => { _setup.Top = _side.Height - 86; _uninstall.Top = _side.Height - 46; };

            AngularBtn(_play, "▶   JOUER", true); _play.Size = new Size(280, 60); _play.Font = new Font("Segoe UI", 15.5f, FontStyle.Bold | FontStyle.Italic);
            _hero.Controls.Add(_play);
            _hero.Resize += (_, _) => LayoutHero();

            _setup.Click += (_, _) => DoSetup();
            _uninstall.Click += (_, _) => DoUninstall();
            _play.Click += (_, _) => DoPlay();
            Load += (_, _) => { RefreshStatus(); LayoutHero(); _setup.Top = _side.Height - 86; _uninstall.Top = _side.Height - 46; };
        }

        void LayoutHero()
        {
            _play.Location = new Point(60, _hero.Height - 214);
            _play.Region = new Region(Parallelogram(new Rectangle(0, 0, _play.Width, _play.Height), 16));
        }

        // ---------------- hero ----------------
        void PaintHero(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int w = _hero.Width, h = _hero.Height;

            // deep cinematic base: near-black with a warm pocket bottom-right
            using (var br = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(15, 16, 19), Color.FromArgb(44, 30, 16), 35f))
                g.FillRectangle(br, 0, 0, w, h);
            // warm key light, lower-right
            RadialGlow(g, new PointF(w * 0.78f, h * 0.92f), Math.Max(w, h) * 0.7f, Color.FromArgb(70, 255, 150, 40));
            // subtle cog, lower-right
            DrawGear(g, w - 60, h - 40, 260, 214, 22, Color.FromArgb(14, 255, 255, 255));
            // top vignette for the title
            using (var v = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(150, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 60f))
                g.FillRectangle(v, 0, 0, w, h);

            // HUD corner brackets
            Bracket(g, 24, 20, 26, 3, Amber, true, true);
            Bracket(g, w - 24, h - 20, 26, 3, Amber, false, false);

            // account chip (top-right)
            string who = string.IsNullOrWhiteSpace(_name.Text) ? Environment.UserName : _name.Text.Trim();
            var chip = new Rectangle(w - 196, 24, 172, 34);
            using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillPath(b, Parallelogram(chip, 12));
            using (var b = new SolidBrush(Green)) g.FillEllipse(b, chip.X + 16, chip.Y + 12, 10, 10);
            TextRenderer.DrawText(g, who.ToUpperInvariant(), new Font("Consolas", 9.5f, FontStyle.Bold), new Rectangle(chip.X + 34, chip.Y, chip.Width - 40, chip.Height), White, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            // title block
            int ty = h - 342;
            TextRenderer.DrawText(g, "MULTIJOUEUR  ·  GARAGE PARTAGÉ", new Font("Consolas", 10f, FontStyle.Bold), new Point(62, ty), Amber, TextFormatFlags.NoPrefix);
            using var big = new Font("Segoe UI Black", 38f, FontStyle.Bold | FontStyle.Italic);
            Shadowed(g, "CAR MECHANIC", big, new Point(56, ty + 20), White);
            Shadowed(g, "SIMULATOR", big, new Point(56, ty + 68), White);
            // amber underline
            using (var b = new SolidBrush(Amber)) g.FillRectangle(b, 62, ty + 122, 120, 4);
            HazardStripe(g, new Rectangle(188, ty + 122, 150, 4));

            // play hint + info card
            TextRenderer.DrawText(g, "HÉBERGER · REJOINDRE · CHAT  =  EN JEU   [ F6 ]", new Font("Consolas", 9.5f), new Point(62, _hero.Height - 142), Dim, TextFormatFlags.NoPrefix);

            var ic = new Rectangle(w - 326, h - 252, 286, 152);
            using (var b = new SolidBrush(Color.FromArgb(225, Steel2.R, Steel2.G, Steel2.B))) g.FillPath(b, Parallelogram(ic, 16));
            using (var b = new SolidBrush(Amber)) g.FillRectangle(b, ic.X + 14, ic.Y, 3, 22);
            TextRenderer.DrawText(g, "PROTOCOLE", new Font("Consolas", 10f, FontStyle.Bold), new Point(ic.X + 26, ic.Y + 14), Amber, TextFormatFlags.NoPrefix);
            var steps = new[] { "01   ▶ JOUER", "02   Charger / continuer une partie", "03   F6  →  Héberger / Rejoindre", "04   Inviter via l'overlay Steam" };
            for (int i = 0; i < steps.Length; i++)
                TextRenderer.DrawText(g, steps[i], new Font("Consolas", 9.5f), new Point(ic.X + 26, ic.Y + 44 + i * 23), White, TextFormatFlags.NoPrefix);

            // bottom HUD status strip
            using (var b = new SolidBrush(Color.FromArgb(160, 0, 0, 0))) g.FillRectangle(b, 0, h - 30, w, 30);
            using (var b = new SolidBrush(Amber)) g.FillRectangle(b, 0, h - 30, 3, 30);
            if (!string.IsNullOrEmpty(_activity))
                TextRenderer.DrawText(g, "› " + _activity, new Font("Consolas", 9f), new Point(14, h - 29), Dim, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
        }

        // ---------------- sidebar ----------------
        void PaintSidebar(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(Amber)) g.FillRectangle(b, _side.Width - 2, 0, 2, _side.Height); // edge line

            var badge = new Rectangle(22, 24, 46, 46);
            using (var b = new SolidBrush(Amber)) g.FillPath(b, Parallelogram(badge, 10));
            DrawGear(g, badge.X + 23, badge.Y + 23, 15, 11, 10, Color.FromArgb(240, 14, 14, 16));
            TextRenderer.DrawText(g, "CARMECASIM", new Font("Segoe UI", 13f, FontStyle.Bold), new Point(78, 28), White, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "CO-OP  ·  v" + Constants.ModVersion, new Font("Consolas", 8f, FontStyle.Bold), new Point(79, 50), Amber, TextFormatFlags.NoPrefix);

            Section(g, "BIBLIOTHÈQUE", 100);
            var row = new Rectangle(10, 120, 236, 48);
            using (var b = new SolidBrush(Steel2)) g.FillPath(b, Parallelogram(row, 12));
            using (var b = new SolidBrush(Amber)) g.FillRectangle(b, row.X, row.Y + 9, 3, row.Height - 18);
            var ico = new Rectangle(row.X + 14, row.Y + 10, 28, 28);
            using (var b = new SolidBrush(Color.FromArgb(44, 46, 52))) g.FillPath(b, Parallelogram(ico, 6));
            DrawGear(g, ico.X + 14, ico.Y + 14, 9, 6.5f, 9, Amber);
            TextRenderer.DrawText(g, "Car Mechanic Sim. 2021", new Font("Segoe UI", 9.5f, FontStyle.Bold), new Point(row.X + 52, row.Y + 8), White, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "CO-OP MULTIJOUEUR", new Font("Consolas", 7.5f, FontStyle.Bold), new Point(row.X + 52, row.Y + 27), Dim, TextFormatFlags.NoPrefix);

            Section(g, "SYSTÈME", 192);
            StatusRow(g, 214, "BEPINEX", _status.LoaderInstalled);
            StatusRow(g, 240, "MOD CO-OP", _status.ModInstalled);
            StatusRow(g, 266, "INTEROP", _status.InteropGenerated);

            Section(g, "PSEUDO", 334);
        }

        void Section(Graphics g, string t, int y)
        {
            TextRenderer.DrawText(g, t, new Font("Consolas", 8.5f, FontStyle.Bold), new Point(22, y), Dim, TextFormatFlags.NoPrefix);
            using var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
            g.FillRectangle(b, 22, y + 16, 212, 1);
        }

        void StatusRow(Graphics g, int y, string label, bool ok)
        {
            using (var b = new SolidBrush(ok ? Green : Red)) g.FillRectangle(b, 24, y + 5, 8, 8);
            TextRenderer.DrawText(g, label, new Font("Consolas", 9.5f, FontStyle.Bold), new Point(42, y), ok ? White : Dim, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, ok ? "ONLINE" : "OFFLINE", new Font("Consolas", 8.5f, FontStyle.Bold), new Rectangle(140, y, 94, 18), ok ? Green : Dim, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
        }

        // ---------------- gdi helpers ----------------
        static PointF P(float cx, float cy, float r, double a) => new PointF(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r));

        static GraphicsPath Parallelogram(Rectangle r, int cut)
        {
            var p = new GraphicsPath();
            p.AddPolygon(new[] {
                new Point(r.X + cut, r.Y), new Point(r.Right, r.Y),
                new Point(r.Right - cut, r.Bottom), new Point(r.X, r.Bottom) });
            return p;
        }

        void RadialGlow(Graphics g, PointF c, float radius, Color col)
        {
            using var path = new GraphicsPath();
            path.AddEllipse(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            using var pg = new PathGradientBrush(path)
            { CenterPoint = c, CenterColor = col, SurroundColors = new[] { Color.FromArgb(0, col) } };
            g.FillPath(pg, path);
        }

        void Bracket(Graphics g, int x, int y, int len, int th, Color col, bool right, bool down)
        {
            using var pen = new Pen(col, th);
            int dx = right ? len : -len, dy = down ? len : -len;
            g.DrawLine(pen, x, y, x + dx, y);
            g.DrawLine(pen, x, y, x, y + dy);
        }

        void HazardStripe(Graphics g, Rectangle r)
        {
            var old = g.Clip; g.SetClip(r);
            using var b1 = new SolidBrush(Amber); using var b2 = new SolidBrush(Color.FromArgb(20, 20, 22));
            for (int x = r.X - r.Height; x < r.Right; x += 10)
                g.FillPolygon((x / 10 % 2 == 0) ? b1 : b2, new[] {
                    new Point(x, r.Bottom), new Point(x + r.Height, r.Y), new Point(x + r.Height + 5, r.Y), new Point(x + 5, r.Bottom) });
            g.Clip = old;
        }

        static void DrawGear(Graphics g, float cx, float cy, float rOut, float rIn, int teeth, Color col)
        {
            var pts = new System.Collections.Generic.List<PointF>();
            double step = Math.PI * 2 / teeth;
            for (int i = 0; i < teeth; i++)
            {
                double a = i * step;
                pts.Add(P(cx, cy, rIn, a - step * 0.26)); pts.Add(P(cx, cy, rOut, a - step * 0.17));
                pts.Add(P(cx, cy, rOut, a + step * 0.17)); pts.Add(P(cx, cy, rIn, a + step * 0.26));
            }
            using var b = new SolidBrush(col); g.FillPolygon(b, pts.ToArray());
            float hr = rIn * 0.52f;
            using var hub = new SolidBrush(Color.FromArgb(System.Math.Max(col.A - 2, 8), 10, 8, 6));
            g.FillEllipse(hub, cx - hr, cy - hr, hr * 2, hr * 2);
            using var body = new SolidBrush(col); g.FillEllipse(body, cx - rIn, cy - rIn, rIn * 2, rIn * 2);
        }

        void Shadowed(Graphics g, string s, Font f, Point p, Color c)
        {
            TextRenderer.DrawText(g, s, f, new Point(p.X + 2, p.Y + 3), Color.FromArgb(170, 0, 0, 0), TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, s, f, p, c, TextFormatFlags.NoPrefix);
        }

        void AngularBtn(Button b, string text, bool primary)
        {
            b.Text = text; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(70, 72, 80);
            b.ForeColor = primary ? Color.Black : White;
            b.BackColor = primary ? Amber : Steel2;
            b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false;
            b.FlatAppearance.MouseOverBackColor = primary ? AmberHi : Color.FromArgb(44, 46, 52);
            b.FlatAppearance.MouseDownBackColor = primary ? AmberHi : Color.FromArgb(54, 56, 64);
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
                SetActivity("JEU INTROUVABLE VIA STEAM");
            }
            else
            {
                _status = new ModInstaller(_gameDir).GetStatus();
                _play.Enabled = _status.FullyReady; _setup.Enabled = true;
                _uninstall.Enabled = _status.LoaderInstalled || _status.ModInstalled;
                SetActivity(_status.FullyReady ? "SYSTÈME PRÊT  ·  appuie sur JOUER" : "CONFIG REQUISE  ·  clique RÉPARER / INSTALLER");
            }
            _side.Invalidate();
        }

        void DoSetup()
        {
            if (_gameDir == null) return;
            var payload = ModInstaller.FindPayloadRoot();
            if (payload == null) { SetActivity("payload d'installation introuvable"); return; }
            var inst = new ModInstaller(_gameDir);
            SetActivity("installation de BepInEx…");
            if (!inst.EnsureLoader(payload, SetActivity)) { SetActivity("échec BepInEx"); return; }
            InteropPatcher.EnsurePatched(_gameDir, SetActivity);
            inst.InstallMod(payload, SetActivity);
            inst.EnsureAppId(SetActivity);
            SetActivity("terminé · 1er lancement = génération interop (quelques min)");
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
                Role = MpRole.Menu,
                DisplayName = string.IsNullOrWhiteSpace(_name.Text) ? Environment.UserName : _name.Text.Trim(),
                AutoStart = true,
            };
            new GameRunner(_gameDir).WriteConfig(cfg);
            SetActivity("lancement du jeu (pseudo : " + cfg.DisplayName + ")…");
            new GameRunner(_gameDir).Launch();
        }
    }
}
