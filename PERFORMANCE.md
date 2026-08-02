# Performance

Every figure below comes from a purpose-built benchmark colony, measured in game, on RimWorld 1.6.
Nothing here is estimated from reading code.

## The benchmark

A dedicated stress colony is built during map generation, so a run needs no interaction and every
run measures the same thing:

- **A 10 x 10 lattice of rooms**, each 4 x 4 with a door to each neighbour — 100 rooms.
- **400 lamps**, four in the corners of every room.
- **A conduit lattice and charged batteries**, so the grid is genuinely powered and the mod's
  power checks have a real network to walk. Batteries rather than generators, so a run is not
  quietly measuring a day/night brownout.
- **12 colonists**, drafted so their own AI never moves them, then teleported through a fixed tour
  on a fixed cadence. Wandering colonists were the single largest source of run-to-run variance:
  light switches are most of the cost, and pawns chose a different number of them every run.
- **A sterilised map.** Everything outside the lattice is removed — plants, wildlife, chunks, ruins
  — terrain is levelled, and weather is pinned. Before this, a *do-nothing* control run varied 43%
  between launches, because the benchmark was mostly measuring thousands of ticking plants and wild
  animals rather than lamps.

### Is it actually repeatable?

Yes, to the unit. Three consecutive runs of the same configuration:

| Run | Room switches | Glow invalidations |
| --- | --- | --- |
| 1 | 66 | 216,744 |
| 2 | 66 | 216,744 |
| 3 | 66 | 216,744 |

Identical workload every time. That is what makes the comparisons below meaningful.

One metric was **discarded** as unfit: total game tick time. It swung 28–43% between identical
runs, and in one configuration scored *below* a control that did nothing at all. The reason is
structural — the expensive part of a light change is a per-**frame** job, and how much of it lands
inside any given tick is arbitrary. Counting the work requested is stable; timing the tick it
happens to land in is not.

## What the tests found

### 1. Where the time actually goes

Splitting the mod's own tick by phase, on 400 lamps:

| Phase | Share of mod time |
| --- | --- |
| Deciding whether a room should be lit | 0.9% |
| Checking the room can afford its power | 1.2% |
| Applying the switch | **84.7%** |
| Rebuilding the room-to-lamp map | 1.4% |
| Scanning for occupants | 4.9% |

Almost none of the cost is the mod's own logic. It is the act of changing a light.

### 2. Real switches versus idle re-checks

Splitting the apply phase again:

| | Calls | Cost each |
| --- | --- | --- |
| Real state changes | 54 | 2694.5 us |
| Idle re-checks | 2715 | 4.7 us |

The mod re-checks rooms constantly and that is nearly free. All the cost is in genuine switches.

### 3. Why a switch was so expensive — and the fix

RimWorld invalidates glow **per lamp, over the lamp's entire lit area**. A standing lamp reaches 12
cells, so each one marks roughly 625 cells, twice over. Four lamps in one small room have nearly
identical lit areas, so a room switch was marking about 2,500 cells to cover a union of about 730 —
the same cells, four times.

The engine already batches the glow *computation* itself, so that was left alone. Only the
invalidation was unbatched. A room switch now collects the affected cells and hands the distinct set
to the engine once.

| | Before | After | |
| --- | --- | --- | --- |
| Per room switch | 1627.8 us | 796.4 us | **2.0x** |
| Mean per tick | 59.1 us | 38.4 us | **-35%** |
| Worst tick | 4354.4 us | 1746.0 us | **2.5x** |

Measured in one process on one map, switching the behaviour at runtime, because two separate
launches vary more than the change being measured.

### 4. Smaller wins

- **Room outline cached per rebuild** instead of recomputed per lamp — about 3x off rebuild cost.
  Honest caveat: rebuilds are six ticks in eighteen hundred, so this barely moves the total.
- **Rebuilds debounced** to at most one per 30 ticks. Walls coming down during a raid previously
  forced a full rebuild every tick for the duration.
- **No allocation when a building is selected.** The gizmo hook was allocating for every selected
  building, lamp or not, every frame.
- **Nothing in the steady path allocates.**

## What it costs

On the benchmark colony — **400 lamps, 100 rooms, 66 room switches per 30 seconds of game time**:

- **About 42 microseconds per tick**, roughly **0.25% of a 60 TPS tick budget**.
- **About 1.1 milliseconds per room switch**, the large majority of which is the engine's own glow
  work rather than this mod's logic.
- One room is evaluated per tick regardless of colony size; rooms are spread across the evaluation
  window rather than all checked together.
- Occupancy is swept four times a second, once, for the whole map.

The cost scales with **how often rooms change state**, not with how many lamps you own. A colony
with 400 lamps that rarely change is close to free.

## Quiet switching

Switching a light in RimWorld plays the power click — the same sound as a power failure. That is
reasonable when a player flicks a switch by hand; it is not reasonable several times a minute,
forever, for every room in the colony. Players reported hearing it constantly.

Every power change this mod makes is now silent, from every path: a room switching itself, a rebuild
handing a lamp back, ungrouping a lamp, resetting a damaged circuit, or switching the mod off.
Flicking a lamp by hand still clicks, exactly as vanilla does. It can be turned off in settings.

## On comparing against other lighting mods

Another mod in this space was benchmarked on the identical colony. The result is being reported as a
**difference in behaviour, not a speed claim**, because the two do not do the same amount of work
and any raw cost comparison would mislead.

That mod adapts vanilla's existing switch mechanic lazily: each lamp carries its own component and
decides for itself on roughly a four-second timer, staggered against every other lamp. The
consequences are behavioural:

- **A room does not light as a unit.** Each lamp reaches its own decision at its own moment, so
  lamps arrive piecemeal and there is no point at which a room is guaranteed to be fully lit.
- **A lamp may wait for its next check before responding**, rather than responding when someone
  walks in.
- Over an identical benchmark window it performed roughly **a third** of the light switching this
  mod did — consistent with lamps responding late, or not at all, within a given visit.

Doing a third of the work naturally costs less. That is a design difference — responsiveness and
whole-room consistency traded against doing less — and quoting a cost figure without that context
would be misleading in either direction. So no head-to-head performance number is published here.

This mod takes the other trade: a room comes up **as a unit, on a single tick**, and the engineering
above exists to make that affordable.

## Reproducing any of this

The harness ships with the mod behind a command-line flag, builds the colony during map generation
and writes its results to the log without any interaction:

```
RimWorldWin64.exe -quicktest -roomautolight-stress
```

`-roomautolight-cadence=N` sets the teleport interval. `-roomautolight-passive` builds the same
colony with this mod's automation switched off, which is how the control and the comparison runs
were measured. Debug actions cover building the colony on an existing save and starting or stopping
a recording.
