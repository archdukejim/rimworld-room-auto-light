using System.Collections.Generic;
using Verse;

namespace RoomAutoLight
{
    public class RoomAutoLightSettings : ModSettings
    {
        // Master switch. When turned off every suppressed light is released back to vanilla control.
        public bool enabled = true;

        // Trigger rules. A room is lit when any enabled trigger says so.
        public bool doorOpenLightsOn = true;
        public bool occupancyLightsOn = true;
        public bool ignoreHeldOpenDoors = false;
        public bool animalsCountAsOccupants = false;
        public bool sleepingPawnsCountAsEmpty = false;

        // A room only goes dark once it has been unlit-worthy for this long, so a pawn
        // walking through does not strobe the whole group.
        public int offDelayTicks = 240;

        // How often a given room re-evaluates. Rooms are spread across this window so that
        // every lamp inside one room still flips on the exact same tick.
        public int updateIntervalTicks = 60;

        // Grow rooms: while a sun lamp is lit it drowns out any ordinary lamp, so the group
        // stands down and takes over once the sun lamp hits its plant resting period.
        public bool growLightAware = true;

        // Vanilla's sun lamp carries overlightRadius 7; the ritual props that also use the field
        // (LightBall 2.0, Loudspeaker 1.5) sit well below this.
        public float growLightMinOverlight = 4f;

        // Every player-owned lamp standing outdoors forms one group, rather than trying to
        // automate the map-sized outdoor "room" cell by cell. It defaults to dusk-to-dawn.
        public bool manageOutdoorLamps = true;

        // Celestial glow at or below this counts as night for the dusk-to-dawn schedule. 0.6 is
        // vanilla's own day/night cutoff (GenCelestial.IsDaytime).
        public float duskGlowThreshold = 0.6f;

        // Sky glow at or below this counts as dark for the darkness schedule. Sky glow includes
        // weather and game conditions, so an eclipse trips this and dusk-to-dawn would not.
        public float darknessGlowThreshold = 0.5f;

        public bool showGizmo = true;

        // Anything drawing more than this is treated as industrial equipment, not a room lamp.
        public float maxManagedWatts = 1000f;

        // defName overrides for modded lamps that use a custom thingClass, or vanilla-shaped
        // things that should be left alone.
        public string includedDefNamesRaw = "";
        public string excludedDefNamesRaw = "";

        // Modded plant lights that the overlight heuristic misses.
        public string growLightDefNamesRaw = "";

        private HashSet<string> includedCache;
        private HashSet<string> excludedCache;
        private HashSet<string> growLightCache;

        public HashSet<string> IncludedDefNames
        {
            get
            {
                if (includedCache == null) includedCache = ParseDefNames(includedDefNamesRaw);
                return includedCache;
            }
        }

        public HashSet<string> ExcludedDefNames
        {
            get
            {
                if (excludedCache == null) excludedCache = ParseDefNames(excludedDefNamesRaw);
                return excludedCache;
            }
        }

        public HashSet<string> GrowLightDefNames
        {
            get
            {
                if (growLightCache == null) growLightCache = ParseDefNames(growLightDefNamesRaw);
                return growLightCache;
            }
        }

        public void NotifyDefFiltersChanged()
        {
            includedCache = null;
            excludedCache = null;
            growLightCache = null;
            RoomLightUtility.ClearDefCache();
        }

        private static HashSet<string> ParseDefNames(string raw)
        {
            HashSet<string> set = new HashSet<string>();
            if (raw.NullOrEmpty()) return set;
            string[] parts = raw.Split(',', ';', '\n');
            for (int i = 0; i < parts.Length; i++)
            {
                string trimmed = parts[i].Trim();
                if (trimmed.Length > 0) set.Add(trimmed);
            }
            return set;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref doorOpenLightsOn, "doorOpenLightsOn", true);
            Scribe_Values.Look(ref occupancyLightsOn, "occupancyLightsOn", true);
            Scribe_Values.Look(ref ignoreHeldOpenDoors, "ignoreHeldOpenDoors", false);
            Scribe_Values.Look(ref animalsCountAsOccupants, "animalsCountAsOccupants", false);
            Scribe_Values.Look(ref sleepingPawnsCountAsEmpty, "sleepingPawnsCountAsEmpty", false);
            Scribe_Values.Look(ref offDelayTicks, "offDelayTicks", 240);
            Scribe_Values.Look(ref updateIntervalTicks, "updateIntervalTicks", 60);
            Scribe_Values.Look(ref manageOutdoorLamps, "manageOutdoorLamps", true);
            Scribe_Values.Look(ref duskGlowThreshold, "duskGlowThreshold", 0.6f);
            Scribe_Values.Look(ref darknessGlowThreshold, "darknessGlowThreshold", 0.5f);
            Scribe_Values.Look(ref showGizmo, "showGizmo", true);
            Scribe_Values.Look(ref maxManagedWatts, "maxManagedWatts", 1000f);
            Scribe_Values.Look(ref includedDefNamesRaw, "includedDefNamesRaw", "");
            Scribe_Values.Look(ref excludedDefNamesRaw, "excludedDefNamesRaw", "");
            Scribe_Values.Look(ref growLightAware, "growLightAware", true);
            Scribe_Values.Look(ref growLightMinOverlight, "growLightMinOverlight", 4f);
            Scribe_Values.Look(ref growLightDefNamesRaw, "growLightDefNamesRaw", "");

            if (includedDefNamesRaw == null) includedDefNamesRaw = "";
            if (excludedDefNamesRaw == null) excludedDefNamesRaw = "";
            if (growLightDefNamesRaw == null) growLightDefNamesRaw = "";
            if (updateIntervalTicks < 1) updateIntervalTicks = 1;
            if (offDelayTicks < 0) offDelayTicks = 0;
            NotifyDefFiltersChanged();
        }
    }
}
