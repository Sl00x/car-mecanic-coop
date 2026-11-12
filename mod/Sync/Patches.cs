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
    }

    // Player takes a part OFF the car. This — not PartScript.ActionUnMount — is the real removal entry
    // point in CMS2021 (confirmed via the game's own "[Inventory] -> Add() car-part" log). The part key
    // is a clean, machine-stable string ("car_vallsen2040-hood"). Two overloads: Void and IEnumerator.
    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.TakeOffCarPart), new Type[] { typeof(string) })]
    internal static class Patch_TakeOff
    {
        private static void Postfix(CarLoader __instance, string __0) { HookDiag.Log("TakeOffCarPart(str) " + __0); PartSync.OnLocalTakeOff(__instance, __0); }
    }

    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.TakeOffCarPart), new Type[] { typeof(string), typeof(bool) })]
    internal static class Patch_TakeOffCo
    {
        private static void Postfix(CarLoader __instance, string __0) { HookDiag.Log("TakeOffCarPart(str,bool) " + __0); PartSync.OnLocalTakeOff(__instance, __0); }
    }

    // Mechanical part MOUNTED (engine/suspension/other/driveshaft) — positional identity (CMS21-Together).
    [HarmonyPatch(typeof(PartScript), nameof(PartScript.DoMount))]
    internal static class Patch_DoMount
    {
        private static void Postfix(PartScript __instance) { HookDiag.Log("PartScript.DoMount"); CarPartSync.OnDoMount(__instance); }
    }

    // Mechanical part UNMOUNTED — the no-arg Hide() is the reliable unmount signal (CMS21-Together).
    [HarmonyPatch(typeof(PartScript), nameof(PartScript.Hide), new Type[0])]
    internal static class Patch_PartHide
    {
        private static void Postfix(PartScript __instance) { CarPartSync.OnHide(__instance); }
    }

    // A car is loaded -> replicate it so everyone sees the same vehicle on the lift.
    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.LoadCar))]
    internal static class Patch_LoadCar
    {
        private static void Postfix(CarLoader __instance, string name) => CarSync.OnLocalLoadCar(__instance, name);
    }

    // A car is removed (sold / job finished) -> propagate the garage change to guests.
    // CarLoader has two DeleteCar overloads — patch the no-arg one explicitly (empty Type[]) so the
    // match isn't ambiguous (an ambiguous match throws out of PatchAll and drops ALL patches).
    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.DeleteCar), new Type[0])]
    internal static class Patch_DeleteCar
    {
        private static void Postfix(CarLoader __instance) => CarSync.OnLocalLoadCar(__instance, null);
    }
}
