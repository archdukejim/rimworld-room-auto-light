using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RoomAutoLight
{
    public class RoomLightManager : MapComponent
    {
        private const int RebuildIntervalTicks = 300;
        private const int OccupancyRefreshTicks = 15;

        private readonly HashSet<Building> registered = new HashSet<Building>();
        private readonly HashSet<Building> registeredGrowLights = new HashSet<Building>();
        private readonly Dictionary<int, RoomLightGroup> groups = new Dictionary<int, RoomLightGroup>();
        private readonly HashSet<int> occupiedRoomIds = new HashSet<int>();
        private readonly HashSet<int> urgentRoomIds = new HashSet<int>();

        // Player choices are keyed by a cell rather than a room id, because room ids are rebuilt
        // from scratch every time the map loads or a wall moves. The outdoor group has no room,
        // so it carries its preferences directly.
        private Dictionary<IntVec3, RoomLightPrefs> anchors = new Dictionary<IntVec3, RoomLightPrefs>();
        private RoomLightPrefs outdoorPrefs = new RoomLightPrefs(RoomLightMode.Auto, LightSchedule.Darkness);

        private List<RoomLightGroup>[] buckets = new List<RoomLightGroup>[1];
        private readonly List<Building> scratchLights = new List<Building>();
        private readonly List<int> scratchIds = new List<int>();

        private bool dirty = true;
        private bool wasEnabled = true;
        private int nextRebuildTick;
        private int lastOccupancyTick = -9999;

        public RoomLightManager(Map map) : base(map)
        {
            buckets[0] = new List<RoomLightGroup>();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref anchors, "anchors", LookMode.Value, LookMode.Deep);
            Scribe_Deep.Look(ref outdoorPrefs, "outdoorPrefs");
            if (anchors == null) anchors = new Dictionary<IntVec3, RoomLightPrefs>();
            if (outdoorPrefs == null) outdoorPrefs = new RoomLightPrefs(RoomLightMode.Auto, LightSchedule.Darkness);
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

        public void MarkDirty()
        {
            dirty = true;
        }

        public void NotifyDoorChanged(Building_Door door)
        {
            if (door == null || !door.Spawned) return;
            IntVec3 pos = door.Position;
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
            {
                IntVec3 cell = pos + GenAdj.CardinalDirections[i];
                if (!cell.InBounds(map)) continue;
                Room room = RegionAndRoomQuery.RoomAt(cell, map, RegionType.Set_Passable);
                if (room != null) urgentRoomIds.Add(room.ID);
            }
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
                RoomLightPrefs prefs = new RoomLightPrefs(group.mode, group.schedule);
                if (!prefs.IsDefault && anchorLight != null && anchorLight.Spawned)
                    anchors[anchorLight.Position] = prefs;
            }

            int now = Find.TickManager.TicksGame;
            RefreshOccupancy(now, true);
            group.Evaluate(now, occupiedRoomIds.Contains(group.roomId));
        }

        private void ClearAnchorsFor(int roomId)
        {
            if (anchors.Count == 0) return;
            List<IntVec3> toRemove = new List<IntVec3>();
            foreach (KeyValuePair<IntVec3, RoomLightPrefs> pair in anchors)
            {
                Room room = RegionAndRoomQuery.RoomAtOrAdjacent(pair.Key, map, RegionType.Set_Passable);
                if (room != null && room.ID == roomId) toRemove.Add(pair.Key);
            }
            for (int i = 0; i < toRemove.Count; i++) anchors.Remove(toRemove[i]);
        }

        public override void MapComponentTick()
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

            if (dirty || now >= nextRebuildTick)
            {
                RebuildGroups();
                nextRebuildTick = now + RebuildIntervalTicks;
            }

            RefreshOccupancy(now, false);

            if (urgentRoomIds.Count > 0)
            {
                foreach (int roomId in urgentRoomIds)
                {
                    RoomLightGroup group;
                    if (groups.TryGetValue(roomId, out group))
                        group.Evaluate(now, occupiedRoomIds.Contains(roomId));
                }
                urgentRoomIds.Clear();
            }

            List<RoomLightGroup> bucket = buckets[BucketIndex(now, buckets.Length)];
            for (int i = 0; i < bucket.Count; i++)
            {
                RoomLightGroup group = bucket[i];
                group.Evaluate(now, occupiedRoomIds.Contains(group.roomId));
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

            occupiedRoomIds.Clear();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!RoomLightUtility.CountsAsOccupant(pawn)) continue;
                Room room = RegionAndRoomQuery.RoomAt(pawn.Position, map, RegionType.Set_Passable);
                if (room == null) continue;

                if (RoomLightUtility.IsOutdoors(room)) occupiedRoomIds.Add(RoomLightGroup.OutdoorGroupId);
                else occupiedRoomIds.Add(room.ID);
            }
        }

        private void RebuildGroups()
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

            List<Building> stale = null;
            HashSet<Building> assigned = new HashSet<Building>();

            foreach (Building light in registered)
            {
                if (light == null || !light.Spawned || light.Map != map || light.Destroyed
                    || !RoomLightUtility.IsManagedLightDef(light.def))
                {
                    if (stale == null) stale = new List<Building>();
                    stale.Add(light);
                    continue;
                }

                Room room = RoomLightUtility.RoomOf(light);
                if (room == null) continue;

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

            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    LightSuppression.Release(stale[i]);
                    registered.Remove(stale[i]);
                }
            }

            for (int i = 0; i < scratchLights.Count; i++)
            {
                Building light = scratchLights[i];
                if (!assigned.Contains(light)) LightSuppression.Release(light);
            }

            scratchIds.Clear();
            foreach (KeyValuePair<int, RoomLightGroup> pair in groups)
                if (pair.Value.lights.Count == 0) scratchIds.Add(pair.Key);
            for (int i = 0; i < scratchIds.Count; i++) groups.Remove(scratchIds[i]);

            AssignGrowLights();
            ApplyPrefs();
            RebuildBuckets();
        }

        /// <summary>
        /// Runs after groups are pruned: grow lights only ever attach to a group that already
        /// exists, since a grow room with no ordinary lamps has nothing to switch.
        /// </summary>
        private void AssignGrowLights()
        {
            if (registeredGrowLights.Count == 0) return;

            List<Building> stale = null;
            foreach (Building growLight in registeredGrowLights)
            {
                if (growLight == null || !growLight.Spawned || growLight.Map != map || growLight.Destroyed
                    || !RoomLightUtility.IsGrowLightDef(growLight.def))
                {
                    if (stale == null) stale = new List<Building>();
                    stale.Add(growLight);
                    continue;
                }

                Room room = RoomLightUtility.RoomOf(growLight);
                if (room == null || RoomLightUtility.IsOutdoors(room)) continue;

                RoomLightGroup group;
                if (groups.TryGetValue(room.ID, out group)) group.growLights.Add(growLight);
            }

            if (stale == null) return;
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
                }
                else
                {
                    group.mode = RoomLightMode.Auto;
                    group.schedule = LightSchedule.None;
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
            RebuildBuckets();
            dirty = true;
        }
    }
}
