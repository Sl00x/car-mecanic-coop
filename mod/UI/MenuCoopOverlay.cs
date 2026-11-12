using System.Collections.Generic;
using UnityEngine;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;
using CarMecaSim.Mp.Net;

namespace CarMecaSim.Mp.UI
{
    /// <summary>
    /// Main-menu co-op panel: lists friends currently hosting a CMS co-op game (via Steam rich
    /// presence) and lets you JOIN them in one click, without the Steam overlay. Hosting stays
    /// automatic (load a save → you're hosting). Only drawn on the game's main menu.
    /// </summary>
    public sealed class MenuCoopOverlay
    {
        private readonly MpSession _s;
        private readonly List<SteamNet.JoinableFriend> _friends = new();
        private float _lastRefresh = -999f;
        private Vector2 _scroll;

        public MenuCoopOverlay(MpSession s) { _s = s; }

        private void Refresh()
        {
            if (Time.unscaledTime - _lastRefresh < 3f) return;
            _lastRefresh = Time.unscaledTime;
            _friends.Clear();
            if (_s.Net.Ready) foreach (var f in _s.JoinableFriends()) _friends.Add(f);
        }

        public void Draw()
        {
            if (!GameRefs.OnMainMenu || GameRefs.InGameSession) return;
            Theme.EnsureInit();
            _s.EnsureSteam();
            Refresh();

            const int w = 360;
            int h = 158 + Mathf.Clamp(_friends.Count, 0, 6) * 42 + (_s.InLobby ? 28 : 0);
            // bottom-left of the main menu (clear of the menu buttons and the DLC news cards)
            var r = new Rect(48, Screen.height - h - 70, w, h);

            GUI.Box(new Rect(r.x - 2, r.y - 2, w + 4, h + 4), GUIContent.none, Theme.GlassShadow);
            GUI.Box(r, GUIContent.none, Theme.Glass);
            GUI.Box(new Rect(r.x, r.y, 4, h), GUIContent.none, Theme.OrangeFill);
            GUI.Label(new Rect(r.x + 18, r.y + 12, w - 30, 22), "MULTIJOUEUR CO-OP", Theme.PanelTitle);
            GUI.Box(new Rect(r.x + 16, r.y + 40, w - 32, 1), GUIContent.none, Theme.Divider);

            GUILayout.BeginArea(new Rect(r.x + 18, r.y + 50, w - 36, h - 60));

            if (!_s.Net.Ready)
            {
                GUILayout.Label("Connexion à Steam…", Theme.MutedS);
                GUILayout.EndArea();
                return;
            }

            if (_s.InLobby)
            {
                GUILayout.Label(_s.IsHost ? "● Tu héberges une partie" : "● Connecté à une partie", Theme.RolePill);
                GUILayout.Space(4);
                GUILayout.Label("Charge/continue ta sauvegarde pour entrer\ndans le garage partagé.", Theme.MutedS);
                GUILayout.Space(6);
                if (GUILayout.Button("Quitter le lobby", Theme.Btn, GUILayout.Height(26))) _s.Leave();
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("AMIS QUI HÉBERGENT  —  " + _friends.Count, Theme.SectionLabel);
            GUILayout.Space(2);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Clamp(_friends.Count, 1, 6) * 42 + 6));
            if (_friends.Count == 0)
                GUILayout.Label("Aucun ami en partie pour l'instant.\nDemande-lui de lancer sa sauvegarde.", Theme.MutedS);
            foreach (var f in _friends)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(38));
                GUILayout.BeginVertical();
                GUILayout.Label(f.Name, Theme.Label);
                GUILayout.Label("partie ouverte", Theme.SectionLabel);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("REJOINDRE", Theme.BtnPrimary, GUILayout.Width(96), GUILayout.Height(30)))
                    _s.JoinLobby(f.Lobby);
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            GUILayout.Label("Pour HÉBERGER : continue ta sauvegarde,\ntes amis te verront ici automatiquement.", Theme.MutedS);

            GUILayout.EndArea();
        }
    }
}
