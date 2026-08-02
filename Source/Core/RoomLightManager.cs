using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RoomAutoLight
{
    public class RoomLightManager : MapComponent
    {
        private const int RebuildIntervalTicks = 300;
        private const int OccupancyRefreshTicks = 15;

        // Walls coming down during a raid fire Room.Notify_RoomShapeChanged over and over. Without
        // a floor on the rebuild rate that turns into a full rebuild every tick for the duration.
        private const int RebuildDebounceTicks = 30;

        private readonly HashSet<Building> registered = new HashSet<Building>();
        private readonly HashSet<Building> registeredGrowLights = new HashSet<Building>();
        private readonly Dictionary<int, RoomLightGroup> groups = new Dictionary<int, RoomLightGroup>();
        // Occupancy has to be tracked twice over, because whether a sleeper counts is a per-room
        // choice: one set of rooms holding someone awake, one holding someone asleep.
        private readonly HashSet<int> roomsWithAwake = new HashSet<int>();
        private readonly HashSet<int> roomsWithSleeper = new HashSet<int>();
        private readonly HashSet<int> urgentRoomIds = new HashSet<int>();

        // Player choices are keyed by a cell rather than a room id, because room ids are rebuilt
        // from scratch every time the map loads or a wall moves. The outdoor group has no room,
        // so it carries its preferences directly.
        private Dictionary<IntVec3, RoomLightPrefs> anchors = new Dictionary<IntVec3, RoomLightPrefs>();

        // Lamps the player has pulled out of their room's group, by thing id, which is stable
        // across saves. These fall back to plain vanilla behaviour.
        private HashSet<int> ungroupedIds = new HashSet<int>();

        // Lamps whose room changed shape or identity under them: the circuit counts as damaged,
        // so they are held dark and out of any group until the player resets them. Persisted, or
        // a save and reload would repair every circuit for free.
        private HashSet<int> brokenIds = new HashSet<int>();

        // What room each lamp was last seen in, as room id combined with the room's outline.
        // Runtime only: on load every lamp is simply recorded afresh, so loading a save can never
        // trip a break.
        private readonly Dictionary<int, int> lastRoomSignature = new Dictionary<int, int>();

        // Kept ready for the alert, which is polled on a stagger and should not have to walk every
        // registered lamp each time it is asked.
        private readonly List<Thing> brokenLights = new List<Thing>();
        private RoomLightPrefs outdoorPrefs =
            new RoomLightPrefs(RoomLightMode.Auto, LightSchedule.Darkness, SleepDarkening.Never);

        private List<RoomLightGroup>[] buckets = new List<RoomLightGroup>[1];

        // Reused across rebuilds rather than reallocated; a rebuild can run as often as the
        // debounce allows while a base is being reshaped.
        private readonly List<Building> scratchLights = new List<Building>();
        private readonly List<int> scratchIds = new List<int>();
        private readonly HashSet<Building> scratchAssigned = new HashSet<Building>();
        private readonly List<Building> scratchStale = new List<Building>();
        private readonly List<IntVec3> scratchAnchorCells = new List<IntVec3>();
        private readonly HashSet<int> previouslyOccupied = new HashSet<int>();

        // Room.ExtentsClose is not cached by the game: every read walks the room's whole region
        // list. The fingerprint is per room but asked for per lamp, so it is memoised for the
        // duration of one rebuild.
        private readonly Dictionary<int, int> fingerprintThisPass = new Dictionary<int, int>();

        private int profiledGroupEvals;
        private bool dirty = true;
        private bool wasEnabled = true;
        private int nextRebuildTick;
        private int earliestRebuildTick;
        private int lastOccupancyTick = -9999;

        public RoomLightManager(Map map) : base(map)
        {
            buckets[0] = new List<RoomLightGroup>();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref anchors, "anchors", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref ungroupedIds, "ungroupedIds", LookMode.Value);
            Scribe_Collections.Look(ref brokenIds, "brokenIds", LookMode.Value);
            if (ungroupedIds == null) ungroupedIds = new HashSet<int>();
            if (brokenIds == null) brokenIds = new HashSet<int>();
            Scribe_Deep.Look(ref outdoorPrefs, "outdoorPrefs");
            if (anchors == null) anchors = new Dictionary<IntVec3, RoomLightPrefs>();
            if (outdoorPrefs == null)
                outdoorPrefs = new RoomLightPrefs(RoomLightMode.Auto, LightSchedule.Darkness, SleepDarkening.Never);
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // Safety net: CompGlower.PostSpawnSetup normally registers everything on load,
            // but a mid-save install should not have to wait for a respawn.
            List<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i] as Building;
                if (building == null) continue;
                if (RoomLightUtility.IsManagedLight(building)) registered.Add(building);
                else if (RoomLightUtility.IsGrowLight(building)) registeredGrowLights.Add(building);
            }
            dirty = true;
        }

        public override void MapGenerated()
        {
            base.MapGenerated();
            if (!StressTestBuilder.RequestedOnMapGen) return;

            StressTestBuilder.Build(map);
            StressBenchmark.Arm();
        }

        public override void MapRemoved()
        {
            ReleaseEverything();
            base.MapRemoved();
        }

        public void Register(Building light)
        {
            if (registered.Add(light)) dirty = true;
        }

        public void Unregister(Building light)
        {
            LightSuppression.Release(light);
            lastRoomSignature.Remove(light.thingIDNumber);
            if (brokenIds.Remove(light.thingIDNumber)) brokenLights.Remove(light);
            if (registered.Remove(light)) dirty = true;
        }

        public void RegisterGrowLight(Building growLight)
        {
            if (registeredGrowLights.Add(growLight)) dirty = true;
        }

        public void UnregisterGrowLight(Building growLight)
        {
            if (registeredGrowLights.Remove(growLight)) dirty = true;
        }

        public bool IsUngrouped(Building light)
        {
            return light != null && ungroupedIds.Contains(light.thingIDNumber);
        }

        public void SetUngrouped(Building light, bool ungrouped)
        {
            if (light == null) return;
            if (ungrouped) ungroupedIds.Add(light.thingIDNumber);
            else ungroupedIds.Remove(light.thingIDNumber);

            // Hand it back to vanilla straight away rather than after the next rebuild.
            if (ungrouped)
            {
                LightSuppression.Unsuppress(light);
                LightSuppression.PowerUp(light);
            }
            dirty = true;
        }

        public void MarkDirty()
        {
            dirty = true;
        }

        public RoomLightGroup GroupFor(Building light)
        {
            Room room = RoomLightUtility.RoomOf(light);
            if (room == null) return null;

            int key = RoomLightUtility.IsOutdoors(room) ? RoomLightGroup.OutdoorGroupId : room.ID;
            RoomLightGroup group;
            if (!groups.TryGetValue(key, out group)) return null;
            return group.lights.Contains(light) ? group : null;
        }

        public void SetMode(RoomLightGroup group, RoomLightMode mode, Building anchorLight)
        {
            if (group == null) return;
            group.mode = mode;
            CommitPrefs(group, anchorLight);
        }

        public void SetSchedule(RoomLightGroup group, LightSchedule schedule, Building anchorLight)
        {
            if (group == null) return;
            group.schedule = schedule;
            CommitPrefs(group, anchorLight);
        }

        public void SetSleepDarkening(RoomLightGroup group, SleepDarkening sleepDarkening, Building anchorLight)
        {
            if (group == null) return;
            group.sleepDarkening = sleepDarkening;
            CommitPrefs(group, anchorLight);
        }

        private void CommitPrefs(RoomLightGroup group, Building anchorLight)
        {
            if (group.isOutdoor)
            {
                outdoorPrefs.mode = group.mode;
                outdoorPrefs.schedule = group.schedule;
            }
            else
            {
                ClearAnchorsFor(group.roomId);
                RoomLightPrefs prefs =
                    new RoomLightPrefs(group.mode, group.schedule, group.sleepDarkening);
                if (!prefs.IsDefault && anchorLight != null && anchorLight.Spawned)
                    anchors[anchorLight.Position] = prefs;
            }

            int now = Find.TickManager.TicksGame;
            RefreshOccupancy(now, true);
            group.Evaluate(now, IsOccupied(group), roomsWithSleeper.Contains(group.roomId));
        }

        private void ClearAnchorsFor(int roomId)
        {
            if (anchors.Count == 0) return;
            scratchAnchorCells.Clear();
            foreach (KeyValuePair<IntVec3, RoomLightPrefs> pair in anchors)
            {
                if (!pair.Key.InBounds(map)) continue;
                Room room = RegionAndRoomQuery.RoomAtOrAdjacent(pair.Key, map, RegionType.Set_Passable);
                if (room != null && room.ID == roomId) scratchAnchorCells.Add(pair.Key);
            }
            for (int i = 0; i < scratchAnchorCells.Count; i++) anchors.Remove(scratchAnchorCells[i]);
        }

        public override void MapComponentTick()
        {
            if (StressBenchmark.Running) StressBenchmark.Advance();

            if (!RoomLightProfiler.Enabled)
            {
                TickInternal();
                return;
            }

            long start = RoomLightProfiler.Now();
            int before = profiledGroupEvals;
            TickInternal();
            RoomLightProfiler.RecordTick(start, profiledGroupEvals - before);
        }

        private void TickInternal()
        {
            RoomAutoLightSettings settings = RoomAutoLightMod.Settings;

            if (!settings.enabled)
            {
                if (wasEnabled)
                {
                    ReleaseEverything();
                    wasEnabled = false;
                }
                return;
            }
            if (!wasEnabled)
            {
                wasEnabled = true;
                dirty = true;
            }

            int now = Find.TickManager.TicksGame;

            if ((dirty && now >= earliestRebuildTick) || now >= nextRebuildTick)
            {
                long rebuildStart = RoomLightProfiler.Enabled ? RoomLightProfiler.Now() : 0L;
                RebuildGroups(now);
                if (RoomLightProfiler.Enabled) RoomLightProfiler.RecordRebuild(rebuildStart);

                nextRebuildTick = now + RebuildIntervalTicks;
                earliestRebuildTick = now + RebuildDebounceTicks;
            }

            long occupancyStart = RoomLightProfiler.Enabled ? RoomLightProfiler.Now() : 0L;
            RefreshOccupancy(now, false);
            if (RoomLightProfiler.Enabled) RoomLightProfiler.RecordOccupancy(occupancyStart);

            if (urgentRoomIds.Count > 0)
            {
                foreach (int roomId in urgentRoomIds)
                {
                    RoomLightGroup group;
                    if (groups.TryGetValue(roomId, out group))
                    {
                        group.Evaluate(now, IsOccupied(group), roomsWithSleeper.Contains(group.roomId));
                        profiledGroupEvals++;
                    }
                }
                urgentRoomIds.Clear();
            }

            List<RoomLightGroup> bucket = buckets[BucketIndex(now, buckets.Length)];
            for (int i = 0; i < bucket.Count; i++)
            {
                RoomLightGroup group = bucket[i];
                group.Evaluate(now, IsOccupied(group), roomsWithSleeper.Contains(group.roomId));
                profiledGroupEvals++;
            }
        }

        private static int BucketIndex(int value, int slots)
        {
            return ((value % slots) + slots) % slots;
        }

        private void RefreshOccupancy(int now, bool force)
        {
            if (!force && now - lastOccupancyTick < OccupancyRefreshTicks) return;
            lastOccupancyTick = now;

            previouslyOccupied.Clear();
            foreach (int id in roomsWithAwake) previouslyOccupied.Add(id);

            roomsWithAwake.Clear();
            roomsWithSleeper.Clear();

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!RoomLightUtility.CountsAsOccupant(pawn)) continue;

                Room room = RegionAndRoomQuery.RoomAt(pawn.Position, map, RegionType.Set_Passable);
                if (room == null) continue;

                bool outdoors = RoomLightUtility.IsOutdoors(room);
                int id = outdoors ? RoomLightGroup.OutdoorGroupId : room.ID;

                if (RoomLightUtility.IsSleeping(pawn, outdoors)) roomsWithSleeper.Add(id);
                else roomsWithAwake.Add(id);
            }

            // Walking into a dark room should not wait for the room's slot to come round, so any
            // room that just gained someone awake jumps the queue.
            foreach (int id in roomsWithAwake)
                if (!previouslyOccupied.Contains(id)) urgentRoomIds.Add(id);
        }

        /// <summary>Resolves the group's sleep rule against this tick's occupancy.</summary>
        private bool IsOccupied(RoomLightGroup group)
        {
            bool awake = roomsWithAwake.Contains(group.roomId);
            bool sleeper = roomsWithSleeper.Contains(group.roomId);

            switch (group.sleepDarkening)
            {
                case SleepDarkening.Never: return awake || sleeper;
                case SleepDarkening.IfAny: return awake && !sleeper;
                default: return awake;
            }
        }

        private void RebuildGroups(int now)
        {
            dirty = false;
            RoomAutoLightSettings settings = RoomAutoLightMod.Settings;

            // Anything that was in a group before and is not in one afterwards must be handed
            // back to vanilla, or it stays dark forever.
            scratchLights.Clear();
            foreach (KeyValuePair<int, RoomLightGroup> pair in groups)
            {
                scratchLights.AddRange(pair.Value.lights);
                pair.Value.lights.Clear();
                pair.Value.growLights.Clear();
            }

            List<Building> stale = scratchStale;
            HashSet<Building> assigned = scratchAssigned;
            stale.Clear();
            assigned.Clear();
            fingerprintThisPass.Clear();

            foreach (Building light in registered)
            {
                if (light == null || !light.Spawned || light.Map != map || light.Destroyed
                    || !RoomLightUtility.IsManagedLightDef(light.def))
                {
                    stale.Add(light);
                    continue;
                }

                if (ungroupedIds.Contains(light.thingIDNumber)) continue;

                Room room = RoomLightUtility.RoomOf(light);
                if (room == null) continue;

                // The room moving under a lamp is the signal, not the room object surviving. A
                // blast that merges a room into the outdoors makes brand new rooms, so watching a
                // group's own room for reshaping would miss the very case this is here for.
                if (settings.breakOnRoomChange && NoteRoomAndCheckBreak(light, room)) continue;
                if (IsBroken(light)) continue;

                RoomLightGroup group;
                if (RoomLightUtility.IsOutdoors(room))
                {
                    if (!settings.manageOutdoorLamps) continue;
                    if (!RoomLightUtility.IsPlayerOwned(light)) continue;

                    if (!groups.TryGetValue(RoomLightGroup.OutdoorGroupId, out group))
                    {
                        group = new RoomLightGroup(map);
                        groups[RoomLightGroup.OutdoorGroupId] = group;
                    }
                }
                else
                {
                    if (!RoomLightUtility.IsAutomatableRoom(room)) continue;
                    if (!groups.TryGetValue(room.ID, out group))
                    {
                        group = new RoomLightGroup(map, room);
                        groups[room.ID] = group;
                    }
                    group.room = room;
                }

                group.lights.Add(light);
                assigned.Add(light);
            }

            for (int i = 0; i < stale.Count; i++)
            {
                LightSuppression.Release(stale[i]);
                registered.Remove(stale[i]);
            }

            for (int i = 0; i < scratchLights.Count; i++)
            {
                Building light = scratchLights[i];
                if (assigned.Contains(light)) continue;
                if (IsBroken(light)) continue;
                LightSuppression.Release(light);
            }

            // Broken lamps belong to no group, so nothing else would hold them dark.
            if (brokenIds.Count > 0 && settings.breakOnRoomChange)
            {
                foreach (Building light in registered)
                    if (brokenIds.Contains(light.thingIDNumber)) LightSuppression.TurnOff(light);
            }

            scratchIds.Clear();
            foreach (KeyValuePair<int, RoomLightGroup> pair in groups)
                if (pair.Value.lights.Count == 0) scratchIds.Add(pair.Key);
            for (int i = 0; i < scratchIds.Count; i++) groups.Remove(scratchIds[i]);

            AssignGrowLights();
            ApplyPrefs();
            RebuildBuckets();
            RefreshBrokenCache();
        }

        /// <summary>
        /// Records the room a lamp is standing in and reports whether it moved. A lamp seen for
        /// the first time is only recorded, so loading a save or building a new lamp never trips
        /// anything. Returns true if this lamp is now broken.
        /// </summary>
        private bool NoteRoomAndCheckBreak(Building light, Room room)
        {
            int id = light.thingIDNumber;

            int fingerprint;
            if (RoomLightProfiler.BypassFingerprintCache)
            {
                fingerprint = RoomLightUtility.RoomFingerprint(room);
            }
            else if (!fingerprintThisPass.TryGetValue(room.ID, out fingerprint))
            {
                fingerprint = RoomLightUtility.RoomFingerprint(room);
                fingerprintThisPass[room.ID] = fingerprint;
            }
            int signature = room.ID * 397 ^ fingerprint;

            int previous;
            if (!lastRoomSignature.TryGetValue(id, out previous))
            {
                lastRoomSignature[id] = signature;
                return false;
            }
            if (previous == signature) return false;

            lastRoomSignature[id] = signature;
            if (!brokenIds.Add(id)) return true;

            // No off-delay: as far as the colony is concerned the circuit just took a hit.
            LightSuppression.TurnOff(light);
            return true;
        }

        /// <summary>Lamps whose circuit is down, ready for the alert to read.</summary>
        public List<Thing> BrokenLights
        {
            get { return brokenLights; }
        }

        private void RefreshBrokenCache()
        {
            brokenLights.Clear();
            if (brokenIds.Count == 0) return;
            foreach (Building light in registered)
                if (IsBroken(light) && light.Spawned) brokenLights.Add(light);
        }

        /// <summary>
        /// Checks the setting too, so turning the mechanic off puts every damaged circuit straight
        /// back into service without losing the record, in case it is turned back on.
        /// </summary>
        public bool IsBroken(Building light)
        {
            return light != null
                   && RoomAutoLightMod.Settings.breakOnRoomChange
                   && brokenIds.Contains(light.thingIDNumber);
        }

        /// <summary>
        /// Resets every broken lamp sharing this lamp's room, so one click repairs the room rather
        /// than one lamp at a time.
        /// </summary>
        public void ResetCircuitAt(Building light)
        {
            if (light == null) return;
            Room room = RoomLightUtility.RoomOf(light);

            scratchStale.Clear();
            foreach (Building candidate in registered)
            {
                if (!brokenIds.Contains(candidate.thingIDNumber)) continue;
                if (room != null && RoomLightUtility.RoomOf(candidate) != room) continue;
                scratchStale.Add(candidate);
            }

            for (int i = 0; i < scratchStale.Count; i++)
            {
                Building repaired = scratchStale[i];
                brokenIds.Remove(repaired.thingIDNumber);
                LightSuppression.Unsuppress(repaired);
            }
            scratchStale.Clear();
            RefreshBrokenCache();

            dirty = true;
            earliestRebuildTick = 0;
        }

        /// <summary>
        /// Runs after groups are pruned: grow lights only ever attach to a group that already
        /// exists, since a grow room with no ordinary lamps has nothing to switch.
        /// </summary>
        private void AssignGrowLights()
        {
            if (registeredGrowLights.Count == 0) return;

            List<Building> stale = scratchStale;
            stale.Clear();
            foreach (Building growLight in registeredGrowLights)
            {
                if (growLight == null || !growLight.Spawned || growLight.Map != map || growLight.Destroyed
                    || !RoomLightUtility.IsGrowLightDef(growLight.def))
                {
                    stale.Add(growLight);
                    continue;
                }

                Room room = RoomLightUtility.RoomOf(growLight);
                if (room == null || RoomLightUtility.IsOutdoors(room)) continue;

                RoomLightGroup group;
                if (groups.TryGetValue(room.ID, out group)) group.growLights.Add(growLight);
            }

            for (int i = 0; i < stale.Count; i++) registeredGrowLights.Remove(stale[i]);
        }

        private void ApplyPrefs()
        {
            foreach (KeyValuePair<int, RoomLightGroup> pair in groups)
            {
                RoomLightGroup group = pair.Value;
                if (group.isOutdoor)
                {
                    group.mode = outdoorPrefs.mode;
                    group.schedule = outdoorPrefs.schedule;
                    group.sleepDarkening = SleepDarkening.Never;
                }
                else
                {
                    group.mode = RoomLightMode.Auto;
                    group.schedule = LightSchedule.None;
                    group.sleepDarkening = RoomAutoLightMod.Settings.defaultSleepDarkening;
                }
            }

            foreach (KeyValuePair<IntVec3, RoomLightPrefs> anchor in anchors)
            {
                if (!anchor.Key.InBounds(map)) continue;
                Room room = RegionAndRoomQuery.RoomAtOrAdjacent(anchor.Key, map, RegionType.Set_Passable);
                if (room == null) continue;
                RoomLightGroup group;
                if (!groups.TryGetValue(room.ID, out group)) continue;
                group.mode = anchor.Value.mode;
                group.schedule = anchor.Value.schedule;
                group.sleepDarkening = anchor.Value.sleepDarkening;
            }
        }

        private void RebuildBuckets()
        {
            int interval = RoomAutoLightMod.Settings.updateIntervalTicks;
            if (interval < 1) interval = 1;

            if (buckets.Length != interval)
            {
                buckets = new List<RoomLightGroup>[interval];
                for (int i = 0; i < interval; i++) buckets[i] = new List<RoomLightGroup>();
            }
            else
            {
                for (int i = 0; i < buckets.Length; i++) buckets[i].Clear();
            }

            // Spread groups across the window; every lamp inside one group still shares a tick.
            foreach (KeyValuePair<int, RoomLightGroup> pair in groups)
                buckets[BucketIndex(pair.Key, interval)].Add(pair.Value);
        }

        private void ReleaseEverything()
        {
            foreach (KeyValuePair<int, RoomLightGroup> pair in groups) pair.Value.ReleaseAll();
            groups.Clear();
            urgentRoomIds.Clear();

            // Broken lamps sit in no group, so they would otherwise stay dark after the mod is
            // switched off. The break itself is kept, in case it is switched back on.
            LightSuppression.ReleaseAll();

            RebuildBuckets();
            dirty = true;
        }
    }
}
