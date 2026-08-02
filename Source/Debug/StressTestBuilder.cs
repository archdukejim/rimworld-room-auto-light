using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using Verse;

namespace RoomAutoLight
{
    /// <summary>
    /// Builds a fixed benchmark colony: a 10x10 lattice of 4x4 rooms, doors between neighbours,
    /// four lamps in every room's corners, the whole lattice wired and battery-backed.
    ///
    /// Hooked off map generation rather than built by hand, so every run measures the identical
    /// layout and a run needs no clicking at all:
    ///   RimWorldWin64.exe -quicktest -roomautolight-stress
    /// </summary>
    public static class StressTestBuilder
    {
        public const string CommandLineArg = "roomautolight-stress";

        private const int Rooms = 10;      // rooms per side
        private const int Interior = 4;    // interior size of each room
        private const int Pitch = Interior + 1;
        private const int Span = Rooms * Pitch + 1;

        public static bool RequestedOnMapGen
        {
            get { return GenCommandLine.CommandLineArgPassed(CommandLineArg); }
        }

        public static void Build(Map map, int pawnCount = 12)
        {
            IntVec3 origin = new IntVec3(map.Size.x / 2 - Span / 2, 0, map.Size.z / 2 - Span / 2);
            if (origin.x < 2 || origin.z < 2)
            {
                Log.Error("[RoomAutoLight] Map too small for the stress grid: needs " + Span + " cells square.");
                return;
            }

            ThingDef wallDef = ThingDefOf.Wall;
            ThingDef doorDef = ThingDef.Named("Door");
            ThingDef conduitDef = ThingDef.Named("PowerConduit");
            ThingDef lampDef = ThingDef.Named("StandingLamp");
            ThingDef batteryDef = ThingDef.Named("Battery");
            ThingDef stuff = ThingDefOf.Steel;

            int walls = 0, doors = 0, lamps = 0;

            // Flatten, floor, unfog and roof the whole footprint first, so the rooms that come out
            // of it are ordinary indoor rooms rather than half-fogged caves.
            for (int x = 0; x < Span; x++)
            {
                for (int z = 0; z < Span; z++)
                {
                    IntVec3 c = origin + new IntVec3(x, 0, z);
                    ClearCell(c, map);
                    map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete);
                    map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                    map.fogGrid.Unfog(c);
                }
            }

            for (int x = 0; x < Span; x++)
            {
                for (int z = 0; z < Span; z++)
                {
                    bool onLattice = x % Pitch == 0 || z % Pitch == 0;
                    if (!onLattice) continue;

                    IntVec3 c = origin + new IntVec3(x, 0, z);

                    // Conduit runs under the whole lattice, so every lamp has a net to sit on.
                    Spawn(conduitDef, null, c, map);

                    if (IsInteriorDoorCell(x, z))
                    {
                        Spawn(doorDef, stuff, c, map);
                        doors++;
                    }
                    else
                    {
                        Spawn(wallDef, stuff, c, map);
                        walls++;
                    }
                }
            }

            for (int rx = 0; rx < Rooms; rx++)
            {
                for (int rz = 0; rz < Rooms; rz++)
                {
                    int bx = rx * Pitch;
                    int bz = rz * Pitch;
                    int[] offsets = { 1, Interior };
                    foreach (int ox in offsets)
                    {
                        foreach (int oz in offsets)
                        {
                            Spawn(lampDef, null, origin + new IntVec3(bx + ox, 0, bz + oz), map);
                            lamps++;
                        }
                    }
                }
            }

            // Charged batteries rather than generators: no day/night swing, so a benchmark run is
            // not measuring a brownout. The net still has real comps for the affordability check
            // to walk.
            for (int i = 0; i < 12; i++)
            {
                IntVec3 c = origin + new IntVec3(i * Pitch + 2, 0, 0);
                if (!c.InBounds(map)) continue;
                Thing battery = Spawn(batteryDef, stuff, c, map);
                if (battery == null) continue;
                CompPowerBattery comp = battery.TryGetComp<CompPowerBattery>();
                if (comp != null) comp.SetStoredEnergyPct(1f);
            }

            SpawnPawns(map, origin, pawnCount);

            RoomLightManager manager = map.GetComponent<RoomLightManager>();
            if (manager != null) manager.MarkDirty();

            string summary = "[RoomAutoLight] stress grid: " + (Rooms * Rooms) + " rooms, "
                             + lamps + " lamps, " + doors + " doors, " + walls + " walls, "
                             + pawnCount + " pawns, " + Span + "x" + Span + " cells.";
            Log.Message(summary);
        }

        /// <summary>Doors sit at the midpoint of every interior wall segment, never on a corner.</summary>
        private static bool IsInteriorDoorCell(int x, int z)
        {
            bool xOnLine = x % Pitch == 0;
            bool zOnLine = z % Pitch == 0;
            if (xOnLine && zOnLine) return false;

            if (xOnLine)
            {
                if (x == 0 || x == Span - 1) return false;
                return z % Pitch == Pitch / 2;
            }
            if (z == 0 || z == Span - 1) return false;
            return x % Pitch == Pitch / 2;
        }

        private static void ClearCell(IntVec3 c, Map map)
        {
            List<Thing> things = c.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing t = things[i];
                if (t.def.destroyable && !(t is Pawn)) t.Destroy(DestroyMode.Vanish);
            }
        }

        private static Thing Spawn(ThingDef def, ThingDef stuff, IntVec3 c, Map map)
        {
            if (!c.InBounds(map)) return null;
            Thing thing = ThingMaker.MakeThing(def, stuff);
            thing.SetFactionDirect(Faction.OfPlayer);
            return GenSpawn.Spawn(thing, c, map, WipeMode.Vanish);
        }

        private static void SpawnPawns(Map map, IntVec3 origin, int count)
        {
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = origin + new IntVec3(
                    (i % Rooms) * Pitch + 2, 0, (i / Rooms % Rooms) * Pitch + 2);
                if (!cell.InBounds(map)) continue;

                Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(pawn, cell, map, WipeMode.Vanish);
            }
        }

        [DebugAction("Room Auto Light", "Build stress grid", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugBuild()
        {
            Build(Find.CurrentMap);
        }

        [DebugAction("Room Auto Light", "Profiler: start", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugProfilerStart()
        {
            RoomLightProfiler.Reset();
            RoomLightProfiler.Enabled = true;
            Messages.Message("Room Auto Light profiler recording.", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("Room Auto Light", "Profiler: stop and report",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugProfilerStop()
        {
            RoomLightProfiler.Enabled = false;
            Log.Message(RoomLightProfiler.Report());
            Messages.Message("Room Auto Light profile written to the log.", MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
