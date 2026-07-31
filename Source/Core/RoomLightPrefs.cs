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
        /// <summary>Doors and occupancy drive the group.</summary>
        None,

        /// <summary>Celestial clock only. Ignores weather and eclipses, so dusk stays dusk.</summary>
        DuskToDawn,

        /// <summary>Actual sky glow, so an eclipse or a black storm at noon counts as dark.</summary>
        Darkness
    }

    /// <summary>Player choices for one group. Kept separate from the group so they can outlive a room rebuild.</summary>
    public class RoomLightPrefs : IExposable
    {
        public RoomLightMode mode = RoomLightMode.Auto;
        public LightSchedule schedule = LightSchedule.None;

        public RoomLightPrefs()
        {
        }

        public RoomLightPrefs(RoomLightMode mode, LightSchedule schedule)
        {
            this.mode = mode;
            this.schedule = schedule;
        }

        public bool IsDefault
        {
            get { return mode == RoomLightMode.Auto && schedule == LightSchedule.None; }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref mode, "mode", RoomLightMode.Auto);
            Scribe_Values.Look(ref schedule, "schedule", LightSchedule.None);
        }
    }
}
