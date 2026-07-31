using HarmonyLib;
using Verse;

namespace RoomAutoLight
{
    [HarmonyPatch(typeof(CompGlower), nameof(CompGlower.PostSpawnSetup))]
    public static class CompGlower_PostSpawnSetup_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CompGlower __instance)
        {
            Building building = __instance.parent as Building;
            if (building == null || building.Map == null) return;

            RoomLightManager manager = building.Map.GetComponent<RoomLightManager>();
            if (manager == null) return;

            if (RoomLightUtility.IsManagedLightDef(building.def)) manager.Register(building);
            else if (RoomLightUtility.IsGrowLightDef(building.def)) manager.RegisterGrowLight(building);
        }
    }

    [HarmonyPatch(typeof(CompGlower), nameof(CompGlower.PostDeSpawn))]
    public static class CompGlower_PostDeSpawn_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CompGlower __instance, Map __0)
        {
            Building building = __instance.parent as Building;
            if (building == null || __0 == null) return;

            RoomLightManager manager = __0.GetComponent<RoomLightManager>();
            if (manager == null) return;

            manager.Unregister(building);
            manager.UnregisterGrowLight(building);
        }
    }
}
