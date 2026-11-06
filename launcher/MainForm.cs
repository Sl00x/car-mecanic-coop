using System;
using System.Drawing;
using System.Windows.Forms;
using CarMecaSim.Shared;

namespace CarMecaSim.Launcher
{
    public sealed class MainForm : Form
    {
        private string? _gameDir;
        private readonly Label _status = new();
        private readonly TextBox _log = new();
        private readonly TextBox _name = new();
        private readonly RadioButton _host = new() { Text = "Host a game", Checked = true };
        private readonly RadioButton _join = new() { Text = "Join a game" };
        private readonly TextBox _lobby = new() { PlaceholderText = "Lobby ID (from your host)" };
        private readonly NumericUpDown _max = new() { Minimum = 2, Maximum = 8, Value = 4 };
        private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button _setup = new() { Text = "Setup / Repair" };
        private readonly Button _launch = new() { Text = "Launch co-op" };
        private readonly Button _uninstall = new() { Text = "Uninstall" };

        public MainForm()
        {
            Text = "CarMecaSim — Co-op Launcher";
            Size = new Size(560, 620);
            MinimumSize = new Size(520, 560);
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;

            int y = 14;
            Add(new Label { Text = "Car Mechanic Simulator 2021 — Shared-Garage Co-op",
                            Font = new Font("Segoe UI", 12f, FontStyle.Bold), AutoSize = true, Left = 16, Top = y });
            y += 34;

            // --- Status box ---
            var statusBox = new GroupBox { Text = "Install status", Left = 16, Top = y, Width = 510, Height = 120 };
            _status.Left = 12; _status.Top = 22; _status.Width = 486; _status.Height = 60; _status.AutoSize = false;
            statusBox.Controls.Add(_status);
            _setup.SetBounds(12, 84, 130, 26);
            _uninstall.SetBounds(150, 84, 110, 26);
            statusBox.Controls.Add(_setup); statusBox.Controls.Add(_uninstall);
            Controls.Add(statusBox);
            y += 132;

            // --- Play box ---
            var playBox = new GroupBox { Text = "Play", Left = 16, Top = y, Width = 510, Height = 230 };
            int py = 26;
            playBox.Controls.Add(new Label { Text = "Display name", Left = 12, Top = py + 3, AutoSize = true });
            _name.SetBounds(130, py, 360, 24); _name.Text = Environment.UserName;
            playBox.Controls.Add(_name); py += 36;

            _host.SetBounds(12, py, 150, 24); _join.SetBounds(170, py, 150, 24);
            playBox.Controls.Add(_host); playBox.Controls.Add(_join); py += 34;

            playBox.Controls.Add(new Label { Text = "Lobby ID (join)", Left = 12, Top = py + 3, AutoSize = true });
            _lobby.SetBounds(130, py, 360, 24); _lobby.Enabled = false;
            playBox.Controls.Add(_lobby); py += 36;

            playBox.Controls.Add(new Label { Text = "Max players (host)", Left = 12, Top = py + 3, AutoSize = true });
            _max.SetBounds(130, py, 60, 24);
            playBox.Controls.Add(_max);
            playBox.Controls.Add(new Label { Text = "Lobby type", Left = 210, Top = py + 3, AutoSize = true });
            _type.Items.AddRange(new object[] { "FriendsOnly", "Public", "Private" }); _type.SelectedIndex = 0;
            _type.SetBounds(290, py, 120, 24);
            playBox.Controls.Add(_type); py += 40;

            _launch.SetBounds(12, py, 478, 34); _launch.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            playBox.Controls.Add(_launch);
            Controls.Add(playBox);
            y += 242;

            // --- Log ---
            _log.SetBounds(16, y, 510, 120); _log.Multiline = true; _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical; _log.BackColor = Color.White;
            _log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_log);

            // wiring
            _host.CheckedChanged += (_, _) => { _lobby.Enabled = _join.Checked; };
            _join.CheckedChanged += (_, _) => { _lobby.Enabled = _join.Checked; };
            _setup.Click += (_, _) => DoSetup();
            _uninstall.Click += (_, _) => DoUninstall();
            _launch.Click += (_, _) => DoLaunch();
            Load += (_, _) => RefreshStatus();
        }

        private void Add(Control c) => Controls.Add(c);
        private void LogLine(string s) => _log.AppendText(s + Environment.NewLine);

        private void RefreshStatus()
        {
            _gameDir = GameLocator.Find();
            if (_gameDir == null)
            {
                _status.Text = "Game not found. Use the Steam library, or place the launcher in the game folder.";
                _setup.Enabled = _launch.Enabled = _uninstall.Enabled = false;
                return;
            }
            var s = new ModInstaller(_gameDir).GetStatus();
            _status.Text =
                $"Game: {_gameDir}\n" +
                $"BepInEx: {YN(s.LoaderInstalled)}   Interop: {YN(s.InteropGenerated)}   " +
                $"Co-op mod: {YN(s.ModInstalled)} {s.InstalledModVersion}\n" +
                (s.FullyReady ? "Status: READY to play." : "Status: click Setup / Repair.");
            _launch.Enabled = s.FullyReady;
            _setup.Enabled = true;
            _uninstall.Enabled = s.LoaderInstalled || s.ModInstalled;
        }

        private void DoSetup()
        {
            if (_gameDir == null) return;
            var payload = ModInstaller.FindPayloadRoot();
            if (payload == null) { LogLine("ERROR: install payload not found next to launcher."); return; }
            var inst = new ModInstaller(_gameDir);
            LogLine("Setup starting...");
            if (!inst.EnsureLoader(payload, LogLine)) { LogLine("BepInEx step failed."); return; }
            if (!inst.GetStatus().InteropGenerated)
                LogLine("Note: interop not generated yet — it is created automatically the first time the game launches.");
            if (!inst.InstallMod(payload, LogLine)) { LogLine("Mod install failed (is the mod built?)."); }
            inst.EnsureAppId(LogLine);
            LogLine("Setup done.");
            RefreshStatus();
        }

        private void DoUninstall()
        {
            if (_gameDir == null) return;
            var all = MessageBox.Show("Also remove BepInEx entirely (full vanilla restore)?",
                "Uninstall", MessageBoxButtons.YesNo) == DialogResult.Yes;
            new ModInstaller(_gameDir).Uninstall(all, LogLine);
            RefreshStatus();
        }

        private void DoLaunch()
        {
            if (_gameDir == null) return;
            var cfg = new MpConfig
            {
                Role = _host.Checked ? MpRole.Host : MpRole.Join,
                DisplayName = string.IsNullOrWhiteSpace(_name.Text) ? Environment.UserName : _name.Text.Trim(),
                MaxPlayers = (int)_max.Value,
                LobbyType = Enum.Parse<MpLobbyType>(_type.SelectedItem!.ToString()!),
            };
            if (cfg.Role == MpRole.Join)
            {
                if (!ulong.TryParse(_lobby.Text.Trim(), out var id))
                { MessageBox.Show("Enter a numeric Lobby ID to join."); return; }
                cfg.LobbyId = id;
            }
            var runner = new GameRunner(_gameDir);
            var path = runner.WriteConfig(cfg);
            LogLine($"Config written ({cfg.Role}) -> {path}");
            runner.Launch();
            LogLine("Launching game via Steam...");
        }

        private static string YN(bool b) => b ? "yes" : "no";
    }
}
