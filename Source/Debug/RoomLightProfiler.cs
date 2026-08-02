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

        /// <summary>
        /// Benchmark switch: makes the rebuild recompute a room's fingerprint once per lamp the way
        /// it did before the memo was added. Lets the A/B run in one process on one map, rather
        /// than comparing two launches whose maps and timings never line up.
        /// </summary>
        public static bool BypassFingerprintCache;

        /// <summary>Benchmark switch: invalidate glow once per lamp, the way it worked before batching.</summary>
        public static bool BypassGlowBatch;

        private static long tickCount;
        private static long rebuildCount;
        private static long evaluatedGroups;

        private static double totalMs;
        private static double rebuildMs;
        private static double occupancyMs;
        private static double worstTickMs;

        // Inside a group evaluation.
        private static double computeWantMs;
        private static double affordMs;
        private static double applyMs;
        private static long affordCalls;
        private static long netQueries;
        private static double applyTransitionMs;
        private static double applyNoOpMs;
        private static long applyTransitions;
        private static long applyNoOps;

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

        public static void AddComputeWant(long start)
        {
            computeWantMs += MsSince(start);
        }

        public static void AddAfford(long start)
        {
            affordMs += MsSince(start);
            affordCalls++;
        }

        public static void AddApply(long start, bool changed)
        {
            double ms = MsSince(start);
            applyMs += ms;
            if (changed)
            {
                applyTransitionMs += ms;
                applyTransitions++;
            }
            else
            {
                applyNoOpMs += ms;
                applyNoOps++;
            }
        }

        /// <summary>One PowerNet energy query, each of which walks every comp on that net.</summary>
        public static void CountNetQuery()
        {
            netQueries++;
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
            computeWantMs = 0;
            affordMs = 0;
            applyMs = 0;
            affordCalls = 0;
            netQueries = 0;
            applyTransitionMs = 0;
            applyNoOpMs = 0;
            applyTransitions = 0;
            applyNoOps = 0;
        }

        public static string Report()
        {
            if (tickCount == 0) return "Room Auto Light profiler: no ticks recorded.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Room Auto Light profile ===");
            sb.AppendLine("mode                : "
                          + (BypassFingerprintCache ? "PRE-FIX (fingerprint per lamp)" : "PATCHED"));
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
            sb.AppendLine("--- inside group evaluation ---");
            sb.AppendLine("ComputeWant         : " + (computeWantMs / totalMs * 100.0).ToString("F1")
                          + " % (" + (computeWantMs / evaluatedGroups * 1000.0).ToString("F1") + " us/eval)");
            sb.AppendLine("CanAffordWholeGroup : " + (affordMs / totalMs * 100.0).ToString("F1")
                          + " % (" + affordCalls + " calls, "
                          + (affordCalls > 0 ? (affordMs / affordCalls * 1000.0).ToString("F1") : "0")
                          + " us/call)");
            sb.AppendLine("Apply               : " + (applyMs / totalMs * 100.0).ToString("F1")
                          + " % (" + (applyMs / evaluatedGroups * 1000.0).ToString("F1") + " us/eval)");
            sb.AppendLine("  transitions       : " + applyTransitions + " calls, "
                          + (applyTransitions > 0 ? (applyTransitionMs / applyTransitions * 1000.0).ToString("F1") : "0")
                          + " us each, " + (applyTransitionMs / totalMs * 100.0).ToString("F1") + " % of mod time");
            sb.AppendLine("  no-ops            : " + applyNoOps + " calls, "
                          + (applyNoOps > 0 ? (applyNoOpMs / applyNoOps * 1000.0).ToString("F1") : "0")
                          + " us each, " + (applyNoOpMs / totalMs * 100.0).ToString("F1") + " % of mod time");
            sb.AppendLine("PowerNet queries    : " + netQueries
                          + " (each walks every comp on the net)");
            return sb.ToString();
        }
    }
}
