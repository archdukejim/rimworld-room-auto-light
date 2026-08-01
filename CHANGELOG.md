# Changelog

All notable changes to Room Auto Light. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Room groups.** Every lamp in a room is driven as a single switch. All members change state on the
  same tick; groups are staggered against each other so the work is spread without ever splitting a
  room across ticks.
- **Door trigger.** Any door touching a room being physically open lights the whole group, driven by
  an event on the door rather than by polling.
- **Occupancy trigger.** A room stays lit while anyone is inside, with a configurable delay before
  going dark so a pawn walking through does not strobe the lights.
- **Aggregated power.** A dark group draws nothing from the grid, and selecting any lamp reports the
  group as one load: lamp count, wattage, state, and the reason for it.
- **`Room lights` gizmo.** Cycles the group between `auto`, `always on`, and `always off`.
- **`Schedule` gizmo.** Cycles `off`, `dusk to dawn` (celestial clock, ignores weather), and
  `on darkness` (sky glow, so an eclipse or a black storm at noon counts). While a schedule is set it
  is the only thing deciding.
- **`Trigger` gizmo.** Cycles which signals drive the group on auto: `combined`, `occupancy`, or
  `doors`.
- **`Sleepers` gizmo.** Cycles how sleeping occupants are treated: `dark if any asleep`,
  `dark if all asleep`, or `ignored`. Never applied outdoors.
- **Outdoor group.** Every player-owned lamp standing outdoors forms one group, defaulting to
  `on darkness`, instead of trying to automate the map-sized outdoor room.
- **Grow room handover.** A room's ordinary lamps stand down while a sun lamp in the same room is
  lit, and take back over during its plant resting period, so nobody works in the dark. Sun lamps
  themselves are left to vanilla's own `CompProperties_Schedule`.
- **Settings.** Default trigger link, held-open door handling, animals as occupants, default sleeper
  handling, the delay before going dark, the re-evaluation interval, the glow levels that count as
  dusk and as dark, outdoor grouping, the wattage ceiling, and defName include / exclude / grow-light
  lists.
- Per-group choices persist across saves and room rebuilds, anchored to a cell rather than a room id.

### Notes

- Lamps are held off through `FlickUtility.WantsToBeOn`, which vanilla already consults in
  `PowerNet.PowerNetTick`, in the `CompPowerTrader.PowerOn` setter, and in
  `CompGlower.ShouldBeLitNow`. One postfix there darkens the lamp and drops it off the grid with no
  per-tick fight, no synthetic flick, and no leftover "needs power" overlay.
- Managed lamps are plain `Building` things carrying both a glower and a power trader under the
  wattage ceiling, which picks up modded lamps while skipping sun lamps, workbenches, TVs and
  turrets.
- Built against RimWorld 1.6.
