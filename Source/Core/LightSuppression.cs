using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RoomAutoLight
{
    /// <summary>
    /// The one place that says "this light is currently held off by its group".
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

        /// <summary>
        /// While set, IsSuppressed answers false, so a group can ask vanilla what it would say
        /// about its own members if they were released. Only ever held across one synchronous
        /// check inside RoomLightGroup.
        /// </summary>
        public static bool BypassForProbe;

        public static bool IsSuppressed(Thing thing)
        {
            if (BypassForProbe) return false;
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

        /// <summary>Releases the hold. Does not power anything up; PowerUp does that.</summary>
        public static void Unsuppress(Building light)
        {
            if (light == null) return;
            if (!suppressed.Remove(light)) return;
            CompGlower glower = light.TryGetComp<CompGlower>();
            if (glower != null && light.Spawned) glower.UpdateLit(light.Map);
        }

        /// <summary>
        /// Powers a released lamp up immediately rather than waiting on PowerNet.PowerNetTick,
        /// which only restores about 5% of the waiting parts every 30-odd ticks and picks them at
        /// random. Left to vanilla, a room comes up one lamp at a time over several seconds.
        /// The conditions mirror the ones CompPowerTrader.PowerOn would otherwise warn about.
        /// </summary>
        public static void PowerUp(Building light)
        {
            if (light == null || !light.Spawned) return;
            CompPowerTrader power = light.TryGetComp<CompPowerTrader>();
            if (power == null || power.PowerOn || power.PowerNet == null) return;
            if (!FlickUtility.WantsToBeOn(light)) return;
            if (BreakdownableUtility.IsBrokenDown(light)) return;
            power.PowerOn = true;
        }

        /// <summary>
        /// Hands a lamp back to vanilla in the state vanilla would have left it: released and
        /// powered, rather than dark until the net's drip-feed happens to reach it.
        /// </summary>
        public static void Release(Building light)
        {
            Unsuppress(light);
            PowerUp(light);
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
                PowerUp(light);
            }
        }
    }
}
