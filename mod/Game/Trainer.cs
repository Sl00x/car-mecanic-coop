using System;
using UnityEngine;
using CarMecaSim.Mp.Core;

namespace CarMecaSim.Mp.Game
{
    /// <summary>
    /// Host "mega-trainer" actions, driven from the F8 overlay. The reliable cheats use the game's public
    /// static economy API (GlobalData, via <see cref="GameState"/>) and <c>CarLoader.SetConditionAll</c>, so
    /// they always work. The rest (repair/fluids/clean, unlock-all) call the game's own DEBUG components
    /// (CarDebug / UpgradeSystem / TestScript) if they exist in the scene — in a release build they may be
    /// absent, so every call is best-effort and logs "non disponible" instead of throwing. Money/XP changes
    /// propagate to guests automatically through the existing host-authoritative economy sync.
    /// </summary>
    public static class Trainer
    {
        private static void Log(string m) => CarMecaPlugin.Logger?.LogInfo("[trainer] " + m);
        private static void Warn(string m) => CarMecaPlugin.Logger?.LogWarning("[trainer] " + m);

        // ---- economy (always available) ----
        public static void AddMoney(int d) { try { GameState.AddMoney(d); Log($"+{d}$ -> {GameState.Money}"); } catch (Exception e) { Warn("money: " + e.Message); } }
        public static void AddExp(int d)   { try { GameState.AddExp(d);   Log($"+{d}xp -> {GameState.Exp}"); } catch (Exception e) { Warn("xp: " + e.Message); } }
        public static void AddScraps(int d){ try { GameState.AddScraps(d);Log($"+{d} ferraille -> {GameState.Scraps}"); } catch (Exception e) { Warn("scraps: " + e.Message); } }

        private static CarLoader ActiveCar()
        {
            try { var c = CarRefs.CarWithParts(); if (c != null) return c; } catch { }
            try { return CarRefs.LoadedCar(); } catch { return null; }
        }

        /// <summary>Set every part on the active car to perfect condition (reliable: CarLoader.SetConditionAll).</summary>
        public static void PerfectCar()
        {
            var cl = ActiveCar();
            if (cl == null) { Warn("voiture parfaite: aucune voiture active"); return; }
            try { cl.SetConditionAll(1f); Log("voiture -> condition 100%"); }
            catch (Exception e) { Warn("SetConditionAll: " + e.Message); }
        }

        /// <summary>Repair + refill fluids + de-dust via the game's CarDebug (if present in the scene).</summary>
        public static void RepairAndClean()
        {
            CarDebug dbg = null;
            try { dbg = UnityEngine.Object.FindObjectOfType<CarDebug>(); } catch { }
            if (dbg == null) { Warn("réparer/fluides: CarDebug absent (build release) — utilise 'Voiture parfaite'"); PerfectCar(); return; }
            try { dbg.RepairAll(); Log("CarDebug.RepairAll"); } catch (Exception e) { Warn("RepairAll: " + e.Message); }
            try { dbg.RefillFluids(); Log("CarDebug.RefillFluids"); } catch (Exception e) { Warn("RefillFluids: " + e.Message); }
            try { dbg.SetDust(); Log("CarDebug.SetDust (anti-poussière)"); } catch (Exception e) { Warn("SetDust: " + e.Message); }
        }

        /// <summary>Unlock all upgrades / parking / tuning via the game's debug managers (best-effort).</summary>
        public static void UnlockEverything()
        {
            int ok = 0;
            try { var up = UnityEngine.Object.FindObjectOfType<UpgradeSystem>(); if (up != null) { up.UnlockAll(true); ok++; Log("UpgradeSystem.UnlockAll"); } } catch (Exception e) { Warn("UpgradeSystem: " + e.Message); }
            try { var ts = UnityEngine.Object.FindObjectOfType<TestScript>(); if (ts != null) { try { ts.UnlockAllUpgrades(); ok++; } catch { } try { ts.UnlockAllParkingLevels(); ok++; } catch { } try { ts.AddAllTuningParts(); ok++; } catch { } try { ts.AddAllTuningBodyParts(); ok++; } catch { } Log("TestScript unlocks"); } } catch (Exception e) { Warn("TestScript: " + e.Message); }
            if (ok == 0) Warn("débloquer tout: composants debug absents (build release)");
        }
    }
}
