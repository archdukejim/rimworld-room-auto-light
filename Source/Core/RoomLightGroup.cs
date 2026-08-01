using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RoomAutoLight
{
    /// <summary>
    /// A set of lamps driven as a single switch. Normally that is one room; the map also carries
    /// one synthetic group holding every player-owned lamp standing outdoors, since the outdoors
    /// is a single sprawling "room" that nobody wants automated cell by cell.
    ///
    /// All members flip on the same tick; groups are staggered against each other so the work is
    /// spread without ever splitting a room across ticks.
    /// </summary>
    public class RoomLightGroup
    {
        public const int OutdoorGroupId = int.MinValue;

        public readonly int roomId;
        public readonly bool isOutdoor;
        public readonly Map map;
        public Room room;
        public readonly List<Building> lights = new List<Building>();

        /// <summary>Sun lamps and the like sharing this room. Not switched, only watched.</summary>
        public readonly List<Building> growLights = new List<Building>();

        public RoomLightMode mode = RoomLightMode.Auto;
        public LightSchedule schedule = LightSchedule.None;
        public SleepDarkening sleepDarkening = SleepDarkening.IfAll;
        public TriggerLink link = TriggerLink.Combined;

        private bool lit = true;
        private int darkAtTick = -1;
        private string reason = "";

        public RoomLightGroup(Map map, Room room)
        {
            this.map = map;
            this.room = room;
            roomId = room.ID;
            isOutdoor = false;
        }

        public RoomLightGroup(Map map)
        {
            this.map = map;
            room = null;
            roomId = OutdoorGroupId;
            isOutdoor = true;
            schedule = LightSchedule.Darkness;
        }

        public bool Lit { get { return lit; } }
        public int LightCount { get { return lights.Count; } }
        public string Reason { get { return reason; } }

        public float PotentialWatts
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < lights.Count; i++)
                {
                    CompProperties_Power props = lights[i].def.GetCompProperties<CompProperties_Power>();
                    if (props != null) total += props.PowerConsumption;
                }
                return total;
            }
        }

        public float CurrentWatts
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < lights.Count; i++)
                {
                    CompPowerTrader power = lights[i].TryGetComp<CompPowerTrader>();
                    if (power != null && power.PowerOn) total += -power.PowerOutput;
                }
                return total;
            }
        }

        public void Evaluate(int now, bool occupied, bool sleepersPresent)
        {
            // A wall going up mid-tick can leave the cached room behind; the next rebuild fixes it.
            if (!isOutdoor && (room == null || room.Dereferenced)) return;

            RoomAutoLightSettings settings = RoomAutoLightMod.Settings;
            bool want = ComputeWant(occupied, sleepersPresent, settings);

            if (want)
            {
                darkAtTick = -1;
                Apply(true);
                return;
            }

            if (lit)
            {
                if (darkAtTick < 0) darkAtTick = now + settings.offDelayTicks;
                if (now < darkAtTick)
                {
                    Apply(true);
                    return;
                }
            }

            darkAtTick = -1;
            Apply(false);
        }

        private bool ComputeWant(bool occupied, bool sleepersPresent, RoomAutoLightSettings settings)
        {
            if (mode == RoomLightMode.ForceOff)
            {
                reason = "forced off";
                return false;
            }
            if (mode == RoomLightMode.ForceOn)
            {
                reason = "forced on";
                return true;
            }

            // A lit sun lamp floods the room far past anything a 30 W lamp adds, so the group
            // stands down and takes back over the moment the sun lamp hits its resting period.
            // That gap is where the darkness penalty would otherwise bite.
            if (settings.growLightAware && AnyGrowLightLit())
            {
                reason = "grow light covering";
                return false;
            }

            // A schedule is not a trigger: while one is set, it is the only thing that decides.
            // That is what makes it usable for perimeter lighting.
            if (schedule == LightSchedule.DuskToDawn)
            {
                bool night = RoomLightUtility.IsNight(map);
                reason = night ? "after dusk" : "daylight";
                return night;
            }
            if (schedule == LightSchedule.Darkness)
            {
                bool dark = RoomLightUtility.IsDark(map);
                reason = dark ? "dark outside" : "light outside";
                return dark;
            }

            // Every door on the map touches the outdoors, so the door trigger is meaningless there.
            if (!isOutdoor && link != TriggerLink.Occupied && RoomLightUtility.AnyOpenDoor(room))
            {
                reason = "door open";
                return true;
            }
            if (link != TriggerLink.Doors && occupied)
            {
                reason = "occupied";
                return true;
            }

            if (isOutdoor) reason = "nobody outside";
            else if (link == TriggerLink.Doors) reason = "doors closed";
            else if (sleepersPresent) reason = "occupants asleep";
            else if (link == TriggerLink.Occupied) reason = "empty";
            else reason = "closed and empty";
            return false;
        }

        private bool AnyGrowLightLit()
        {
            for (int i = 0; i < growLights.Count; i++)
            {
                Building growLight = growLights[i];
                if (growLight == null || !growLight.Spawned) continue;
                CompGlower glower = growLight.TryGetComp<CompGlower>();
                if (glower != null && glower.Glows) return true;
            }
            return false;
        }

        /// <summary>
        /// One pass over the members, so every lamp in the group changes state on the same tick.
        /// Both directions are idempotent, which also picks up lamps added since the last evaluation.
        /// </summary>
        public void Apply(bool on)
        {
            lit = on;
            for (int i = 0; i < lights.Count; i++)
            {
                if (on) LightSuppression.TurnOn(lights[i]);
                else LightSuppression.TurnOff(lights[i]);
            }
        }

        public void ReleaseAll()
        {
            lit = true;
            darkAtTick = -1;
            for (int i = 0; i < lights.Count; i++) LightSuppression.Release(lights[i]);
        }

        public string ModeLabel
        {
            get
            {
                if (mode == RoomLightMode.ForceOn) return "always on";
                if (mode == RoomLightMode.ForceOff) return "always off";
                if (schedule == LightSchedule.DuskToDawn) return "dusk to dawn";
                if (schedule == LightSchedule.Darkness) return "on darkness";
                return "auto";
            }
        }

        public string StatusLine()
        {
            string state = lit ? "lit" : "dark";
            float watts = lit ? CurrentWatts : PotentialWatts;
            string wattLabel = lit ? watts.ToString("F0") + " W" : watts.ToString("F0") + " W idle";
            string title = isOutdoor ? "Outdoor lights" : "Room lights";

            return title + " (" + ModeLabel + "): " + lights.Count + " "
                   + (lights.Count == 1 ? "lamp" : "lamps") + ", " + state + " - "
                   + reason + " (" + wattLabel + ")";
        }
    }
}
