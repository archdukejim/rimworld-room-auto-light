using HarmonyLib;
using RimWorld;
using Verse;

namespace RoomAutoLight
{
    /// <summary>
    /// Doors drive the headline rule, so they get an event rather than waiting for the next
    /// polling slot. Both hooks only queue the affected rooms; the switch still happens in
    /// MapComponentTick so the whole group moves together.
    /// </summary>
    [HarmonyPatch(typeof(Building_Door), "DoorOpen")]
    public static class Building_Door_DoorOpen_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Building_Door __instance)
        {
            Notify(__instance);
        }

        internal static void Notify(Building_Door door)
        {
            if (door == null || door.Map == null) return;
            RoomLightManager manager = door.Map.GetComponent<RoomLightManager>();
            if (manager != null) manager.NotifyDoorChanged(door);
        }
    }

    [HarmonyPatch(typeof(Building_Door), "DoorTryClose")]
    public static class Building_Door_DoorTryClose_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Building_Door __instance, bool __result)
        {
            if (__result) Building_Door_DoorOpen_Patch.Notify(__instance);
        }
    }
}
