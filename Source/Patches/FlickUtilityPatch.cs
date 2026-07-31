using HarmonyLib;
using RimWorld;
using Verse;

namespace RoomAutoLight
{
    /// <summary>
    /// The single hook the whole mod hangs off. Vanilla asks this before re-powering anything
    /// (PowerNet.PowerNetTick), before allowing PowerOn to be set true, and before lighting a
    /// glower, so answering "no" here holds the lamp dark and off the grid at once.
    /// </summary>
    [HarmonyPatch(typeof(FlickUtility), nameof(FlickUtility.WantsToBeOn))]
    public static class FlickUtility_WantsToBeOn_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __0, ref bool __result)
        {
            if (__result && LightSuppression.IsSuppressed(__0)) __result = false;
        }
    }
}
