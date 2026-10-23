# ADR-0003. Physics: Jitter2 behind our own facade, not a JigLibX port

- **Status:** accepted (2026-10-24, after the spike below)
- **Date:** 2026-10-24
- **Tasks:** OM-7, OM-29

## Context

The original uses JigLibX 0.0.2.0 (copyright 2008) with Magicka's own additions on top
([Engine architecture](../engine/ARCHITECTURE.md) §7, §13). ADR-0001 planned a port of upstream
JigLibX once its license was confirmed, and BepuPhysics v2 otherwise.

### The JigLibX license cannot be confirmed

- CodePlex is closed and its archive page for the project does not open.
- The surviving copies of the source (for example, in game repositories on GitHub) have no license
  file. Their file header has copyright lines (Thorben Linneweber; JigLib by Danny Chapman) and
  no license.
- The game's `JigLibX.dll` names no license in its assembly attributes.
- Second-hand lists disagree: MIT in one blog, while the upstream JigLib is zlib and the JigLibSDX
  fork is Ms-PL.

Source without a license grant cannot be ported. Porting from the game's copy would break the
clean-room rule. **A JigLibX port is not an option.**

### What the game needs

Counted from the original (§7 and [Entities](../engine/ENTITIES_AI.md)):

| Need | Notes |
|---|---|
| Primitives | capsules (characters, the most common), spheres, boxes, static triangle meshes |
| Level collision | 10 static meshes per level, one per material; the hit triangle's mesh selects the material |
| Queries | segment and ray tests against the level and bodies: the most used call, by far (AI line of sight, spells, ground checks) |
| Character body | a capsule with no rotation and a velocity set by gameplay; ground contact and material, fall damage, water contact |
| Dynamic props | crates and barrels: mass, pushes, `Immovable`, non-collidable pairs, contact callbacks |
| Ice, water and lava | triangles that are passable or solid by a per-vertex freeze value, switching at run time |
| Shield | a hollow sphere or dome (wall 0.5) you can walk into: concave |
| World | gravity (0, −19.64, 0), body sleeping, sweep tests |
| Not needed | joints beyond one hinge, vehicles, soft bodies, cloth |

Two facts make this easier:

- ADR-0002 uses host authority and snapshots, so physics does not have to be deterministic
  across machines.
- Shields do not move once placed, so a hollow sphere can be a static two-sided triangle shell
  (with the arc cut out). That works in any engine, and no custom concave primitive is needed.

An exact match of the original's physics does not matter for gameplay. The feel of character
movement and pushes does, and that comes from the character body and the push rules, which are
our own code in every option.

## Options

| | Jitter2 | BepuPhysics v2 | Our own physics |
|---|---|---|---|
| License | MIT | Apache-2.0 (keep the NOTICE) | ours |
| State (2026-10) | active, 2.13.0 released 2026-10-22 | active, 2.5.0-beta.33 (last stable 2.4.0) | — |
| API | classes: `World`, `RigidBody`, shapes; impulse solver close to JigLibX in design | structs and callbacks; the fastest, and the hardest to learn | exactly what we need |
| Per-triangle filtering (ice, water, materials) | narrow-phase filter | `AllowContactGeneration` with child indices | ours |
| Ray and segment queries | yes | yes | to write: a BVH over level meshes |
| Risks | smaller community | API churn in the beta; struct-heavy code throughout gameplay | months of solver and stacking work for props |

## Decision

1. **Jitter2 behind our own facade.** A new `OpenMagicka.Physics` project, with no FNA, owns the
   engine. Gameplay sees only our types: bodies, shapes, materials, queries and contact events, in
   our vector types. The engine can then be swapped for BepuPhysics v2 without touching gameplay.
2. **Magicka's parts are ours:**
   - the character body;
   - the 10 material meshes, with the material looked up from the hit;
   - ice, water and lava filtering;
   - the shield shell;
   - the collision-skin reader (inverted winding).
3. **A spike before the full work.** Load a real level's 10 meshes and drop capsules, spheres and
   boxes on them. Run 1,000 segment queries per tick. Check the frame time and that nothing
   tunnels at 60 Hz. If Jitter2 fails the spike, the same spike decides for BepuPhysics v2.
4. JigLibX and the game's physics code are a behaviour reference only, as with all decompiled
   code.

## Consequences

- OM-7 is closed for JigLibX: no upstream port. XNAnimation keeps its plan (Ms-PL, upstream).
- ADR-0001's physics row points to this ADR.
- The character controller and the push rules have to be tuned against the original by playing
  it side by side; that tuning is part of OM-32.
- `OpenMagicka.Physics` gets its own tests: queries, materials, ice and water, and the shield
  shell.

## Spike results (2026-10-24)

`OpenMagicka.Physics` (Jitter2 2.13.0) with `LevelCollision` on the game's files; the test is
`PhysicsSpikeTests`. Release build, one thread, Windows x64.

| Check | Result |
|---|---|
| All 132 level models: their material meshes loaded | 226,343 triangles in 312 meshes; no Water or Lava meshes (liquids have their own collision) |
| 2,828 capsules, spheres and boxes dropped from 2 m onto nav-mesh points, 3 s at 60 Hz | none fell through; 59 rolled off an edge |
| Largest level (`ew_s1`, 8,568 triangles), 64 bodies, a physics step | mean 0.15 ms, p99 1.2 ms |
| The same, 1,000 segments a tick between random nav-mesh points | mean 1.6 ms, p99 4.9 ms |
| A 0.225 m sphere at 110 m/s (a boulder) against a single-triangle wall | stopped, with CCD |

Jitter2 passes. What the spike found:

- **Triangles are one-sided**, for collision and for rays: solid from the side the right-hand
  normal (B − A) × (C − A) points to. The reader's index order gives exactly that for the level
  meshes (area facing up 2.84 M, facing down 0.17 M), so no reordering is needed. The shield shell
  needs both windings.
- **The engine's ray test misses rays through a shared edge** (for example, a vertical ray at
  x = z over a quad split along its diagonal). Our segment cast adds its own triangle test with a
  small edge tolerance inside the engine's tree walk.
- Long segments cost about 1.6 µs each. 1,000 a tick is far more than the game needs, but
  queries should stay short where they can.

Not covered yet: animated level parts (moving collision), friction per material, ice and water,
characters and the shield.
