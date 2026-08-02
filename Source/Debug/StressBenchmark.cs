using RimWorld;
using Verse;

namespace RoomAutoLight
{
    /// <summary>
    /// Drives the whole run without a single click when a stress arg is passed.
    ///
    /// Normal mode does the A/B on our own code: warm up, record patched, flip to the pre-fix
    /// behaviour, record again. Passive mode leaves our automation off and only records the whole
    /// game tick, which is what makes a comparison against another lighting mod meaningful.
    /// </summary>
    public static class StressBenchmark
    {
        private const int WarmupTicks = 900;
        private const int PhaseTicks = 1800;

        private const string RivalPackageId = "Merthsoft.AutoLightSwitch";

        private static bool armed;
        private static bool passive;
        private static int startTick = -1;
        private static int phase;

        public static void Arm()
        {
            armed = true;
            phase = 0;
            passive = StressTestBuilder.Passive;

            if (passive) RoomAutoLightMod.Settings.enabled = false;
        }

        public static bool Running
        {
            get { return armed && phase < 3; }
        }

        private static string ConfigLabel
        {
            get
            {
                // RimWorld stores package ids lower-cased, so a mixed-case literal never matches.
                bool rival = ModsConfig.IsActive(RivalPackageId.ToLowerInvariant());
                string mine = passive ? "RoomAutoLight OFF" : "RoomAutoLight ON";
                return mine + ", AutoLightSwitch " + (rival ? "ON" : "off");
            }
        }

        public static void Advance()
        {
            if (!armed || phase >= 3) return;

            int now = Find.TickManager.TicksGame;
            if (startTick < 0)
            {
                startTick = now;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                Log.Message("[RoomAutoLight] benchmark armed (" + ConfigLabel + "), warming up "
                            + WarmupTicks + " ticks.");
            }

            int elapsed = now - startTick;

            if (phase == 0 && elapsed >= WarmupTicks)
            {
                phase = 1;
                RoomLightProfiler.BypassFingerprintCache = false;
                RoomLightProfiler.BypassGlowBatch = false;
                RoomLightProfiler.Reset();
                RoomLightProfiler.Enabled = !passive;
                WholeTickProfiler.Reset();
                WholeTickProfiler.Enabled = true;
                Log.Message("[RoomAutoLight] recording " + PhaseTicks + " ticks (" + ConfigLabel + ").");
                return;
            }

            if (phase == 1 && elapsed >= WarmupTicks + PhaseTicks)
            {
                Log.Message(WholeTickProfiler.Report(ConfigLabel));
                if (!passive) Log.Message(RoomLightProfiler.Report());

                if (passive)
                {
                    Finish();
                    return;
                }

                phase = 2;
                RoomLightProfiler.BypassFingerprintCache = true;
                RoomLightProfiler.BypassGlowBatch = true;
                RoomLightProfiler.Reset();
                WholeTickProfiler.Reset();
                Log.Message("[RoomAutoLight] phase 2 of 2: recording PRE-FIX (no glow batching) for "
                            + PhaseTicks + " ticks.");
                return;
            }

            if (phase == 2 && elapsed >= WarmupTicks + PhaseTicks * 2)
            {
                Log.Message(WholeTickProfiler.Report("PRE-FIX, " + ConfigLabel));
                Log.Message(RoomLightProfiler.Report());
                Finish();
            }
        }

        private static void Finish()
        {
            RoomLightProfiler.Enabled = false;
            RoomLightProfiler.BypassFingerprintCache = false;
            WholeTickProfiler.Enabled = false;
            phase = 3;
            Log.Message("[RoomAutoLight] BENCHMARK COMPLETE");
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        }
    }
}
