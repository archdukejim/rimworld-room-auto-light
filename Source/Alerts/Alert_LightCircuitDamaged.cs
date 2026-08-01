using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RoomAutoLight
{
    /// <summary>
    /// AlertsReadout finds every Alert subclass by reflection at startup, so this needs no def and
    /// no registration. It is polled on a stagger, hence reading each map's cached list rather than
    /// walking every registered lamp.
    /// </summary>
    public class Alert_LightCircuitDamaged : Alert
    {
        private readonly List<Thing> culprits = new List<Thing>();

        public Alert_LightCircuitDamaged()
        {
            defaultLabel = "Lighting circuit damaged";
            defaultPriority = AlertPriority.Medium;
        }

        private List<Thing> Culprits()
        {
            culprits.Clear();
            if (!RoomAutoLightMod.Settings.enabled) return culprits;
            if (!RoomAutoLightMod.Settings.breakOnRoomChange) return culprits;

            List<Map> maps = Find.Maps;
            if (maps == null) return culprits;

            for (int i = 0; i < maps.Count; i++)
            {
                RoomLightManager manager = maps[i].GetComponent<RoomLightManager>();
                if (manager == null) continue;
                culprits.AddRange(manager.BrokenLights);
            }
            return culprits;
        }

        public override AlertReport GetReport()
        {
            return AlertReport.CulpritsAre(Culprits());
        }

        public override string GetLabel()
        {
            int count = culprits.Count;
            if (count <= 1) return "Lighting circuit damaged";
            return "Lighting circuits damaged (" + count + ")";
        }

        public override TaggedString GetExplanation()
        {
            int count = culprits.Count;
            string lamps = count == 1 ? "One lamp has" : count + " lamps have";

            return lamps + " lost their room. A wall was blown out, deconstructed or built, so the"
                   + " boundary the lighting circuit was wired around no longer holds, and the lamps"
                   + " have dropped."
                   + "\n\nSelect any one of them and use Reset lighting circuit. That repairs every"
                   + " affected lamp in the room at once and re-forms the group around the room as"
                   + " it now stands.";
        }
    }
}
