using System.Diagnostics;
using HarmonyLib;
using Verse;

namespace RoomAutoLight
{
    /// <summary>
    /// Times the game's whole tick, so a run can be compared against another mod doing the same
    /// job. Measuring only our own MapComponent says nothing about a rival that works from a
    /// ThingComp; the difference between whole-tick means across configurations does.
    ///
    /// The patch is applied always but costs one bool test unless a benchmark turns it on.
    /// </summary>
    [HarmonyPatch(typeof(GlowGrid), nameof(GlowGrid.GlowGridUpdate_First))]
    public static class GlowGrid_Update_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(ref long __state)
        {
            __state = WholeTickProfiler.Enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        [HarmonyPostfix]
        public static void Postfix(long __state)
        {
            if (__state != 0L) WholeTickProfiler.AddGlowUpdate(__state);
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    public static class WholeTickProfiler
    {
        public static bool Enabled;

        private static long ticks;
        private static double totalMs;
        private static double worstMs;

        // Glow work, whoever caused it. GlowGridUpdate_First runs per frame rather than per tick,
        // which is why whole-tick could not hold a figure steady: how much of the recompute landed
        // inside a DoSingleTick window was arbitrary. Timing it directly removes that.
        private static double glowUpdateMs;
        private static long glowUpdateCalls;

        // Every glow invalidation on the map, from any mod. The workload measure that makes two
        // different lighting mods comparable at all.
        private static long dirtyCells;

        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        public static void CountDirtyCell()
        {
            dirtyCells++;
        }

        public static void AddGlowUpdate(long start)
        {
            glowUpdateMs += (Stopwatch.GetTimestamp() - start) * TicksToMs;
            glowUpdateCalls++;
        }

        [HarmonyPrefix]
        public static void Prefix(ref long __state)
        {
            __state = Enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        [HarmonyPostfix]
        public static void Postfix(long __state)
        {
            if (!Enabled || __state == 0L) return;
            double ms = (Stopwatch.GetTimestamp() - __state) * TicksToMs;
            ticks++;
            totalMs += ms;
            if (ms > worstMs) worstMs = ms;
        }

        public static void Reset()
        {
            ticks = 0;
            totalMs = 0;
            worstMs = 0;
            glowUpdateMs = 0;
            glowUpdateCalls = 0;
            dirtyCells = 0;
        }

        public static string Report(string label)
        {
            if (ticks == 0) return "whole-tick profiler: no samples.";

            double glowPerTick = glowUpdateMs / ticks * 1000.0;
            return "=== global [" + label + "] ===\n"
                   + "ticks sampled       : " + ticks + "\n"
                   + "mean whole tick     : " + (totalMs / ticks * 1000.0).ToString("F1") + " us\n"
                   + "worst whole tick    : " + (worstMs * 1000.0).ToString("F1") + " us\n"
                   + "glow invalidations  : " + dirtyCells + "  <- workload, any mod\n"
                   + "glow update calls   : " + glowUpdateCalls + "\n"
                   + "glow update total   : " + glowUpdateMs.ToString("F1") + " ms\n"
                   + "glow update per tick: " + glowPerTick.ToString("F1") + " us\n";
        }
    }
}
