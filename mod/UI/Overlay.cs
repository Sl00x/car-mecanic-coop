using System;
using UnityEngine;
using CarMecaSim.Shared;
using CarMecaSim.Mp.Core;

namespace CarMecaSim.Mp.UI
{
    /// <summary>Minimal IMGUI overlay: session status, host/join/leave, player list, chat, nametags.</summary>
    public sealed class Overlay
    {
        private readonly MpSession _s;
        public bool Visible = true;

        private string _lobbyInput = "";
        private string _chatInput = "";
        private Vector2 _chatScroll;
        private bool _focusChat;

        public Overlay(MpSession s) { _s = s; }

        public void Draw()
        {
            if (!Visible) return;
            DrawNametags();

            var panel = new Rect(10, 10, 320, 360);
            GUI.Box(panel, "");
            GUILayout.BeginArea(new Rect(panel.x + 10, panel.y + 8, panel.width - 20, panel.height - 16));

            GUILayout.Label($"<b>CarMecaSim Co-op</b> v{Constants.ModVersion}   (F6 hide)");
            GUILayout.Label(_s.InLobby
                ? $"Lobby: {_s.Net.LobbyId.m_SteamID}  [{(_s.IsHost ? "HOST" : "client")}]"
                : "Not connected");
            GUILayout.Label($"You: {_s.Config.DisplayName}   Players: {1 + _s.Peers.Count}");

            GUILayout.Space(4);
            if (!_s.InLobby)
            {
                if (GUILayout.Button("Host game")) _s.HostLobby();
                GUILayout.BeginHorizontal();
                _lobbyInput = GUILayout.TextField(_lobbyInput, GUILayout.Width(190));
                if (GUILayout.Button("Join") && ulong.TryParse(_lobbyInput.Trim(), out var id)) _s.JoinLobby(id);
                GUILayout.EndHorizontal();
                GUILayout.Label("<i>Paste a Lobby ID, or use Steam overlay invite.</i>");
            }
            else
            {
                if (GUILayout.Button("Leave session")) _s.Leave();
                GUILayout.Label("<b>Players</b>");
                GUILayout.Label($"  • {_s.Config.DisplayName} (you)");
                foreach (var p in _s.Peers)
                    GUILayout.Label($"  • {p.Name}");
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>Chat</b> (Enter to send)");
            _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(120));
            foreach (var line in _s.ChatLog) GUILayout.Label(line);
            GUILayout.EndScrollView();

            GUI.SetNextControlName("cms_chat");
            _chatInput = GUILayout.TextField(_chatInput);
            if (_focusChat) { GUI.FocusControl("cms_chat"); _focusChat = false; }

            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                if (!string.IsNullOrWhiteSpace(_chatInput))
                {
                    _s.SendChat(_chatInput.Trim());
                    _chatInput = "";
                    _focusChat = true;
                }
            }

            GUILayout.EndArea();
        }

        private void DrawNametags()
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var p in _s.Peers)
            {
                var wp = p.WorldPos + new Vector3(0, 1.1f, 0);
                var sp = cam.WorldToScreenPoint(wp);
                if (sp.z <= 0) continue; // behind camera
                var r = new Rect(sp.x - 60, Screen.height - sp.y - 10, 120, 20);
                GUI.Label(r, p.Name);
            }
        }
    }
}
