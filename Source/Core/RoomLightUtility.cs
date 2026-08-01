using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RoomAutoLight
{
    public static class RoomLightUtility
    {
        private static readonly Dictionary<ThingDef, bool> managedDefCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> growLightDefCache = new Dictionary<ThingDef, bool>();

        public static void ClearDefCache()
        {
            managedDefCache.Clear();
            growLightDefCache.Clear();
        }

        public static bool IsManagedLight(Thing thing)
        {
            return thing is Building && IsManagedLightDef(thing.def);
        }

        public static bool IsManagedLightDef(ThingDef def)
        {
            if (def == null) return false;
            bool cached;
            if (managedDefCache.TryGetValue(def, out cached)) return cached;
            bool result = EvaluateDef(def);
            managedDefCache[def] = result;
            return result;
        }

        private static bool EvaluateDef(ThingDef def)
        {
            RoomAutoLightSettings settings = RoomAutoLightMod.Settings;
            if (def.category != ThingCategory.Building) return false;
            if (settings.ExcludedDefNames.Contains(def.defName)) return false;
            if (settings.IncludedDefNames.Contains(def.defName)) return true;

            // Plain Building is the tell for "this is a lamp". Sun lamps (Building_SunLamp),
            // workbenches, TVs and turrets all carry their own thingClass and are skipped.
            if (def.thingClass != typeof(Building)) return false;

            CompProperties_Glower glower = def.GetCompProperties<CompProperties_Glower>();
            if (glower == null || glower.glowRadius <= 0f) return false;

            CompProperties_Power power = def.GetCompProperties<CompProperties_Power>();
            if (power == null || power.compClass == null) return false;
            if (!typeof(CompPowerTrader).IsAssignableFrom(power.compClass)) return false;
            if (power.PowerConsumption <= 0f || power.PowerConsumption > settings.maxManagedWatts) return false;

            return true;
        }

        /// <summary>
        /// Plant lights: bright enough that any ordinary lamp in the same room is redundant while
        /// they are lit. Vanilla's sun lamp is Building_SunLamp with overlightRadius 7; the ritual
        /// props that also use overlightRadius (LightBall, Loudspeaker) sit below the threshold.
        /// </summary>
        public static bool IsGrowLightDef(ThingDef def)
        {
            if (def == null) return false;
            bool cached;
            if (growLightDefCache.TryGetValue(def, out cached)) return cached;
            bool result = EvaluateGrowLightDef(def);
            growLightDefCache[def] = result;
            return result;
        }

        private static bool EvaluateGrowLightDef(ThingDef def)
        {
            RoomAutoLightSettings settings = RoomAutoLightMod.Settings;
            if (def.category != ThingCategory.Building) return false;

            CompProperties_Glower glower = def.GetCompProperties<CompProperties_Glower>();
            if (glower == null) return false;

            if (settings.GrowLightDefNames.Contains(def.defName)) return true;
            if (DerivesFromSunLamp(def.thingClass)) return true;
            return glower.overlightRadius >= settings.growLightMinOverlight;
        }

        /// <summary>Building_SunLamp is internal to Assembly-CSharp, so match it by name up the chain.</summary>
        private static bool DerivesFromSunLamp(Type thingClass)
        {
            for (Type type = thingClass; type != null; type = type.BaseType)
                if (type.Name == "Building_SunLamp") return true;
            return false;
        }

        public static bool IsGrowLight(Thing thing)
        {
            return thing is Building && IsGrowLightDef(thing.def);
        }

        /// <summary>
        /// Wall-mounted lamps sit on an impassable cell and have no region of their own,
        /// so fall through to the adjacent room rather than dropping the light.
        /// </summary>
        public static Room RoomOf(Thing thing)
        {
            if (thing == null || !thing.Spawned) return null;
            return RegionAndRoomQuery.RoomAtOrAdjacent(thing.Position, thing.Map, RegionType.Set_Passable);
        }

        public static bool IsOutdoors(Room room)
        {
            return room != null && !room.Dereferenced && room.PsychologicallyOutdoors;
        }

        /// <summary>True for rooms that get their own group. The outdoors is handled separately.</summary>
        public static bool IsAutomatableRoom(Room room)
        {
            if (room == null || room.Dereferenced) return false;
            if (room.IsDoorway) return false;
            if (room.PsychologicallyOutdoors) return false;
            if (!room.ProperRoom) return false;
            return true;
        }

        public static bool IsPlayerOwned(Thing thing)
        {
            return thing != null && thing.Faction != null && thing.Faction == Faction.OfPlayer;
        }

        /// <summary>
        /// Celestial glow only, so eclipses and storms do not count as dusk. The threshold
        /// defaults to vanilla's own day/night cutoff in GenCelestial.IsDaytime.
        /// </summary>
        public static bool IsNight(Map map)
        {
            if (map == null) return false;
            return GenCelestial.CurCelestialSunGlow(map) <= RoomAutoLightMod.Settings.duskGlowThreshold;
        }

        /// <summary>
        /// Actual sky glow, which SkyManager folds weather and game conditions into. An eclipse
        /// or a black storm at noon reads as dark here, where the celestial clock would not.
        /// Deliberately not the glow grid: reading light the lamps themselves cast would oscillate.
        /// </summary>
        public static bool IsDark(Map map)
        {
            if (map == null || map.skyManager == null) return false;
            return map.skyManager.CurSkyGlow <= RoomAutoLightMod.Settings.darknessGlowThreshold;
        }

        /// <summary>
        /// Cheap stand-in for the room's outline. Cell count alone misses a wall swap that keeps
        /// the area, and extents alone miss a wall knocked through into an identical footprint, so
        /// both go in. Two genuinely different shapes agreeing on all of it is rare enough to
        /// accept, and the cost of missing one is only that a circuit survives a hit.
        /// </summary>
        public static int RoomFingerprint(Room room)
        {
            if (room == null || room.Dereferenced) return 0;

            CellRect extents = room.ExtentsClose;
            int hash = room.CellCount;
            hash = hash * 397 ^ extents.minX;
            hash = hash * 397 ^ extents.minZ;
            hash = hash * 397 ^ extents.maxX;
            hash = hash * 397 ^ extents.maxZ;
            hash = hash * 397 ^ room.RegionCount;
            return hash;
        }

        public static bool CountsAsOccupant(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned) return false;
            if (!pawn.RaceProps.Humanlike && !RoomAutoLightMod.Settings.animalsCountAsOccupants) return false;
            return true;
        }

        /// <summary>
        /// Sleep is only ever counted indoors: nobody is trying to sleep through a perimeter
        /// floodlight, and the dark out there is the point. What a room does about a sleeper is
        /// then up to its own SleepDarkening setting.
        /// </summary>
        public static bool IsSleeping(Pawn pawn, bool outdoors)
        {
            if (outdoors) return false;
            return !RestUtility.Awake(pawn);
        }
    }
}
