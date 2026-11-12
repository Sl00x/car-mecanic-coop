using System;
using HarmonyLib;

namespace CarMecaSim.Mp.Sync
{
    /// <summary>Temporary diagnostics: logs each part/bolt hook firing (counter-capped) so we can see
    /// exactly which game method a user's bolt-turn / part-removal actually calls.</summary>
    internal static class HookDiag
    {
        private static int _n;
        public static void Log(string what)
        {
            if (_n++ < 40) CarMecaPlugin.Logger?.LogInfo($"[hook] {what}");
        }

        // CRITICAL: a Harmony postfix runs INSIDE the patched game method. If it throws, the exception
        // propagates into the game — e.g. a throw inside the CarLoader.LoadCar postfix kills the save-load
        // coroutine (the "stuck at 80%" hang). Every postfix body MUST go through Safe() so a mod bug can
        // never break the game; we just log (rate-limited) and carry on.
        private static int _err;
        public static void Safe(string tag, Action body)
        {
            try { body(); }
            catch (Exception e)
            {
                if (_err++ < 20)
                    CarMecaPlugin.Logger?.LogWarning($"[hook] {tag} threw (swallowed; game continues): {e.Message}");
            }
        }
    }

    // Player takes a part OFF the car. This — not PartScript.ActionUnMount — is the real removal entry
    // point in CMS2021 (confirmed via the game's own "[Inventory] -> Add() car-part" log). The part key
    // is a clean, machine-stable string ("car_vallsen2040-hood"). Two overloads: Void and IEnumerator.
    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.TakeOffCarPart), new Type[] { typeof(string) })]
    internal static class Patch_TakeOff
    {
        private static void Postfix(CarLoader __instance, string __0) => HookDiag.Safe("TakeOffCarPart(str)", () => { HookDiag.Log("TakeOffCarPart(str) " + __0); PartSync.OnLocalTakeOff(__instance, __0); });
    }

    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.TakeOffCarPart), new Type[] { typeof(string), typeof(bool) })]
    internal static class Patch_TakeOffCo
    {
        private static void Postfix(CarLoader __instance, string __0) => HookDiag.Safe("TakeOffCarPart(str,bool)", () => { HookDiag.Log("TakeOffCarPart(str,bool) " + __0); PartSync.OnLocalTakeOff(__instance, __0); });
    }

    // Mechanical part MOUNTED (engine/suspension/other/driveshaft) — positional identity (CMS21-Together).
    [HarmonyPatch(typeof(PartScript), nameof(PartScript.DoMount))]
    internal static class Patch_DoMount
    {
        private static void Postfix(PartScript __instance) => HookDiag.Safe("PartScript.DoMount", () => { HookDiag.Log("PartScript.DoMount"); CarPartSync.OnDoMount(__instance); });
    }

    // Mechanical part UNMOUNTED — the no-arg Hide() is the reliable unmount signal (CMS21-Together).
    [HarmonyPatch(typeof(PartScript), nameof(PartScript.Hide), new Type[0])]
    internal static class Patch_PartHide
    {
        private static void Postfix(PartScript __instance) => HookDiag.Safe("PartScript.Hide", () => CarPartSync.OnHide(__instance));
    }

    // Openable (hood / trunk / engine_cover / clamshell) opened or closed -> sync open/close state.
    // The 2-arg SwitchCarPart(CarPart, bool) is the animated path a player's click uses (the 3-arg overload
    // SwitchCarPart(CarPart, bool animate, bool switched) is the no-animation setter we use to APPLY).
    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.SwitchCarPart), new Type[] { typeof(CarPart), typeof(bool) })]
    internal static class Patch_Switch
    {
        private static void Postfix(CarLoader __instance, CarPart __0, bool __1) => HookDiag.Safe("SwitchCarPart", () => PartSync.OnLocalSwitch(__instance, __0, __1));
    }

    // A car is loaded -> replicate it so everyone sees the same vehicle on the lift.
    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.LoadCar))]
    internal static class Patch_LoadCar
    {
        private static void Postfix(CarLoader __instance, string name) => HookDiag.Safe("LoadCar", () => CarSync.OnLocalLoadCar(__instance, name));
    }

    // A car is removed (sold / job finished) -> propagate the garage change to guests.
    // CarLoader has two DeleteCar overloads — patch the no-arg one explicitly (empty Type[]) so the
    // match isn't ambiguous (an ambiguous match throws out of PatchAll and drops ALL patches).
    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.DeleteCar), new Type[0])]
    internal static class Patch_DeleteCar
    {
        private static void Postfix(CarLoader __instance) => HookDiag.Safe("DeleteCar", () => CarSync.OnLocalLoadCar(__instance, null));
    }
}
