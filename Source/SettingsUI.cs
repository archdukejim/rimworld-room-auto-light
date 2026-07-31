using UnityEngine;
using Verse;

namespace RoomAutoLight
{
    public static class SettingsUI
    {
        public static void Draw(Rect inRect, RoomAutoLightSettings s)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled("Enable room light automation", ref s.enabled,
                "Turn this off to hand every lamp back to vanilla control immediately.");
            listing.GapLine();

            listing.Label("Triggers");
            listing.CheckboxLabeled("An open door lights the room", ref s.doorOpenLightsOn,
                "Any door touching the room being physically open switches the whole group on.");
            listing.CheckboxLabeled("Ignore doors held open", ref s.ignoreHeldOpenDoors,
                "Off by default, so a hold-open door counts as open and keeps its rooms lit. Turn on if you pin doors open and still want those rooms to go dark.");
            listing.CheckboxLabeled("An occupant lights the room", ref s.occupancyLightsOn,
                "The room stays lit while anyone is inside, so a sealed bedroom is not pitch black.");
            listing.CheckboxLabeled("Animals count as occupants", ref s.animalsCountAsOccupants);
            listing.CheckboxLabeled("Sleeping pawns count as empty", ref s.sleepingPawnsCountAsEmpty,
                "Lets a room go dark once everyone inside is asleep.");
            listing.CheckboxLabeled("Stand down under grow lights", ref s.growLightAware,
                "In a grow room, ordinary lamps stay off while the sun lamp is lit and take over during its plant resting period, so nobody works in the dark.");

            listing.GapLine();
            listing.Label("Timing");
            listing.Label("Delay before going dark: " + (s.offDelayTicks / 60f).ToString("F1") + " s");
            s.offDelayTicks = Mathf.RoundToInt(listing.Slider(s.offDelayTicks, 0f, 1200f));
            listing.Label("Re-evaluation interval: " + s.updateIntervalTicks + " ticks");
            s.updateIntervalTicks = Mathf.RoundToInt(listing.Slider(s.updateIntervalTicks, 1f, 250f));
            listing.Label("Rooms are spread across this window. Lamps within one room always switch on the same tick.");

            listing.GapLine();
            listing.Label("Schedules");
            listing.Label("Dusk to dawn: night begins at celestial glow " + s.duskGlowThreshold.ToString("F2"));
            s.duskGlowThreshold = Mathf.Round(listing.Slider(s.duskGlowThreshold, 0.05f, 0.95f) * 20f) / 20f;
            listing.Label("0.60 matches vanilla's own day/night cutoff. Celestial glow ignores weather, so this tracks the clock.");
            listing.Label("Darkness: dark begins at sky glow " + s.darknessGlowThreshold.ToString("F2"));
            s.darknessGlowThreshold = Mathf.Round(listing.Slider(s.darknessGlowThreshold, 0.05f, 0.95f) * 20f) / 20f;
            listing.Label("Sky glow includes weather and eclipses, so a black storm at noon counts as dark.");

            listing.GapLine();
            listing.Label("Scope");
            listing.CheckboxLabeled("Manage outdoor lamps as one group", ref s.manageOutdoorLamps,
                "Every player-owned lamp standing outdoors is driven as a single switch, set to dusk-to-dawn by default.");
            listing.CheckboxLabeled("Show the room switch on lamps", ref s.showGizmo);
            listing.Label("Largest managed lamp: " + s.maxManagedWatts.ToString("F0") + " W");
            s.maxManagedWatts = Mathf.Round(listing.Slider(s.maxManagedWatts, 50f, 5000f) / 50f) * 50f;

            listing.Label("Extra defNames to manage (comma separated)");
            string included = listing.TextEntry(s.includedDefNamesRaw, 2);
            if (included != s.includedDefNamesRaw)
            {
                s.includedDefNamesRaw = included;
                s.NotifyDefFiltersChanged();
            }

            listing.Label("defNames to never manage (comma separated)");
            string excluded = listing.TextEntry(s.excludedDefNamesRaw, 2);
            if (excluded != s.excludedDefNamesRaw)
            {
                s.excludedDefNamesRaw = excluded;
                s.NotifyDefFiltersChanged();
            }

            listing.Label("Extra grow light defNames (comma separated)");
            string growLights = listing.TextEntry(s.growLightDefNamesRaw, 2);
            if (growLights != s.growLightDefNamesRaw)
            {
                s.growLightDefNamesRaw = growLights;
                s.NotifyDefFiltersChanged();
            }

            listing.End();
        }
    }
}
