using System;
using UnityEngine;
using CarMecaSim.Shared;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;

namespace CarMecaSim.Mp.UI
{
    /// <summary>
    /// Car Mechanic Simulator-styled in-game UI. Only drawn during an actual game session
    /// (never the main menu or loading). Top-right: money/scraps/level/XP HUD.
    /// Top-left: co-op panel (host/join/chat). Plus floating nametags for remote players.
    /// </summary>
    public sealed class Overlay
    {
        private readonly MpSession _s;
        public bool Visible = true;   // co-op panel visibility (F6)

        private string _lobbyInput = "";
        private string _chatInput = "";
        private Vector2 _chatScroll;
        private bool _focusChat;

        public Overlay(MpSession s) { _s = s; }

        public void Draw()
        {
            // HARD GATE: nothing in menus or during loading.
            if (!GameRefs.InGameSession) return;
            Theme.EnsureInit();

            DrawStatsHud();
            DrawNametags();
            if (Visible) DrawCoopPanel();
            else DrawCollapsedTab();
        }

        // ---- top-right money / scraps / level / XP ----
        private void DrawStatsHud()
        {
            const int w = 226, h = 96;
            var r = new Rect(Screen.width - w - 12, 12, w, h);
            GUI.Box(r, GUIContent.none, Theme.Panel_);
            // accent bar
            GUI.Box(new Rect(r.x, r.y, w, 4), GUIContent.none, Theme.OrangeFill);

            var money = GameState.Available ? GameState.Money : 0;
            var scraps = GameState.Available ? GameState.Scraps : 0;
            var level = GameState.Available ? GameState.Level : 0;
            var exp = GameState.Available ? GameState.Exp : 0;

            float x = r.x + 12, y = r.y + 12;
            GUI.Label(new Rect(x, y, 90, 22), "ARGENT", Theme.LabelDim);
            GUI.Label(new Rect(x + 80, y, w - 92, 22), "$ " + money.ToString("N0"), MoneyStyle());
            y += 24;
            GUI.Label(new Rect(x, y, 90, 22), "FERRAILLE", Theme.LabelDim);
            GUI.Label(new Rect(x + 80, y, w - 92, 22), scraps.ToString("N0"), ScrapsStyle());
            y += 26;
            GUI.Label(new Rect(x, y, 70, 22), "NIVEAU " + level, Theme.Title);
            GUI.Label(new Rect(x + 86, y + 2, w - 98, 20), "XP " + exp.ToString("N0"), Theme.Value);
        }

        private GUIStyle _money, _scrapsSt;
        private GUIStyle MoneyStyle() { _money ??= new GUIStyle(Theme.Value) { }; _money.normal.textColor = Theme.Money; return _money; }
        private GUIStyle ScrapsStyle() { _scrapsSt ??= new GUIStyle(Theme.Value) { }; _scrapsSt.normal.textColor = Theme.Scraps; return _scrapsSt; }

        // ---- top-left co-op panel ----
        private void DrawCoopPanel()
        {
            const int w = 330;
            int h = _s.InLobby ? 400 : 250;
            var r = new Rect(12, 12, w, h);
            GUI.Box(r, GUIContent.none, Theme.Panel_);
            GUI.Box(new Rect(r.x, r.y, w, 26), "  MULTIJOUEUR CO-OP        [F6]", Theme.Header);

            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 34, w - 24, h - 44));

            if (!_s.InLobby)
            {
                GUILayout.Label(_s.Net.Ready ? "Connecté à Steam. Prêt." : "Initialisation de Steam...", Theme.Label);
                GUILayout.Space(6);
                if (GUILayout.Button("HÉBERGER UNE PARTIE", Theme.BtnPrimary, GUILayout.Height(34))) _s.HostLobby();
                GUILayout.Space(8);
                GUILayout.Label("Rejoindre via un code lobby :", Theme.LabelDim);
                GUILayout.BeginHorizontal();
                _lobbyInput = GUILayout.TextField(_lobbyInput, Theme.Field, GUILayout.Height(28));
                if (GUILayout.Button("REJOINDRE", Theme.Btn, GUILayout.Width(100), GUILayout.Height(28))
                    && ulong.TryParse(_lobbyInput.Trim(), out var id)) _s.JoinLobby(id);
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
                GUILayout.Label("…ou invite un ami via l'overlay Steam (Maj+Tab).", Theme.LabelDim);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(_s.IsHost ? "RÔLE : HÔTE" : "RÔLE : CLIENT", Theme.Title);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("QUITTER", Theme.Btn, GUILayout.Width(84), GUILayout.Height(24))) _s.Leave();
                GUILayout.EndHorizontal();
                GUILayout.Label($"Lobby : {_s.Net.LobbyId.m_SteamID}", Theme.LabelDim);

                GUILayout.Space(4);
                GUILayout.Label($"JOUEURS ({1 + _s.Peers.Count})", Theme.Header, GUILayout.Height(22));
                GUILayout.Label("  ● " + _s.Config.DisplayName + " (toi)", Theme.Label);
                foreach (var p in _s.Peers) GUILayout.Label("  ● " + p.Name, Theme.Label);

                GUILayout.Space(6);
                GUILayout.Label("CHAT", Theme.Header, GUILayout.Height(22));
                _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(140));
                foreach (var line in _s.ChatLog) GUILayout.Label(line, Theme.ChatLine);
                GUILayout.EndScrollView();

                GUI.SetNextControlName("cms_chat");
                _chatInput = GUILayout.TextField(_chatInput, Theme.Field, GUILayout.Height(26));
                if (_focusChat) { GUI.FocusControl("cms_chat"); _focusChat = false; }
                var e = Event.current;
                if (e != null && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                {
                    if (!string.IsNullOrWhiteSpace(_chatInput)) { _s.SendChat(_chatInput.Trim()); _chatInput = ""; _focusChat = true; }
                }
            }

            GUILayout.EndArea();
        }

        private void DrawCollapsedTab()
        {
            var r = new Rect(12, 12, 150, 26);
            GUI.Box(r, _s.InLobby ? "  CO-OP ● connecté" : "  CO-OP  [F6]", Theme.Header);
        }

        private void DrawNametags()
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var p in _s.Peers)
            {
                var sp = cam.WorldToScreenPoint(p.WorldPos + new Vector3(0, 1.1f, 0));
                if (sp.z <= 0) continue;
                var r = new Rect(sp.x - 70, Screen.height - sp.y - 12, 140, 22);
                GUI.Box(r, p.Name, Theme.Chip);
            }
        }
    }
}
