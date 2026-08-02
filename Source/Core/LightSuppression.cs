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

        /// <summary>
        /// Cuts the light: suppress first, then drop power so the glower goes dark in the same call.
        /// Returns true if this actually changed the lamp, which is what separates a real
        /// transition from an idempotent re-application.
        /// </summary>
        public static bool TurnOff(Building light)
        {
            if (light == null) return false;
            suppressed.Add(light);
            CompPowerTrader power = light.TryGetComp<CompPowerTrader>();
            if (power == null || !power.PowerOn) return false;
            SetPowerQuietly(power, false);
            return true;
        }

        /// <summary>
        /// Every power change this mod makes goes through here, so the vanilla power click is
        /// muted no matter which path caused it - a group switch, a rebuild handing a lamp back,
        /// an ungroup, a circuit reset, or the mod being switched off. Scoping this to the group
        /// switch alone left the other paths still clicking.
        ///
        /// A lamp the player flicks by hand is untouched: vanilla re-powers that through
        /// PowerNetTick, which never calls this.
        /// </summary>
        private static void SetPowerQuietly(CompPowerTrader power, bool on)
        {
            bool previous = SwitchSoundSilencer.Active;
            SwitchSoundSilencer.Active = true;
            try
            {
                power.PowerOn = on;
            }
            finally
            {
                SwitchSoundSilencer.Active = previous;
            }
        }

        /// <summary>Releases the hold. Does not power anything up; PowerUp does that.</summary>
        public static bool Unsuppress(Building light)
        {
            if (light == null) return false;
            if (!suppressed.Remove(light)) return false;
            CompGlower glower = light.TryGetComp<CompGlower>();
            if (glower != null && light.Spawned) glower.UpdateLit(light.Map);
            return true;
        }

        /// <summary>
        /// Powers a released lamp up immediately rather than waiting on PowerNet.PowerNetTick,
        /// which only restores about 5% of the waiting parts every 30-odd ticks and picks them at
        /// random. Left to vanilla, a room comes up one lamp at a time over several seconds.
        /// The conditions mirror the ones CompPowerTrader.PowerOn would otherwise warn about.
        /// </summary>
        public static bool PowerUp(Building light)
        {
            if (light == null || !light.Spawned) return false;
            CompPowerTrader power = light.TryGetComp<CompPowerTrader>();
            if (power == null || power.PowerOn || power.PowerNet == null) return false;
            if (!FlickUtility.WantsToBeOn(light)) return false;
            if (BreakdownableUtility.IsBrokenDown(light)) return false;
            SetPowerQuietly(power, true);
            return true;
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
