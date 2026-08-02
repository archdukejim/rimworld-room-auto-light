using System.Diagnostics;
using System.Text;
using Verse;

namespace RoomAutoLight
{
    /// <summary>
    /// Self-timing for the manager's tick, off unless a debug action turns it on. Uses raw
    /// Stopwatch timestamps rather than a running Stopwatch so the instrumentation itself is a
    /// couple of nanoseconds when enabled and a single bool test when not.
    /// </summary>
    public static class RoomLightProfiler
    {
        public static bool Enabled;

        private static long tickCount;
        private static long rebuildCount;
        private static long evaluatedGroups;

        private static double totalMs;
        private static double rebuildMs;
        private static double occupancyMs;
        private static double worstTickMs;

        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        public static long Now()
        {
            return Stopwatch.GetTimestamp();
        }

        public static double MsSince(long start)
        {
            return (Stopwatch.GetTimestamp() - start) * TicksToMs;
        }

        public static void RecordTick(long start, int groupsEvaluated)
        {
            double ms = MsSince(start);
            tickCount++;
            totalMs += ms;
            evaluatedGroups += groupsEvaluated;
            if (ms > worstTickMs) worstTickMs = ms;
        }

        public static void RecordRebuild(long start)
        {
            rebuildCount++;
            rebuildMs += MsSince(start);
        }

        public static void RecordOccupancy(long start)
        {
            occupancyMs += MsSince(start);
        }

        public static void Reset()
        {
            tickCount = 0;
            rebuildCount = 0;
            evaluatedGroups = 0;
            totalMs = 0;
            rebuildMs = 0;
            occupancyMs = 0;
            worstTickMs = 0;
        }

        public static string Report()
        {
            if (tickCount == 0) return "Room Auto Light profiler: no ticks recorded.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Room Auto Light profile ===");
            sb.AppendLine("ticks sampled       : " + tickCount);
            sb.AppendLine("mean per tick       : " + (totalMs / tickCount * 1000.0).ToString("F1") + " us");
            sb.AppendLine("worst tick          : " + (worstTickMs * 1000.0).ToString("F1") + " us");
            sb.AppendLine("total time          : " + totalMs.ToString("F1") + " ms");
            sb.AppendLine("share of 60 TPS     : " + (totalMs / (tickCount * 16.667) * 100.0).ToString("F3") + " %");
            sb.AppendLine("group evals         : " + evaluatedGroups
                          + " (" + ((double)evaluatedGroups / tickCount).ToString("F2") + " per tick)");
            sb.AppendLine("rebuilds            : " + rebuildCount
                          + (rebuildCount > 0
                              ? " (mean " + (rebuildMs / rebuildCount * 1000.0).ToString("F1") + " us)"
                              : ""));
            sb.AppendLine("rebuild share       : " + (rebuildMs / totalMs * 100.0).ToString("F1") + " % of mod time");
            sb.AppendLine("occupancy share     : " + (occupancyMs / totalMs * 100.0).ToString("F1") + " % of mod time");
            return sb.ToString();
        }
    }
}
