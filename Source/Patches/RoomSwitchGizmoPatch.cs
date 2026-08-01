using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RoomAutoLight
{
    /// <summary>
    /// The aggregated switch. Selecting any lamp exposes one control for its whole group;
    /// selecting several lamps from the same group collapses into that same control, because
    /// the gizmos share a group key.
    /// </summary>
    [HarmonyPatch(typeof(Building), nameof(Building.GetGizmos))]
    public static class Building_GetGizmos_Patch
    {
        private const int ModeKeyBase = 84710000;
        private const int ScheduleKeyBase = 84720000;
        private const int SleepKeyBase = 84730000;
        private const int LinkKeyBase = 84740000;

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Building __instance)
        {
            foreach (Gizmo gizmo in values) yield return gizmo;

            RoomAutoLightSettings settings = RoomAutoLightMod.Settings;
            if (!settings.enabled || !settings.showGizmo) yield break;
            if (__instance.Map == null || !RoomLightUtility.IsManagedLightDef(__instance.def)) yield break;

            RoomLightManager manager = __instance.Map.GetComponent<RoomLightManager>();
            if (manager == null) yield break;

            RoomLightGroup group = manager.GroupFor(__instance);
            if (group == null) yield break;

            Building light = __instance;
            string scope = group.isOutdoor ? "outdoor lights" : "room lights";

            Command_Action mode = new Command_Action();
            mode.defaultLabel = (group.isOutdoor ? "Outdoor lights: " : "Room lights: ") + group.ModeLabel;
            mode.defaultDesc = group.StatusLine()
                + "\n\nSwitches every lamp in this " + (group.isOutdoor ? "map's outdoors" : "room")
                + " as one. Cycles automatic, always on, always off."
                + "\n\nWhile off, the group draws nothing from the grid.";
            mode.icon = light.def.uiIcon;
            mode.defaultIconColor = group.Lit ? new Color(1f, 0.92f, 0.6f) : new Color(0.55f, 0.55f, 0.6f);
            mode.groupKey = ModeKeyBase ^ group.roomId;
            mode.action = delegate { manager.SetMode(group, NextMode(group.mode), light); };
            yield return mode;

            RoomLightGroup scheduleGroup = group;
            Command_Action schedule = new Command_Action();
            schedule.defaultLabel = "Schedule: " + ScheduleLabel(group.schedule);
            schedule.defaultDesc = ScheduleDesc(group.schedule, scope, settings)
                + "\n\nWhile a schedule is set it is the only thing deciding, so doors and occupants are"
                + " ignored. Always on and always off still override it."
                + "\n\nCycles off, dusk to dawn, on darkness.";
            schedule.icon = light.def.uiIcon;
            schedule.defaultIconColor = group.schedule == LightSchedule.None
                ? new Color(0.55f, 0.55f, 0.6f)
                : new Color(0.62f, 0.72f, 1f);
            schedule.groupKey = ScheduleKeyBase ^ group.roomId;
            schedule.action = delegate
            {
                manager.SetSchedule(scheduleGroup, NextSchedule(scheduleGroup.schedule), light);
            };
            yield return schedule;

            // Outdoors has no doors to speak of and sleepers never darken it, so both of the
            // remaining controls would be no-ops there.
            if (group.isOutdoor) yield break;

            Command_Action triggers = new Command_Action();
            triggers.defaultLabel = "Trigger: " + LinkLabel(group.link);
            triggers.defaultDesc = LinkDesc(group.link)
                + "\n\nOnly applies on auto with no schedule set."
                + "\n\nCycles combined, occupancy, doors.";
            triggers.icon = light.def.uiIcon;
            triggers.defaultIconColor = group.link == TriggerLink.Combined
                ? new Color(0.55f, 0.55f, 0.6f)
                : new Color(0.65f, 0.9f, 0.72f);
            triggers.groupKey = LinkKeyBase ^ group.roomId;
            triggers.action = delegate
            {
                manager.SetTriggerLink(scheduleGroup, NextLink(scheduleGroup.link), light);
            };
            yield return triggers;

            Command_Action sleepers = new Command_Action();
            sleepers.defaultLabel = "Sleepers: " + SleepLabel(group.sleepDarkening);
            sleepers.defaultDesc = SleepDesc(group.sleepDarkening)
                + "\n\nAn open door still lights the room either way."
                + "\n\nCycles dark if any asleep, dark if all asleep, ignored.";
            sleepers.icon = light.def.uiIcon;
            sleepers.defaultIconColor = group.sleepDarkening == SleepDarkening.Never
                ? new Color(0.55f, 0.55f, 0.6f)
                : new Color(0.72f, 0.66f, 1f);
            sleepers.groupKey = SleepKeyBase ^ group.roomId;
            sleepers.action = delegate
            {
                manager.SetSleepDarkening(scheduleGroup, NextSleepDarkening(scheduleGroup.sleepDarkening), light);
            };
            yield return sleepers;
        }

        private static string LinkLabel(TriggerLink link)
        {
            switch (link)
            {
                case TriggerLink.Occupied: return "occupancy";
                case TriggerLink.Doors: return "doors";
                default: return "combined";
            }
        }

        private static string LinkDesc(TriggerLink link)
        {
            switch (link)
            {
                case TriggerLink.Occupied:
                    return "Only an occupant lights this room. A door swinging open on an empty room"
                           + " leaves it dark, which suits a room people pass by more than enter.";
                case TriggerLink.Doors:
                    return "Only an open door lights this room. Someone standing in it with the door"
                           + " shut leaves it dark, which suits a corridor or an airlock.";
                default:
                    return "Either an open door or an occupant lights this room.";
            }
        }

        private static TriggerLink NextLink(TriggerLink link)
        {
            switch (link)
            {
                case TriggerLink.Combined: return TriggerLink.Occupied;
                case TriggerLink.Occupied: return TriggerLink.Doors;
                default: return TriggerLink.Combined;
            }
        }

        private static string SleepLabel(SleepDarkening sleepDarkening)
        {
            switch (sleepDarkening)
            {
                case SleepDarkening.IfAny: return "dark if any asleep";
                case SleepDarkening.Never: return "ignored";
                default: return "dark if all asleep";
            }
        }

        private static string SleepDesc(SleepDarkening sleepDarkening)
        {
            switch (sleepDarkening)
            {
                case SleepDarkening.IfAny:
                    return "One sleeper is enough to send this room dark, even with others awake."
                           + " Suits a bedroom shared with a workshop corner.";
                case SleepDarkening.Never:
                    return "Sleepers count as ordinary occupants and hold the lights on."
                           + " Suits a hospital or a barracks you want lit around the clock.";
                default:
                    return "This room goes dark once every occupant is asleep, and lights back up"
                           + " the moment one of them wakes.";
            }
        }

        private static SleepDarkening NextSleepDarkening(SleepDarkening sleepDarkening)
        {
            switch (sleepDarkening)
            {
                case SleepDarkening.IfAny: return SleepDarkening.IfAll;
                case SleepDarkening.IfAll: return SleepDarkening.Never;
                default: return SleepDarkening.IfAny;
            }
        }

        private static string ScheduleLabel(LightSchedule schedule)
        {
            switch (schedule)
            {
                case LightSchedule.DuskToDawn: return "dusk to dawn";
                case LightSchedule.Darkness: return "on darkness";
                default: return "off";
            }
        }

        private static string ScheduleDesc(LightSchedule schedule, string scope, RoomAutoLightSettings settings)
        {
            switch (schedule)
            {
                case LightSchedule.DuskToDawn:
                    return "Runs the " + scope + " on the clock: on at dusk, off at dawn. Weather and"
                           + " eclipses are ignored, so a dark storm at noon will not light it."
                           + "\n\nNight starts at celestial glow "
                           + settings.duskGlowThreshold.ToString("F2") + ".";
                case LightSchedule.Darkness:
                    return "Runs the " + scope + " on how dark it actually is, so an eclipse or a black"
                           + " storm at noon lights it just like nightfall does."
                           + "\n\nDark starts at sky glow "
                           + settings.darknessGlowThreshold.ToString("F2") + ".";
                default:
                    return "No schedule. Doors and occupants drive the " + scope + ".";
            }
        }

        private static RoomLightMode NextMode(RoomLightMode mode)
        {
            switch (mode)
            {
                case RoomLightMode.Auto: return RoomLightMode.ForceOn;
                case RoomLightMode.ForceOn: return RoomLightMode.ForceOff;
                default: return RoomLightMode.Auto;
            }
        }

        private static LightSchedule NextSchedule(LightSchedule schedule)
        {
            switch (schedule)
            {
                case LightSchedule.None: return LightSchedule.DuskToDawn;
                case LightSchedule.DuskToDawn: return LightSchedule.Darkness;
                default: return LightSchedule.None;
            }
        }
    }
}
