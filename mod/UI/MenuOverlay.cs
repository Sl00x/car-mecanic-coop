using System;
using UnityEngine;
using Steamworks;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;

namespace CarMecaSim.Mp.UI
{
    /// <summary>
    /// Co-op entry point on the game's MAIN MENU: "Héberger une partie" / "Rejoindre un ami".
    /// Hosting creates a Steam lobby then opens the native save-selection; the chosen save becomes
    /// the shared garage. Joining enters a friend's lobby (Steam overlay or lobby code), then the
    /// player picks any save to drop into the garage, where the host's snapshot replaces it.
    /// Only drawn while the top-level menu is on screen (hidden once a native sub-window opens).
    /// </summary>
    public sealed class MenuOverlay
    {
        private readonly MpSession _s;
        private string _codeInput = "";
        private bool _showJoin;

        public MenuOverlay(MpSession s) { _s = s; }

        public void Draw()
        {
            if (!MenuActions.OnMainMenu || !MenuActions.MainSectionActive()) return;
            if (GameRefs.InGameSession) return;   // safety: never over gameplay
            Theme.EnsureInit();

            const int w = 304, h = 232;
            var r = new Rect(Screen.width - w - 44, Screen.height / 2 - h / 2, w, h);
            GUI.Box(new Rect(r.x - 2, r.y - 2, w + 4, h + 4), GUIContent.none, Theme.GlassShadow);
            GUI.Box(r, GUIContent.none, Theme.Glass);
            GUI.Box(new Rect(r.x, r.y, 4, h), GUIContent.none, Theme.OrangeFill);
            GUI.Label(new Rect(r.x + 18, r.y + 10, w - 30, 22), "CARMECA CO-OP", Theme.PanelTitle);
            GUI.Box(new Rect(r.x + 16, r.y + 38, w - 32, 1), GUIContent.none, Theme.Divider);

            GUILayout.BeginArea(new Rect(r.x + 18, r.y + 48, w - 36, h - 58));
            if (!_s.Net.Ready)
            {
                GUILayout.Label("Connexion à Steam…", Theme.MutedS);
            }
            else if (_s.InLobby)
            {
                GUILayout.Label(_s.IsHost ? "● HÔTE — lobby créé" : "● CLIENT — connecté", Theme.RolePill);
                GUILayout.Space(6);
                GUILayout.Label(_s.IsHost
                    ? "Choisis la sauvegarde à héberger :\nelle devient le garage partagé."
                    : "Charge une sauvegarde pour entrer :\nle garage de l'hôte la remplacera.", Theme.MutedS);
                GUILayout.Space(8);
                if (GUILayout.Button("CHOISIR UNE SAUVEGARDE", Theme.BtnPrimary, GUILayout.Height(36)))
                    MenuActions.OpenSaveSelection();
                GUILayout.Space(6);
                if (GUILayout.Button("Quitter le lobby", Theme.Btn, GUILayout.Height(26))) _s.Leave();
                GUILayout.Label("LOBBY " + _s.Net.LobbyId.m_SteamID, Theme.MutedS);
            }
            else if (!_showJoin)
            {
                if (GUILayout.Button("HÉBERGER UNE PARTIE", Theme.BtnPrimary, GUILayout.Height(42)))
                {
                    _s.HostLobby();
                    MenuActions.OpenSaveSelection();
                }
                GUILayout.Space(10);
                if (GUILayout.Button("REJOINDRE UN AMI", Theme.Btn, GUILayout.Height(42))) _showJoin = true;
                GUILayout.Space(10);
                GUILayout.Label("Héberger : ta sauvegarde devient le\ngarage partagé pour tout le monde.", Theme.MutedS);
            }
            else
            {
                GUILayout.Label("REJOINDRE", Theme.SectionLabel);
                if (GUILayout.Button("Liste d'amis Steam", Theme.BtnPrimary, GUILayout.Height(34)))
                { try { SteamFriends.ActivateGameOverlay("friends"); } catch { } }
                GUILayout.Label("puis clic droit sur l'hôte → Rejoindre.", Theme.MutedS);
                GUILayout.Space(8);
                GUILayout.Label("ou code lobby :", Theme.SectionLabel);
                GUILayout.BeginHorizontal();
                _codeInput = GUILayout.TextField(_codeInput, Theme.Field, GUILayout.Height(28));
                if (GUILayout.Button("OK", Theme.Btn, GUILayout.Width(56), GUILayout.Height(28))
                    && ulong.TryParse(_codeInput.Trim(), out var id)) _s.JoinLobby(id);
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
                if (GUILayout.Button("← retour", Theme.Btn, GUILayout.Height(24))) _showJoin = false;
            }
            GUILayout.EndArea();
        }
    }
}
