using System;
using HarmonyLib;

namespace CarMecaSim.Mp.Sync
{
    // Player installs a part -> replicate + lock. Return false to block (part used by someone else).
    [HarmonyPatch(typeof(PartScript), nameof(PartScript.ActionMount), new Type[] { typeof(bool) })]
    internal static class Patch_ActionMount
    {
        private static bool Prefix(PartScript __instance) => PartSync.TryLocalPart(__instance, PartSync.ActMount);
    }

    // Player removes a part.
    [HarmonyPatch(typeof(PartScript), nameof(PartScript.ActionUnMount))]
    internal static class Patch_ActionUnMount
    {
        private static bool Prefix(PartScript __instance) => PartSync.TryLocalPart(__instance, PartSync.ActUnmount);
    }

    // Player turns a bolt one step.
    [HarmonyPatch(typeof(MountObject), nameof(MountObject.Action))]
    internal static class Patch_Bolt
    {
        private static bool Prefix(MountObject __instance) => PartSync.TryLocalBolt(__instance);
    }

    // A car is loaded -> replicate it so everyone sees the same vehicle on the lift.
    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.LoadCar))]
    internal static class Patch_LoadCar
    {
        private static void Postfix(CarLoader __instance, string name) => CarSync.OnLocalLoadCar(__instance, name);
    }
}
