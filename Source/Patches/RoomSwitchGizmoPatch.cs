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
