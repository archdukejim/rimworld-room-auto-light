using HarmonyLib;
using Verse;

namespace RoomAutoLight
{
    /// <summary>Walls moving means rooms are renumbered, so the light-to-room mapping is stale.</summary>
    [HarmonyPatch(typeof(Room), nameof(Room.Notify_RoomShapeChanged))]
    public static class Room_Notify_RoomShapeChanged_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Room __instance)
        {
            Map map;
            try
            {
                if (__instance.Districts.NullOrEmpty()) return;
                map = __instance.Map;
            }
            catch
            {
                return;
            }
            if (map == null) return;

            RoomLightManager manager = map.GetComponent<RoomLightManager>();
            if (manager != null) manager.MarkDirty();
        }
    }
}
