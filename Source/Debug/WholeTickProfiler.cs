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
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    public static class WholeTickProfiler
    {
        public static bool Enabled;

        private static long ticks;
        private static double totalMs;
        private static double worstMs;

        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

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
        }

        public static string Report(string label)
        {
            if (ticks == 0) return "whole-tick profiler: no samples.";
            return "=== whole game tick [" + label + "] ===\n"
                   + "ticks sampled       : " + ticks + "\n"
                   + "mean whole tick     : " + (totalMs / ticks * 1000.0).ToString("F1") + " us\n"
                   + "worst whole tick    : " + (worstMs * 1000.0).ToString("F1") + " us\n"
                   + "implied max TPS     : " + (1000.0 / (totalMs / ticks)).ToString("F0") + "\n";
        }
    }
}
