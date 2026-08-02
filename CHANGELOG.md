# Changelog

All notable changes to Room Auto Light. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased] - 1.0.1

### Performance

Measured in game on a purpose-built benchmark colony: a 10x10 lattice of 4x4 rooms, doors between
neighbours, **400 lamps**, wired and battery-backed, with drafted colonists teleported on a fixed
schedule across a sterilised map. Three consecutive runs produce an identical workload to the unit.
Both sides of each comparison ran in one process on the same map, switching the behaviour at
runtime, because separate launches vary more than the changes being measured. Full method and
results in `PERFORMANCE.md`.

On that colony the mod costs about **42 us per tick**, roughly **0.25% of a 60 TPS budget**, and
about **1.1 ms per room switch** — the majority of which is the engine's own glow work rather than
this mod's logic. Cost scales with how often rooms change state, not with how many lamps exist.

- **Glow invalidation is coalesced across a room switch.** Profiling put 78% of the mod's time in
  room switches costing 2.7 ms each — none of it in this mod's own logic. `GlowGrid.DirtyCell` does
  two `MapDrawer.MapMeshDirty` calls per cell, and `Register`/`DeRegisterGlower` call it for every
  cell of a glower's affected rect; a standing lamp is `glowRadius 12`, so a four-lamp room marked
  ~2500 cells for a union of about 730. Vanilla already batches the glow *computation* — one
  `ComputeGlowGridsJob` per frame however many glowers moved — so only the invalidation is touched
  here. A switch now collects the cells and replays the distinct set through vanilla's own
  `DirtyCell`.

  | | before | after | |
  | --- | --- | --- | --- |
  | per room switch | 1627.8 us | 796.4 us | 2.0x |
  | mean per tick | 59.1 us | 38.4 us | -35% |
  | worst tick | 4354.4 us | 1746.0 us | 2.5x |
  | share of a 60 TPS budget | 0.354% | 0.230% | |

- **Room outline is computed once per room per rebuild** instead of once per lamp. `Room.ExtentsClose`
  is not cached by the game and walks the room's whole region list on every read. Cuts rebuild cost
  about 3x (1502 us to 388 us), though rebuilds are rare enough — six ticks in eighteen hundred —
  that it barely moves the overall mean.
- **Rebuilds are debounced** to at most one per 30 ticks. Walls coming down during a raid fire
  `Room.Notify_RoomShapeChanged` repeatedly, which previously forced a full rebuild every tick for
  the duration.
- **The gizmo postfix no longer allocates for buildings that are not lamps.** An iterator method
  builds its state machine when called, not when first enumerated, so the early-outs were running
  too late and every selected building allocated a wrapper enumerator each frame.
- Per-rebuild collections are reused rather than reallocated, so a rebuild no longer allocates.

### Added

- **Silent switching.** RimWorld plays the power click — the same sound as a power failure —
  whenever a building gains or loses power. Reasonable for a hand-thrown switch, not for every room
  in the colony several times a minute, which players reported hearing constantly. Every power
  change this mod makes is now silent, from every path: a room switching itself, a rebuild handing a
  lamp back, an ungroup, a circuit reset, or the mod being switched off. Flicking a lamp by hand
  still clicks. Setting: `silentSwitching`.
- A benchmark harness behind a command-line flag, building the colony off map generation and
  reporting to the log without any interaction. Debug actions cover building it on an existing save
  and starting or stopping a recording. See `PERFORMANCE.md`.

## [1.0.0] - 2026-08-01

First public release, built against RimWorld 1.6.

### Added

- **Room groups.** Every lamp in a room is driven as a single switch. All members change state on the
  same tick; groups are staggered against each other so the work is spread without ever splitting a
  room across ticks.
- **Occupancy trigger.** A room lights while anyone is inside, with a configurable delay before going
  dark so a pawn passing through does not strobe the lights. A room that gains an occupant jumps the
  evaluation queue, so walking in lights the room within a quarter second.
- **All-or-nothing power.** A group only lights up if its power net can pay for every member that is
  still waiting, so a room never comes up half lit. Members already drawing are not counted, so a lit
  group always affords itself and cannot flicker; a shortfall is held for a retry delay so a marginal
  net cannot thrash.
- **Aggregated reporting.** A dark group draws nothing from the grid, and selecting any lamp reports
  the group as one load: lamp count, wattage, state, and the reason for it.
- **`Room lights` gizmo.** Cycles the group between `auto`, `always on` and `always off`.
- **`Schedule` gizmo.** Cycles `off`, `dusk to dawn` (celestial clock, so weather and eclipses are
  ignored) and `on darkness` (real sky glow, so an eclipse or a black storm at noon counts). While a
  schedule is set it is the only thing deciding.
- **`Sleepers` gizmo.** Cycles how sleeping occupants are treated: `dark if any asleep`,
  `dark if all asleep` or `ignored`. Never applied outdoors.
- **`Ungroup lamp` gizmo.** Pulls one lamp out of its room group and hands it back to vanilla, keeping
  it out of both the group switching and the all-or-nothing check. It stays visible on an ungrouped
  lamp so it can rejoin.
- **Outdoor group.** Every player-owned lamp standing outdoors forms one group, defaulting to
  `on darkness`, instead of trying to automate the map-sized outdoor room.
- **Grow room handover.** A room's ordinary lamps stand down while a sun lamp in the same room is lit,
  and take back over during its plant resting period, so nobody works in the dark. Sun lamps
  themselves are left to vanilla's own `CompProperties_Schedule`.
- **Circuit breaks on room changes.** A wall blown out, deconstructed or built changes the room under
  its lamps: they drop immediately and stay down until reset. Tracked per lamp on a signature of the
  room it was last seen in, so merges, splits and reshapes are all caught. A lamp seen for the first
  time is only recorded, so loading a save or building a lamp trips nothing.
- **`Reset lighting circuit` gizmo**, shown only on a lamp whose circuit is down. Repairs every broken
  lamp in that room at once.
- **"Lighting circuit damaged" alert**, listing the affected lamps as culprits so clicking it cycles
  through them.
- **Settings.** Animals as occupants, default sleeper handling, all-or-nothing power and its retry
  delay, whether room changes break circuits, the delay before going dark, the re-evaluation
  interval, the glow levels that count as dusk and as dark, outdoor grouping, the wattage ceiling,
  and defName include / exclude / grow-light lists.
- Per-group choices persist across saves and room rebuilds, anchored to a cell rather than a room id.

### Notes

- Lamps are held off through `FlickUtility.WantsToBeOn`, which vanilla already consults in
  `PowerNet.PowerNetTick`, in the `CompPowerTrader.PowerOn` setter, and in
  `CompGlower.ShouldBeLitNow`. One postfix there darkens the lamp and drops it off the grid with no
  per-tick fight, no synthetic flick, and no leftover "needs power" overlay.
- Turning a group on cannot be left to the power net: `PowerNetTick` restores at most 5% of the
  waiting parts (minimum one) once every 30 or more ticks, in random order, which lights a room lamp
  by lamp over several seconds. The group sets `PowerOn` on every member itself, in one pass.
- Managed lamps are plain `Building` things carrying both a glower and a power trader under the
  wattage ceiling, which picks up modded lamps while skipping sun lamps, workbenches, TVs and
  turrets.
- One room is evaluated per tick, occupancy is refreshed four times a second, rebuilds are debounced
  to at most one per 30 ticks, and nothing in the steady path allocates.

### Store assets

- `About/Preview.png`, generated by `tools/make-preview.ps1`.
- `About/gizmos.jpg`, an in-game shot of the group switches.
- `About/steam-description.txt`, the Workshop description in BBCode.
