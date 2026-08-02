using System.Collections.Generic;
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

        // Overridable with -roomautolight-cadence=N. The default is longer than the off-delay so a
        // move produces exactly one room lighting and one darkening; a rival mod checking on its
        // own timer may need a longer dwell than that to react at all.
        private const int DefaultMoveIntervalTicks = 300;

        private static int moveInterval = DefaultMoveIntervalTicks;

        private static void ResolveCadence()
        {
            string raw;
            int parsed;
            if (GenCommandLine.TryGetCommandLineArg("roomautolight-cadence", out raw)
                && int.TryParse(raw, out parsed) && parsed > 0)
            {
                moveInterval = parsed;
            }
        }

        // Coprime with the room count, so the tour visits distinct rooms rather than cycling early.
        private const int PatternStride = 13;

        private const string RivalPackageId = "Merthsoft.AutoLightSwitch";

        private static bool armed;
        private static bool passive;
        private static int startTick = -1;
        private static int phase;
        private static int moves;

        public static void Arm()
        {
            armed = true;
            phase = 0;
            passive = StressTestBuilder.Passive;
            ResolveCadence();

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

        /// <summary>
        /// Teleports the drafted pawns through a fixed tour on a fixed cadence, so the light
        /// workload is identical in every run rather than whatever the colonists felt like doing.
        /// Step and destination derive only from the tick count, so two runs cannot diverge.
        /// </summary>
        private static void DrivePawns(int elapsed)
        {
            if (elapsed < 0 || elapsed % moveInterval != 0) return;

            List<Pawn> pawns = StressTestBuilder.TestPawns;
            if (pawns.Count == 0) return;

            int rooms = StressTestBuilder.RoomsPerSide * StressTestBuilder.RoomsPerSide;
            int step = elapsed / moveInterval;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || !pawn.Spawned || pawn.Dead) continue;

                int room = (i * PatternStride + step * PatternStride * pawns.Count) % rooms;
                IntVec3 target = StressTestBuilder.RoomCentre(room);
                if (!target.InBounds(pawn.Map)) continue;

                pawn.Position = target;
                pawn.Notify_Teleported(false, true);
            }

            moves++;
        }

        public static void Advance()
        {
            if (!armed || phase >= 3) return;

            int now = Find.TickManager.TicksGame;
            if (startTick < 0)
            {
                startTick = now;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                Log.Message("[RoomAutoLight] benchmark armed (" + ConfigLabel + "), cadence "
                            + moveInterval + " ticks, warming up " + WarmupTicks + " ticks.");
            }

            int elapsed = now - startTick;
            DrivePawns(elapsed);

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
                Log.Message("[RoomAutoLight] pawn moves driven this run: " + moves
                            + " (identical across runs means the workload was standardised)");
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
