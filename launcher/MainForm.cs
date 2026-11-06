using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CarMecaSim.Shared;

namespace CarMecaSim.Launcher
{
    /// <summary>
    /// Car Mechanic Simulator-styled launcher. It only installs/verifies and launches the game —
    /// hosting/joining & chat happen in-game (press F6). No host/join choice here by design.
    /// </summary>
    public sealed class MainForm : Form
    {
        // CMS palette
        static readonly Color Bg       = Color.FromArgb(24, 24, 27);
        static readonly Color Panel    = Color.FromArgb(37, 37, 42);
        static readonly Color PanelHi  = Color.FromArgb(46, 46, 52);
        static readonly Color Orange   = Color.FromArgb(245, 148, 30);
        static readonly Color OrangeHi = Color.FromArgb(255, 170, 55);
        static readonly Color TextCol  = Color.FromArgb(236, 236, 239);
        static readonly Color DimCol   = Color.FromArgb(150, 150, 156);
        static readonly Color Green    = Color.FromArgb(120, 205, 110);
        static readonly Color Red      = Color.FromArgb(214, 96, 84);

        string? _gameDir;
        readonly Label _statusLine = new();
        readonly Label _ind = new();
        readonly TextBox _name = new();
        readonly TextBox _log = new();
        readonly Button _play = new();
        readonly Button _setup = new();
        readonly Button _uninstall = new();

        public MainForm()
        {
            Text = "CarMecaSim — Co-op Launcher";
            ClientSize = new Size(600, 620);
            MinimumSize = new Size(560, 560);
            BackColor = Bg;
            ForeColor = TextCol;
            Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            // ---- header banner ----
            var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Panel };
            header.Paint += (_, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillRectangle(new SolidBrush(Orange), 0, 0, 8, header.Height);              // orange spine
                using var big = new Font("Segoe UI Black", 20f, FontStyle.Bold);
                using var sub = new Font("Segoe UI", 9.5f);
                g.DrawString("CAR MECHANIC SIMULATOR", big, new SolidBrush(TextCol), 26, 16);
                g.DrawString("CO·OP  —  shared garage multiplayer", sub, new SolidBrush(Orange), 30, 56);
            };
            Controls.Add(header);

            int y = 108;
            // ---- status card ----
            var card = MakePanel(16, y, 568, 108);
            _ind.SetBounds(14, 10, 540, 24); _ind.Font = new Font("Segoe UI", 10f, FontStyle.Bold); _ind.ForeColor = TextCol;
            _statusLine.SetBounds(14, 40, 540, 56); _statusLine.ForeColor = DimCol; _statusLine.AutoSize = false;
            card.Controls.Add(_ind); card.Controls.Add(_statusLine);
            Controls.Add(card);
            y += 120;

            // ---- name + actions ----
            var nameLbl = new Label { Text = "PSEUDO", Left = 20, Top = y + 6, AutoSize = true, ForeColor = DimCol, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            Controls.Add(nameLbl);
            StyleField(_name); _name.SetBounds(100, y, 300, 28); _name.Text = Environment.UserName;
            Controls.Add(_name);
            y += 44;

            StyleButton(_setup, "INSTALLER / RÉPARER", false); _setup.SetBounds(20, y, 230, 40);
            StyleButton(_uninstall, "DÉSINSTALLER", false); _uninstall.SetBounds(262, y, 150, 40);
            Controls.Add(_setup); Controls.Add(_uninstall);
            y += 52;

            StyleButton(_play, "▶  JOUER", true); _play.SetBounds(20, y, 560, 56); _play.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            Controls.Add(_play);
            y += 68;

            var hint = new Label { Text = "Héberger / rejoindre & chat se font EN JEU (touche F6, une fois dans une partie).",
                Left = 20, Top = y, Width = 560, Height = 20, ForeColor = Orange, Font = new Font("Segoe UI", 8.5f, FontStyle.Italic) };
            Controls.Add(hint);
            y += 26;

            // ---- log ----
            _log.SetBounds(16, y, 568, 150); _log.Multiline = true; _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical; _log.BackColor = Color.FromArgb(18, 18, 20); _log.ForeColor = DimCol;
            _log.BorderStyle = BorderStyle.FixedSingle; _log.Font = new Font("Consolas", 8.5f);
            _log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_log);

            _setup.Click += (_, _) => DoSetup();
            _uninstall.Click += (_, _) => DoUninstall();
            _play.Click += (_, _) => DoPlay();
            Load += (_, _) => RefreshStatus();
        }

        // ---- styling helpers ----
        Panel MakePanel(int x, int y, int w, int h)
        {
            var p = new Panel { Left = x, Top = y, Width = w, Height = h, BackColor = Panel };
            p.Paint += (_, e) => e.Graphics.FillRectangle(new SolidBrush(Orange), 0, 0, 4, p.Height);
            return p;
        }
        void StyleField(TextBox t)
        {
            t.BackColor = Color.FromArgb(18, 18, 20); t.ForeColor = TextCol;
            t.BorderStyle = BorderStyle.FixedSingle; t.Font = new Font("Segoe UI", 10f);
        }
        void StyleButton(Button b, string text, bool primary)
        {
            b.Text = text; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            b.ForeColor = primary ? Color.Black : TextCol;
            b.BackColor = primary ? Orange : PanelHi;
            b.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            b.FlatAppearance.MouseOverBackColor = primary ? OrangeHi : Color.FromArgb(60, 60, 68);
            b.FlatAppearance.MouseDownBackColor = primary ? OrangeHi : Color.FromArgb(70, 70, 80);
        }

        void LogLine(string s) { _log.AppendText(s + Environment.NewLine); }

        void RefreshStatus()
        {
            _gameDir = GameLocator.Find();
            if (_gameDir == null)
            {
                _ind.Text = "● JEU INTROUVABLE";  _ind.ForeColor = Red;
                _statusLine.Text = "Car Mechanic Simulator 2021 n'a pas été trouvé via Steam.";
                _play.Enabled = _setup.Enabled = _uninstall.Enabled = false;
                return;
            }
            var s = new ModInstaller(_gameDir).GetStatus();
            if (s.FullyReady) { _ind.Text = "● PRÊT À JOUER";  _ind.ForeColor = Green; }
            else              { _ind.Text = "● CONFIGURATION REQUISE"; _ind.ForeColor = Orange; }
            _statusLine.Text =
                $"Jeu : {_gameDir}\n" +
                $"BepInEx {Mark(s.LoaderInstalled)}    Mod co-op {Mark(s.ModInstalled)} {s.InstalledModVersion}";
            _play.Enabled = s.FullyReady;
            _setup.Enabled = true;
            _uninstall.Enabled = s.LoaderInstalled || s.ModInstalled;
        }

        static string Mark(bool b) => b ? "✓" : "✗";

        void DoSetup()
        {
            if (_gameDir == null) return;
            var payload = ModInstaller.FindPayloadRoot();
            if (payload == null) { LogLine("ERREUR : payload d'installation introuvable à côté du launcher."); return; }
            var inst = new ModInstaller(_gameDir);
            LogLine("Installation…");
            if (!inst.EnsureLoader(payload, LogLine)) { LogLine("Échec de l'installation de BepInEx."); return; }
            InteropPatcher.EnsurePatched(_gameDir, LogLine);
            if (!inst.InstallMod(payload, LogLine)) { LogLine("Échec de l'installation du mod (est-il compilé ?)."); }
            inst.EnsureAppId(LogLine);
            LogLine("Terminé. Le 1er lancement génère l'interop (quelques minutes).");
            RefreshStatus();
        }

        void DoUninstall()
        {
            if (_gameDir == null) return;
            var all = MessageBox.Show("Supprimer aussi BepInEx (retour au jeu vanilla) ?",
                "Désinstaller", MessageBoxButtons.YesNo) == DialogResult.Yes;
            new ModInstaller(_gameDir).Uninstall(all, LogLine);
            RefreshStatus();
        }

        void DoPlay()
        {
            if (_gameDir == null) return;
            var cfg = new MpConfig
            {
                Role = MpRole.Menu,  // host/join happens in-game
                DisplayName = string.IsNullOrWhiteSpace(_name.Text) ? Environment.UserName : _name.Text.Trim(),
                AutoStart = true,
            };
            var runner = new GameRunner(_gameDir);
            runner.WriteConfig(cfg);
            LogLine($"Lancement du jeu (pseudo : {cfg.DisplayName})…");
            runner.Launch();
        }
    }
}
