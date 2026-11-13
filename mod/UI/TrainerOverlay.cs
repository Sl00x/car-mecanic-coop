using UnityEngine;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;

namespace CarMecaSim.Mp.UI
{
    /// <summary>
    /// Host "mega-trainer" panel (F8). Right-hand side so it never overlaps the F6 co-op panel. Uses the same
    /// freeze/cursor handling as F6 (Plugin.SetFrozen reacts to this being visible), so the mouse works.
    /// Actions live in <see cref="Trainer"/>; the economy cheats sync to guests through the normal economy path.
    /// </summary>
    public static class TrainerOverlay
    {
        public static bool Visible;

        public static void Draw(MpSession s)
        {
            if (!GameRefs.InGameSession || !Visible) return;
            Theme.EnsureInit();

            const int w = 300;
            int h = 452;
            var r = new Rect(Screen.width - w - 16, 16, w, h);

            GUI.Box(new Rect(r.x - 2, r.y - 2, w + 4, h + 4), GUIContent.none, Theme.GlassShadow);
            GUI.Box(r, GUIContent.none, Theme.Glass);
            GUI.Box(new Rect(r.x + w - 4, r.y, 4, h), GUIContent.none, Theme.OrangeFill);

            GUI.Label(new Rect(r.x + 18, r.y + 7, 240, 22), "MÉGA-TRAINER", Theme.PanelTitle);
            GUI.Label(new Rect(r.x + w - 52, r.y + 9, 40, 18), "F8", Theme.KeyHint);
            GUI.Box(new Rect(r.x + 16, r.y + 36, w - 32, 1), GUIContent.none, Theme.Divider);

            GUILayout.BeginArea(new Rect(r.x + 18, r.y + 46, w - 36, h - 56));

            bool host = s != null && s.IsHost;
            bool eco = GameState.Available;
            GUILayout.Label(host ? "● HÔTE — l'argent se synchronise" : (s != null && s.InLobby ? "● CLIENT — local seulement" : "● SOLO"), Theme.MutedS);
            GUILayout.Label(eco ? $"Argent {GameState.Money}$   ·   XP {GameState.Exp}   ·   Niv {GameState.Level}" : "éco indisponible", Theme.MutedS);
            GUILayout.Space(6);

            GUILayout.Label("ARGENT", Theme.SectionLabel);
            GUILayout.BeginHorizontal();
            if (Btn("+1k")) Trainer.AddMoney(1_000);
            if (Btn("+10k")) Trainer.AddMoney(10_000);
            if (Btn("+100k")) Trainer.AddMoney(100_000);
            if (Btn("+1M")) Trainer.AddMoney(1_000_000);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("XP  ·  FERRAILLE", Theme.SectionLabel);
            GUILayout.BeginHorizontal();
            if (Btn("+1k XP")) Trainer.AddExp(1_000);
            if (Btn("+10k XP")) Trainer.AddExp(10_000);
            if (Btn("+1k ferr.")) Trainer.AddScraps(1_000);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("VOITURE ACTIVE", Theme.SectionLabel);
            if (BtnWide("VOITURE PARFAITE (condition 100%)")) Trainer.PerfectCar();
            GUILayout.Space(3);
            if (BtnWide("RÉPARER + FLUIDES + NETTOYER")) Trainer.RepairAndClean();

            GUILayout.Space(10);
            GUILayout.Label("DÉBLOCAGE", Theme.SectionLabel);
            if (BtnWide("DÉBLOQUER TOUT (améliorations/tuning)")) Trainer.UnlockEverything();


            GUILayout.Space(10);
            GUILayout.Label("Astuce : certains cheats « debug » (réparer/débloquer) ne sont\ndisponibles que si le jeu embarque ses composants de debug.", Theme.MutedS);

            GUILayout.EndArea();
        }

        private static bool Btn(string t) => GUILayout.Button(t, Theme.Btn, GUILayout.Height(30));
        private static bool BtnWide(string t) => GUILayout.Button(t, Theme.BtnPrimary, GUILayout.Height(32));
    }
}
