using System;
using UnityEngine;
using CarMecaSim.Shared;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;
using CarMecaSim.Mp.Sync;

namespace CarMecaSim.Mp.UI
{
    /// <summary>
    /// In-game co-op UI. Only drawn during an actual game session (never menu/loading).
    /// We do NOT draw money/XP — the game's own HUD stays. Just the co-op panel + nametags.
    /// F6 opens the full panel (and the plugin freezes the game while it's open).
    /// </summary>
    public sealed class Overlay
    {
        private readonly MpSession _s;
        public bool Visible = false;   // full panel (F6); a slim tab shows otherwise

        private static readonly string Ver = "CarMecaSim v" +
            (typeof(Overlay).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?");

        private string _lobbyInput = "";
        private string _chatInput = "";
        private Vector2 _chatScroll, _jobScroll;
        private bool _focusChat;

        public Overlay(MpSession s) { _s = s; }

        public void Draw()
        {
            if (!GameRefs.InGameSession) return;      // hard gate: nothing in menus/loading
            Theme.EnsureInit();
            GUI.Label(new Rect(Screen.width - 190, 6, 184, 20), Ver, Theme.KeyHint);   // version, top-right
            DrawNametags();
            if (Visible) DrawPanel();
            else DrawTab();
        }

        private void DrawTab()
        {
            var r = new Rect(16, 16, 172, 30);
            GUI.Box(r, GUIContent.none, Theme.Glass);
            GUI.Box(new Rect(r.x, r.y, 3, r.height), GUIContent.none, Theme.OrangeFill);
            GUI.Label(new Rect(r.x + 14, r.y, r.width - 20, r.height),
                _s.InLobby ? "● CO-OP   CONNECTÉ" : "CO-OP   ·   F6", Theme.TabLabel);
        }

        private void DrawPanel()
        {
            const int w = 360;
            int h = _s.InLobby ? 580 : 230;
            var r = new Rect(16, 16, w, h);

            // layered glass panel with a glowing left edge
            GUI.Box(new Rect(r.x - 2, r.y - 2, w + 4, h + 4), GUIContent.none, Theme.GlassShadow);
            GUI.Box(r, GUIContent.none, Theme.Glass);
            GUI.Box(new Rect(r.x, r.y, 4, h), GUIContent.none, Theme.OrangeFill);

            // title bar
            var tb = new Rect(r.x, r.y, w, 34);
            GUI.Label(new Rect(tb.x + 18, tb.y + 7, 220, 22), "MULTIJOUEUR CO-OP", Theme.PanelTitle);
            GUI.Label(new Rect(tb.x + w - 54, tb.y + 9, 40, 18), "F6", Theme.KeyHint);
            GUI.Box(new Rect(r.x + 16, r.y + 36, w - 32, 1), GUIContent.none, Theme.Divider);

            GUILayout.BeginArea(new Rect(r.x + 18, r.y + 46, w - 36, h - 56));

            // live diagnostics (helps verify sync while testing)
            bool eco = GameState.Available;
            GUILayout.Label($"garage {(GameRefs.InGarage ? "✓" : "✗")}    éco {(eco ? "✓ " + GameState.Money : "✗")}    présence {_s.PresenceReceived}", Theme.MutedS);
            GUILayout.Space(4);

            if (!_s.InLobby)
            {
                GUILayout.Label(_s.Net.Ready ? "Connecté à Steam · prêt" : "Connexion à Steam…", Theme.MutedS);
                GUILayout.Space(8);
                if (Button("HÉBERGER UNE PARTIE", true)) _s.HostLobby();
                GUILayout.Space(10);
                GUILayout.Label("REJOINDRE  ·  code lobby", Theme.SectionLabel);
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                _lobbyInput = GUILayout.TextField(_lobbyInput, Theme.Field, GUILayout.Height(30));
                if (Button("OK", false, 64) && ulong.TryParse(_lobbyInput.Trim(), out var id)) _s.JoinLobby(id);
                GUILayout.EndHorizontal();
                GUILayout.Space(8);
                GUILayout.Label("…ou invite un ami via l'overlay Steam (Maj+Tab).", Theme.MutedS);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(_s.IsHost ? "● HÔTE" : "● CLIENT", Theme.RolePill);
                GUILayout.FlexibleSpace();
                if (Button("QUITTER", false, 92)) _s.Leave();
                GUILayout.EndHorizontal();
                GUILayout.Label("LOBBY  " + _s.Net.LobbyId.m_SteamID, Theme.MutedS);

                GUILayout.Space(8);
                GUILayout.Label("JOUEURS  —  " + (1 + _s.Peers.Count), Theme.SectionLabel);
                PlayerRow(_s.Config.DisplayName, Theme.Orange, _s.IsHost ? "HÔTE" : "TOI", -1);
                foreach (var p in _s.Peers)
                {
                    bool fresh = UnityEngine.Time.unscaledTime - p.LastUpdate < 3f;
                    PlayerRow(p.Name, fresh ? Theme.Green : new Color(0.85f, 0.4f, 0.35f),
                              fresh ? (_s.IsHost ? "CLIENT" : "JOUEUR") : "hors ligne", fresh ? p.PingMs : -1);
                }

                // --- MISSIONS (host's job board) ---
                GUILayout.Space(8);
                var jobs = _s.IsHost ? OrderSync.LocalJobs() : OrderSync.HostJobs;
                GUILayout.Label("MISSIONS  —  " + jobs.Count + (_s.IsHost ? "" : "  (de l'hôte)"), Theme.SectionLabel);
                _jobScroll = GUILayout.BeginScrollView(_jobScroll, GUILayout.Height(108));
                foreach (var j in jobs)
                {
                    GUILayout.BeginHorizontal(GUILayout.Height(24));
                    var car = j.Car.StartsWith("car_") ? j.Car.Substring(4) : j.Car;
                    GUILayout.Label($"{(j.Mission ? "★ " : "")}{car}   ${j.Pay}  +{j.Xp}xp", Theme.Label);
                    GUILayout.FlexibleSpace();
                    if (Button("Prendre", true, 74)) OrderSync.Take(j);
                    GUILayout.EndHorizontal();
                }
                if (jobs.Count == 0) GUILayout.Label("aucune mission dispo", Theme.MutedS);
                GUILayout.EndScrollView();

                GUILayout.Space(8);
                GUILayout.Label("CHAT", Theme.SectionLabel);
                _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(104));
                foreach (var line in _s.ChatLog) GUILayout.Label(line, Theme.ChatLine);
                GUILayout.EndScrollView();

                GUI.SetNextControlName("cms_chat");
                _chatInput = GUILayout.TextField(_chatInput, Theme.Field, GUILayout.Height(28));
                if (_focusChat) { GUI.FocusControl("cms_chat"); _focusChat = false; }
                var e = Event.current;
                if (e != null && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                {
                    if (!string.IsNullOrWhiteSpace(_chatInput)) { _s.SendChat(_chatInput.Trim()); _chatInput = ""; _focusChat = true; }
                }
            }
            GUILayout.EndArea();
        }

        private static bool Button(string text, bool primary, int width = 0)
        {
            var style = primary ? Theme.BtnPrimary : Theme.Btn;
            return width > 0
                ? GUILayout.Button(text, style, GUILayout.Height(30), GUILayout.Width(width))
                : GUILayout.Button(text, style, GUILayout.Height(34));
        }

        private static void PlayerRow(string name, Color dot, string badge, int ping)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(24));
            var rect = GUILayoutUtility.GetRect(12, 24, GUILayout.Width(16));
            GUI.color = dot; GUI.Label(new Rect(rect.x, rect.y + 6, 12, 12), "●", Theme.Dot); GUI.color = Color.white;
            GUILayout.Label(name, Theme.Label);
            GUILayout.FlexibleSpace();
            if (ping >= 0) GUILayout.Label(ping + " ms", Theme.SectionLabel, GUILayout.Width(56));
            if (!string.IsNullOrEmpty(badge)) GUILayout.Label(badge, Theme.Tag, GUILayout.Width(62));
            GUILayout.EndHorizontal();
        }

        private void DrawNametags()
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var p in _s.Peers)
            {
                var sp = cam.WorldToScreenPoint(p.WorldPos + new Vector3(0, 1.15f, 0));
                if (sp.z <= 0) continue;
                var r = new Rect(sp.x - 70, Screen.height - sp.y - 14, 140, 24);
                GUI.Box(r, p.Name, Theme.Tag);
            }
        }
    }
}
