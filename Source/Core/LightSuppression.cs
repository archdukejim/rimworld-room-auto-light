using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RoomAutoLight
{
    /// <summary>
    /// The one place that says "this light is currently held off by its room group".
    ///
    /// Vanilla already routes every re-power decision through FlickUtility.WantsToBeOn:
    /// PowerNet.PowerNetTick skips comps that do not want to be on, CompPowerTrader.PowerOn
    /// refuses to be set true for them, and CompGlower.ShouldBeLitNow consults it too. So a
    /// single postfix on that method is enough to hold a lamp dark and off the grid, with no
    /// per-tick fighting and no leftover "needs power" overlay.
    /// </summary>
    public static class LightSuppression
    {
        private static readonly HashSet<Thing> suppressed = new HashSet<Thing>();

        public static bool IsSuppressed(Thing thing)
        {
            return suppressed.Count > 0 && suppressed.Contains(thing);
        }

        /// <summary>Cuts the light: suppress first, then drop power so the glower goes dark in the same call.</summary>
        public static void TurnOff(Building light)
        {
            if (light == null) return;
            suppressed.Add(light);
            CompPowerTrader power = light.TryGetComp<CompPowerTrader>();
            if (power != null && power.PowerOn) power.PowerOn = false;
        }

        /// <summary>
        /// Releases the light. PowerNet re-powers it on its next tick and the resulting
        /// PowerTurnedOn signal relights the glower, so the whole group comes up together.
        /// </summary>
        public static void TurnOn(Building light)
        {
            if (light == null) return;
            if (!suppressed.Remove(light)) return;
            CompGlower glower = light.TryGetComp<CompGlower>();
            if (glower != null && light.Spawned) glower.UpdateLit(light.Map);
        }

        public static void Release(Building light)
        {
            TurnOn(light);
        }

        public static void ReleaseAll()
        {
            if (suppressed.Count == 0) return;
            List<Thing> snapshot = new List<Thing>(suppressed);
            suppressed.Clear();
            for (int i = 0; i < snapshot.Count; i++)
            {
                Building light = snapshot[i] as Building;
                if (light == null || !light.Spawned) continue;
                CompGlower glower = light.TryGetComp<CompGlower>();
                if (glower != null) glower.UpdateLit(light.Map);
            }
        }
    }
}
