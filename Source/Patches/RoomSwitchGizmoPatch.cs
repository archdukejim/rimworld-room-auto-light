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
        // Per lamp rather than per room, so one click can repair a whole selection.
        private const int ResetKey = 84740001;

        // Per-lamp rather than per-group, so one key lets every selected lamp merge into one
        // control that still toggles each of them.
        private const int UngroupKey = 84750001;

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Building __instance)
        {
            foreach (Gizmo gizmo in values) yield return gizmo;

            RoomAutoLightSettings settings = RoomAutoLightMod.Settings;
            if (!settings.enabled || !settings.showGizmo) yield break;
            if (__instance.Map == null || !RoomLightUtility.IsManagedLightDef(__instance.def)) yield break;

            RoomLightManager manager = __instance.Map.GetComponent<RoomLightManager>();
            if (manager == null) yield break;

            Building light = __instance;
            bool ungrouped = manager.IsUngrouped(light);

            // Rendered before the group check and regardless of membership: once a lamp is out of
            // its group there is no group to hang the control off, and it could never rejoin.
            Command_Toggle join = new Command_Toggle();
            join.defaultLabel = "Ungroup lamp";
            join.defaultDesc = "Pulls this one lamp out of its room group and hands it back to vanilla,"
                + " so it keeps its own power and its own switch."
                + "\n\nUse it for a lamp you always want lit, or to keep one lamp out of the all-or-nothing"
                + " power check when the room cannot afford every lamp at once.";
            join.icon = light.def.uiIcon;
            join.defaultIconColor = ungrouped ? new Color(0.9f, 0.55f, 0.5f) : new Color(0.55f, 0.55f, 0.6f);
            join.groupKey = UngroupKey;
            join.isActive = delegate { return manager.IsUngrouped(light); };
            join.toggleAction = delegate { manager.SetUngrouped(light, !manager.IsUngrouped(light)); };
            yield return join;

            if (ungrouped) yield break;

            // A broken lamp is in no group, so this has to come before the group lookup or there
            // would be nothing to click.
            if (manager.IsBroken(light))
            {
                Command_Action reset = new Command_Action();
                reset.defaultLabel = "Reset lighting circuit";
                reset.defaultDesc = "This room's boundary changed under its lamps - a wall blown out,"
                    + " deconstructed or built - so the circuit took the hit and the lights dropped."
                    + "\n\nResetting repairs every broken lamp in this room at once and re-forms the"
                    + " group around the room as it now stands.";
                reset.icon = light.def.uiIcon;
                reset.defaultIconColor = new Color(1f, 0.45f, 0.4f);
                reset.groupKey = ResetKey;
                reset.action = delegate { manager.ResetCircuitAt(light); };
                yield return reset;
                yield break;
            }

            RoomLightGroup group = manager.GroupFor(light);
            if (group == null) yield break;
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

            // Sleepers never darken the outdoors, so the control would be a no-op there.
            if (group.isOutdoor) yield break;

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
