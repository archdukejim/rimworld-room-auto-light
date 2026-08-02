using RimWorld;
using Verse;

namespace RoomAutoLight
{
    /// <summary>
    /// Drives the whole A/B run without a single click when the stress arg is passed: warm up,
    /// record the patched build, flip to the pre-fix behaviour, record again, then report both to
    /// the log. Same process, same map, same pawns, so the two halves are actually comparable.
    /// </summary>
    public static class StressBenchmark
    {
        private const int WarmupTicks = 900;
        private const int PhaseTicks = 1800;

        private static bool armed;
        private static int startTick = -1;
        private static int phase;

        public static void Arm()
        {
            armed = true;
            phase = 0;
        }

        public static bool Running
        {
            get { return armed && phase < 3; }
        }

        public static void Advance()
        {
            if (!armed || phase >= 3) return;

            int now = Find.TickManager.TicksGame;
            if (startTick < 0)
            {
                startTick = now;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                Log.Message("[RoomAutoLight] benchmark armed: warming up for " + WarmupTicks + " ticks.");
            }

            int elapsed = now - startTick;

            if (phase == 0 && elapsed >= WarmupTicks)
            {
                phase = 1;
                RoomLightProfiler.BypassFingerprintCache = false;
                RoomLightProfiler.Reset();
                RoomLightProfiler.Enabled = true;
                Log.Message("[RoomAutoLight] phase 1 of 2: recording PATCHED for " + PhaseTicks + " ticks.");
                return;
            }

            if (phase == 1 && elapsed >= WarmupTicks + PhaseTicks)
            {
                Log.Message(RoomLightProfiler.Report());
                phase = 2;
                RoomLightProfiler.BypassFingerprintCache = true;
                RoomLightProfiler.Reset();
                Log.Message("[RoomAutoLight] phase 2 of 2: recording PRE-FIX for " + PhaseTicks + " ticks.");
                return;
            }

            if (phase == 2 && elapsed >= WarmupTicks + PhaseTicks * 2)
            {
                Log.Message(RoomLightProfiler.Report());
                RoomLightProfiler.Enabled = false;
                RoomLightProfiler.BypassFingerprintCache = false;
                phase = 3;
                Log.Message("[RoomAutoLight] BENCHMARK COMPLETE");
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            }
        }
    }
}
