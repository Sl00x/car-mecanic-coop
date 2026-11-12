using System.Text;
using UnityEngine;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;
using CarMecaSim.Mp.Sync;

namespace CarMecaSim.Mp.UI
{
    /// <summary>
    /// F9 developer overlay (§19): real, measured co-op network + state diagnostics. Read-only; never
    /// mutates game state. Values come from the live session and the canonical PartSync state.
    /// </summary>
    public static class NetDebugOverlay
    {
        public static bool Visible;

        private static GUIStyle _box, _title, _line;

        private static void EnsureStyles()
        {
            if (_box != null) return;
            _box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.87f, 0.99f, 0.22f) } };
            _line = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, normal = { textColor = Color.white } };
        }

        public static void Draw(MpSession s)
        {
            if (!Visible || s == null) return;
            EnsureStyles();

            int peers = 0; int bestPing = -1;
            try { foreach (var p in s.Peers) { peers++; if (p.PingMs > 0 && (bestPing < 0 || p.PingMs < bestPing)) bestPing = p.PingMs; } } catch { }

            var sb = new StringBuilder();
            sb.AppendLine($"Role: <b>{(s.IsHost ? "HOST" : s.InLobby ? "CLIENT" : "—")}</b>");
            sb.AppendLine($"Session: {(s.InLobby ? "Connected" : "Idle")}   Lobby: {s.Net?.LobbyId.m_SteamID}");
            sb.AppendLine($"Steam peers: {peers}   RTT: {(bestPing >= 0 ? bestPing + " ms" : "—")}");
            sb.AppendLine($"Presence rx: {s.PresenceReceived}");
            sb.AppendLine("");
            var st = PartSync.State;
            sb.AppendLine($"Vehicle: <b>{(st != null ? st.Vehicle.ToString() : "none")}</b>");
            sb.AppendLine($"Part revision: {PartSync.Revision}");
            sb.AppendLine($"Removed parts: {PartSync.RemovedCount}");
            sb.AppendLine($"State hash: <b>{PartSync.Hash:X16}</b>");
            sb.AppendLine($"Active locks: {PartSync.ActiveLocks}");
            sb.AppendLine("");
            sb.AppendLine($"In game: {GameRefs.InGameSession}   In garage: {GameRefs.InGarage}");
            if (st != null)
            {
                sb.AppendLine("Off the car:");
                int shown = 0;
                foreach (var slot in st.RemovedSlots())
                {
                    if (shown++ >= 12) { sb.AppendLine("  …"); break; }
                    sb.AppendLine($"  • {slot}");
                }
            }

            var rect = new Rect(14, 90, 340, 430);
            GUI.Box(rect, GUIContent.none, _box);
            GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 10, rect.width - 24, rect.height - 20));
            GUILayout.Label("CMS COOP — NETWORK DEBUG (F9)", _title);
            GUILayout.Space(6);
            GUILayout.Label(sb.ToString(), _line);
            GUILayout.EndArea();
        }
    }
}
