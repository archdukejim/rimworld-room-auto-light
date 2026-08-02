using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace RoomAutoLight
{
    /// <summary>
    /// Coalesces glow invalidation across a whole group switch.
    ///
    /// GlowGrid.DirtyCell does two MapDrawer.MapMeshDirty calls per cell, and
    /// Register/DeRegisterGlower call it for every cell of a glower's affected rect. A standing
    /// lamp is glowRadius 12, so that is ~625 cells each; four lamps three cells apart in one room
    /// have nearly the same rect, so a room switch marks ~2500 cells for a union of about 730.
    ///
    /// Vanilla already batches the glow *computation* — RegisterGlower only sets anyDirtyLight and
    /// one ComputeGlowGridsJob runs per frame no matter how many glowers moved. What is not
    /// batched is the invalidation, so that is the only part this touches: collect the cells while
    /// a group is switching, then replay the distinct set through vanilla's own DirtyCell.
    /// </summary>
    public static class GlowDirtyBatch
    {
        public static bool Active;

        private static GlowGrid grid;
        private static readonly HashSet<IntVec3> cells = new HashSet<IntVec3>();
        private static Action<GlowGrid, IntVec3> dirtyCell;

        private static Action<GlowGrid, IntVec3> DirtyCell
        {
            get
            {
                if (dirtyCell == null)
                    dirtyCell = AccessTools.MethodDelegate<Action<GlowGrid, IntVec3>>(
                        AccessTools.Method(typeof(GlowGrid), "DirtyCell"));
                return dirtyCell;
            }
        }

        public static void Begin(Map map)
        {
            if (map == null || Active || RoomLightProfiler.BypassGlowBatch) return;
            grid = map.glowGrid;
            if (grid == null) return;
            cells.Clear();
            Active = true;
        }

        public static void Collect(IntVec3 cell)
        {
            cells.Add(cell);
        }

        public static void Flush()
        {
            if (!Active) return;
            Active = false;

            if (cells.Count > 0 && grid != null)
            {
                Action<GlowGrid, IntVec3> call = DirtyCell;
                foreach (IntVec3 cell in cells) call(grid, cell);
                cells.Clear();
            }
            grid = null;
        }
    }

    /// <summary>
    /// While a group switch is in flight, park the cell instead of marking it. Costs one static
    /// bool test on every other glow update in the game.
    /// </summary>
    [HarmonyPatch(typeof(GlowGrid), "DirtyCell")]
    public static class GlowGrid_DirtyCell_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(IntVec3 __0)
        {
            // Counted before batching, so it measures work asked for rather than work done, and
            // stays comparable against a mod that does no batching at all.
            if (WholeTickProfiler.Enabled) WholeTickProfiler.CountDirtyCell();

            if (!GlowDirtyBatch.Active) return true;
            GlowDirtyBatch.Collect(__0);
            return false;
        }
    }
}
