using HarmonyLib;
using Verse;

namespace RoomAutoLight
{
    /// <summary>Reports the room as one load rather than making the player add lamps up by hand.</summary>
    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.GetInspectString))]
    public static class ThingWithComps_GetInspectString_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingWithComps __instance, ref string __result)
        {
            if (!RoomAutoLightMod.Settings.enabled) return;

            Building light = __instance as Building;
            if (light == null || light.Map == null) return;
            if (!RoomLightUtility.IsManagedLightDef(light.def)) return;

            RoomLightManager manager = light.Map.GetComponent<RoomLightManager>();
            if (manager == null) return;

            RoomLightGroup group = manager.GroupFor(light);
            if (group == null) return;

            if (!__result.NullOrEmpty()) __result += "\n";
            __result += group.StatusLine();
        }
    }
}
