using Verse;

namespace RoomAutoLight
{
    public enum RoomLightMode
    {
        Auto,
        ForceOn,
        ForceOff
    }

    public enum LightSchedule
    {
        /// <summary>Occupancy drives the group.</summary>
        None,

        /// <summary>Celestial clock only. Ignores weather and eclipses, so dusk stays dusk.</summary>
        DuskToDawn,

        /// <summary>Actual sky glow, so an eclipse or a black storm at noon counts as dark.</summary>
        Darkness
    }

    /// <summary>How sleeping occupants affect a group. Never applies outdoors.</summary>
    public enum SleepDarkening
    {
        /// <summary>One sleeper is enough to send the room dark, even with others awake.</summary>
        IfAny,

        /// <summary>The room goes dark only once every occupant is asleep.</summary>
        IfAll,

        /// <summary>Sleepers count as ordinary occupants and keep the lights on.</summary>
        Never
    }

    /// <summary>Player choices for one group. Kept separate from the group so they can outlive a room rebuild.</summary>
    public class RoomLightPrefs : IExposable
    {
        public RoomLightMode mode = RoomLightMode.Auto;
        public LightSchedule schedule = LightSchedule.None;
        public SleepDarkening sleepDarkening = SleepDarkening.IfAll;

        public RoomLightPrefs()
        {
        }

        public RoomLightPrefs(RoomLightMode mode, LightSchedule schedule, SleepDarkening sleepDarkening)
        {
            this.mode = mode;
            this.schedule = schedule;
            this.sleepDarkening = sleepDarkening;
        }

        /// <summary>Default groups need no anchor, so they follow the global settings as they change.</summary>
        public bool IsDefault
        {
            get
            {
                return mode == RoomLightMode.Auto
                       && schedule == LightSchedule.None
                       && sleepDarkening == RoomAutoLightMod.Settings.defaultSleepDarkening;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref mode, "mode", RoomLightMode.Auto);
            Scribe_Values.Look(ref schedule, "schedule", LightSchedule.None);
            Scribe_Values.Look(ref sleepDarkening, "sleepDarkening", SleepDarkening.IfAll);
        }
    }
}
