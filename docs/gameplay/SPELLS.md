# Spell and combat system (reimplementation spec)

Source of behavior: `Magicka.exe` 1.10.4.2 (see [`ENGINE_ANALYSIS.md`](../ENGINE_ANALYSIS.md)).
Scope: `Magicka.GameLogic.Spells` (+ `SpellEffects`), `Elements`, `StatusEffects`, `Damage` /
`DamageCollection5` / `AttackProperties` / `DamageResult`, `Defines`, `SpellManager` / `SpellTree` /
`MagickTree`, and the related damage and status logic in `Character`, `Barrier`, `Shield`,
`SpellMine`, `Railgun`, `LightningBolt`, `Blast`.

> **Clean-room.** Below are only rules, formulas, constants and identifiers. No original code is
> carried over. Formulas are written in our own notation.
>
> Notation: `E, W, C, F, Z, A, L, S, I, T, P` are the *magnitudes* (counts) of the elements Earth, Water,
> Cold, Fire, Lightning (Z), Arcane, Life, Shield, Ice, sTeam, Poison in the final spell.
> `√x` is the square root, `min1(x) = min(1, x)`, `trunc` drops the fractional part.
> "Tick" = one application of a `DamageCollection5` to a target.

---

## 1. Elements

### 1.1 `Elements` flags (int, `[Flags]`)

| Index | Element | Flag | Key (default) | Gamepad (2 steps) | Obtained |
|---:|---|---|---|---|---|
| 0 | Earth (=Physical) | `0x001` | D | Down → Left | basic |
| 1 | Water | `0x002` | Q | Left → Up | basic |
| 2 | Cold | `0x004` | R | Right → Up | basic |
| 3 | Fire | `0x008` | F | Right → Down | basic |
| 4 | Lightning | `0x010` | A | Left → Down | basic |
| 5 | Arcane | `0x020` | S | Up → Right | basic |
| 6 | Life | `0x040` | W | Up → Left | basic |
| 7 | Shield | `0x080` | E | Down → Right | basic |
| 8 | Ice | `0x100` | — | — | Water + Cold |
| 9 | Steam | `0x200` | — | — | Water + Fire |
| 10 | Poison | `0x400` | — | — | **not available to the player** (NPCs/items/magicks only) |

Index = log2(flag). Special values: `None = 0`, `All = Magick = 0xFFFF` (marker "this is a magick",
index 11 in `Defines.ElementIndex`, 15 in `Spell.ElementIndex`).

Composite masks (used in the rules below):

| Name | Value | Contents |
|---|---|---|
| `Basic` | `0x0FF` | the 8 basic ones |
| `PhysicalElements` | `0x101` | Earth, Ice |
| `Beams` | `0x060` | Arcane, Life |
| `ShieldElements` | `0x0E0` | Arcane, Life, Shield |
| `Defensive` | `0x0B0` | Lightning, Arcane, Shield |
| `StatusEffects` | `0x64E` | Water, Cold, Fire, Life, Steam, Poison |
| `Instant` | `0x371` | Earth, Lightning, Arcane, Life, Ice, Steam |
| `InstantPhysical` | `0x171` | Earth, Lightning, Arcane, Life, Ice |
| `InstantNonPhysical` | `0x270` | Lightning, Arcane, Life, Steam |
| `Offensive` | `0xFF3F` | everything except Life and Shield |

`Defines.NR_OF_ELEMENTS = 11`. Localization: `#element_earth` … `#element_poison` (in index order).

### 1.2 Element input

- Keyboard: each element key has its own cooldown of **0.125 s** (`MagickaElement`);
  the set of enabled elements is controlled by `ElementManager`/`TutorialManager` (the tutorial can
  disable elements). `ElementManager` has no Poison.
- Gamepad: `SpellTree` is a tree of 5 directions (Center/Up/Down/Left/Right), depth 2; each
  leaf is assigned a basic element (table above). The player's direction buffer (`InputQueue`) walks
  the tree; if there is no child, the first element of the buffer is dropped; a found element
  is returned and the buffer is cleared. The keyboard simply emulates the needed pair of directions.
- Bindings: `KeyboardBindings` (Water, Life, Shield, Cold, Lightning, Arcane, Earth, Fire, Cast, CastSelf,
  Boost, Move, Block, Shift, Inventory, PrevMagick, NextMagick). Casting: RMB = Cast, Shift = modifier
  Area/Weapon, MMB = CastSelf, Space = Boost (cast a magick), LMB = Move/attack.

### 1.3 Side effects when conjuring an element (Avatar)

If the player conjures **Lightning while Wet** (and has no `WetLightning` item passive),
or **Fire while Greased**: the element is not added to the queue; instead damage
`Spell(element).CalculateDamage(Lightning, Self)` is computed, the magnitudes of all entries ×0.5, and it is applied
to the caster. The input buffer is cleared.

---

## 2. Element queue (spell queue)

- Capacity: `Defines.SPELL_QUECAP = 5` for the player (for NPCs, the capacity of their list; for a weapon item, also 5).
- A queue entry is a `Spell` with magnitude 1 of one element (or the result of a merge, see below).
- Ours: `OpenMagicka.Gameplay.Spells` (`ElementQueue`, `ElementRelations`, `Spell`, `MagickBook`), an
  immutable queue where conjuring returns the new queue and whether it grew (OM-34).

### 2.1 Relations between elements

| Relation | Pairs | Result |
|---|---|---|
| **Opposites** (mutual cancellation) | Arcane–Life, Fire–Cold, Earth–Lightning, Water–Lightning, **Shield–Shield** | both entries are removed |
| **Revertable** | Cold–Steam, Ice–Fire | merge → Water |
| **Combinable** | Water–Cold, Water–Fire | merge → Ice / Steam |

Notes:
- The "opposites" check uses the entries' flags: Steam is not Water, so
  Steam + Lightning do **not** cancel; Ice + Lightning do not cancel either. For the four pairs
  each flag may sit on either side, so one entry holding both Arcane and Life is opposite to
  anything, and a magick entry (`All`) is opposite to every element: conjuring an element onto a
  queue holding a combined magick removes the magick (Shield–Shield needs Shield on both sides).
- `InclusiveOpposites` = Opposites + the Fire–Water pair. Used when beams meet and for damage
  to elemental barriers (not in the queue).
- Poison–Life cancel only at the magnitude level when adding spells (section 3), not in the queue.

### 2.2 Algorithm for adding element `X`

**If the queue has < 5 entries:**
1. `X` is appended to the end.
2. "Opposites" pass: from the last entry to the first; for each entry `i`, find the nearest
   *earlier* entry `j < i` that is its opposite (search from `i−1` down to 0). If found, both are
   removed and the walk continues from the position before them.
3. "Revertable" pass: likewise from the end; a found pair is merged (spell sum,
   section 3) **into the place of the earlier** entry, the later one is removed.
4. "Combinable" pass: the same for Water+Cold / Water+Fire pairs.

A merge stores the spell sum (section 3) of the two entries. Each pass runs exactly once, in this
order. Consequence: the merge product of step 3
(Water) is not checked for opposites again during the same addition (this is how, for example,
the Amalgameddon magick is reachable: `S Q R A F` → `[Arcane, Water, Lightning]`).

**If the queue is full (5):** search (from end to start) for the first entry opposite to `X` → it
is removed; otherwise the first revertable one → merged with `X` in its place; otherwise the first combinable one →
merged in its place; otherwise `X` is discarded. (For a weapon with the *lightning precedence* flag,
if nothing was found, the first entry from the end that is not Lightning is replaced with Lightning.)

The lightning precedence does not look at the element added: the game sets the flag only when
lightning strikes a weapon, so the added entry is Lightning there anyway.

The function returns "element added" only if the queue length grew by 1.

A weapon's queue (charging an item) adds the element's spell normalised to a total magnitude of 1
(section 3) instead; that belongs to the items (OM-38).

### 2.3 Examples

- `Q F` → `[Steam]`; `Q R` → `[Ice]`; `R Q` → `[Ice]` (water looks for an earlier cold).
- `Q A` → `[]` (Water–Lightning); `E E` → `[]` (the second shield cancels the first).
- `Q F R` → `[Water]` (Steam+Cold are revertable); `Q R F` → `[Water]` (Ice+Fire).
- `F D Q` → `[Steam, Earth]` (water merges with the *latest* suitable entry, here Fire at position 0).
- `S Q R A F W` → `[]`: after `S Q R A F` (`[Arcane, Water, Lightning]`) Life cancels Arcane, and
  the walk goes on with the entry that was before Life, so Lightning then cancels Water.
  `S Q R A F S` → `[Arcane, Arcane]`, `S Q R A F D` → `[Arcane, Water]`.

---

## 3. Final spell (`Spell`)

`Spell` stores an `Element` mask and 11 magnitudes (float). On cast, `SpellManager.Combine` folds
the queue left to right with the addition operator. If the first entry is a magick (`Element == All`),
it is returned.

### 3.1 Addition operator A + B (step by step)

Let `ΣX = A.X + B.X` for each element.

1. `E = max(0, ΣE − ΣZ)`, `C = max(0, ΣC − ΣF)`, `F = max(0, ΣF − ΣC)`, `Z = max(0, ΣZ − ΣE)`,
   `P = max(0, ΣP − ΣL)`.
2. `I = ΣI`; if `I > 0` and `ΣF > 0`: `k = min(I, ΣF)`; `W += k`, `F −= k`, `I −= k`.
3. `T = ΣT`; if `T > 0` and `ΣC > 0`: `k = min(T, ΣC)`; `W += k`, `C −= k`, `T −= k`.
4. `L = ΣL`, `A = ΣA`, `S = ΣS`, `W += ΣW`.
5. `k = min(ΣW, ΣF)`: `T += k`, `W −= k`, `F −= k`.
6. `k = min(ΣW, ΣC)`: `I += k`, `W −= k`, `C −= k`.
7. `Element` = union of the flags of all elements with magnitude > 0.

Steps 2, 3, 5 and 6 use the original sums (ΣF, ΣC, ΣW), not the values already reduced in step 1,
so a magnitude can end up **negative**: a spell holding Water, Fire and Cold (1 each) added to an
empty one gives F = C = W = −1, Steam 1, Ice 1 (flags Steam | Ice). Negative magnitudes set no
flag. A player's queue never holds Water, Fire and Cold together, so only spells built otherwise
(characters, items) can reach this.

Since the queue is already normalized, in practice an element's magnitude = the number of its entries in the queue
(for example, `Q F Q F D` → Steam 2, Earth 1). `Spell.DefaultSpell(mask)` gives magnitude 1 for each
flag. `Normalize()` divides all magnitudes by their sum (used when charging a weapon).

Helper quantities:
- `TotalMagnitude` = sum of all 11 magnitudes.
- `TotalSprayMagnitude` = F + C + T + W + P.
- `BlastSize` = `√(m / 5)`, where `m` depends on the type: Spray → max(T, F, C, W, P); Projectile →
  max(E, I); Beam → max(A, L); otherwise 0. The "blast" radius is usually `BlastSize × 10`
  (5 elements → radius 10).
- `ArcaneMultiplier(m) = 3·√(m/5)` (helper, used outside the core).

### 3.2 Spell type detection (`GetSpellType`), by priority

1. `Element == All` → **Magick**
2. `Element == None` → **Push** (empty queue: "push")
3. contains Shield → **Shield**
4. contains Earth or Ice → **Projectile**
5. contains Arcane or Life → **Beam** (railgun)
6. contains Lightning and does **not** contain Steam → **Lightning**
7. otherwise → **Spray** (Water, Cold, Fire, Steam, Poison, Steam+Lightning)

---

## 4. Cast types and lifecycle

`CastType`: `None/Blast = 0`, `Force = 1` (RMB), `Area = 2` (Shift+RMB), `Self = 3` (MMB),
`Weapon = 4` (Shift+LMB, weapon "imbue"), `Magick = 5` (Space).

Ours (OM-35): the rules and numbers of sections 3.2–5 as pure functions in
`OpenMagicka.Gameplay.Spells`: `SpellTypes`, `ElementQueue.Combine`, `CastRules`, `PushRules`,
`SprayRules`, `ProjectileRules`, `BeamRules`, `LightningRules`, `ShieldRules`, `BlastRules`.
Carrying them out in the world (effects, entities, physics, targeting) waits for the entities and
physics (OM-71).

Order: when cast is pressed, the queue is folded into a `Spell` (the player's queue is cleared), the character
enters `CastState` (or `ChargeState`), and an animation event calls `Spell.Cast(castType)`,
which takes an effect from the pool by `SpellType` and calls `CastForce/CastArea/CastSelf/CastWeapon`.

| SpellType | Effect class |
|---|---|
| Push | `PushSpell` |
| Spray | `SpraySpell` |
| Projectile | `ProjectileSpell` |
| Shield | `ShieldSpell` |
| Beam | `RailGunSpell` (+ entity `Railgun`) |
| Lightning | `LightningSpell` (+ `LightningBolt`) |
| Magick | `SpecialAbility` by `MagickType` (section 10) |

Basic effect rules (`SpellEffect`):
- minTTL/maxTTL: Force/Area/Weapon = 0.5 / 1.5 s, Self = 0.2 / 0.2 s.
- A continuous effect (everything except Projectile) is interrupted if the player released the button after
  minTTL expired, if the caster died, or if the caster's `CastType` became `None`.
- Turn speed while casting (multiplier): base 0.75; Spray 0.6; Lightning 0.1; Shield 0.1;
  Beam 0.0325; Push 0; Projectile 0.
- Effect pools hold 16 instances of each type.

**Charge (SpellPower).** If the type is Projectile and the cast is Force, or the type is Push and the cast is Force/Area, the character
enters `ChargeState`: `charge_force`/`charge_area` plays, `SpellPower` grows linearly
from 0 to 1 over the clip duration, and after the clip ends it is 1 (loop animation). Releasing the button →
cast. Turning while charging ×0.125. For non-avatars, projectile charge is fixed at 0.5 (a boss's
spell zone charges like a player).

Arc angles in this section (cones, weapon arcs) are measured from the facing to either side: a
target counts when its direction deviates from the facing by less than the angle.

**Weapon (imbue).** For the avatar, a Weapon cast does not create an effect right away: all queue entries
are moved into the held weapon's queue (`Item.TryAddToQueue`, with `Normalize` and the same
merge rules, capacity 5), and the player's queue is cleared. When the weapon is "charged" and the player's queue
is empty, an attack calls `Cast(Weapon)` of the stored spell.

### 4.1 Casting in the world (OpenMagicka, OM-71)

**What a character holds.**
- Its element queue (five entries for an avatar) and the spell combined from it.
- Its cast type, its spell power, and the spell effect it is running (at most one).
- For an avatar, two more things:
  - the cast button it holds;
  - whether charging is unlocked. Charging locks when a charge starts and unlocks again when the
    Force button is released.

Setting the cast type to Force, Area or Self, when it was something else, combines the queue
into the spell and empties the queue. Setting it to Weapon keeps the queue.

**An avatar's buttons** (the input, OM-31, calls them; a dead character presses nothing):
- **Force or Area pressed.** The cast starts only when all of these hold:
  - the slot-1 item is a staff;
  - no button is held;
  - the cast type is none.

  The cast type becomes Force or Area, and the button is held. An empty queue casts a push.
- **Force or Area released.** The button is let go if it is the one held. Only a Force release
  unlocks charging, as the original: after an Area charge, charging stays locked until the Force
  button is released.
- **Self pressed** (the special button):
  - **With a queue:** if the cast type is none, the button is held and the cast type becomes Self.
    A frozen, flying, pushed or knocked-down avatar casts at once, since its state will not get to it.
  - **Without a queue:** the spell-source item's special ability is used (ENTITIES_AI §8.1.7),
    unless the avatar is casting or charging.
- **Self released.** The button is let go.
- **Attack pressed** with a queue whose first entry is not a magick, or with a charged weapon:
  if the cast type is none and the avatar is not gripped, the Weapon button is held and the cast
  type becomes Weapon. Otherwise it is an ordinary attack.
- **Attack released.** The Weapon button is let go if the cast type is none.
- **Each frame.** An avatar running no effect and with no cast type lets go of a held Self or
  Weapon button.
- **Conjuring** adds the element to the queue, except while charging or dead (§1.3 has the
  avatar's lightning-while-wet and fire-while-greased).

**Into the cast states.** Idle and Move go to Charge or Cast, as do Entangled (Cast only), when an
avatar has a cast type:
- **Charge**, when charging is unlocked and the spell is a Force projectile or a Force or Area
  push.
- **Cast**, otherwise.
- In Idle the check comes after "moving", and in Move after "blocking" (ENTITIES_AI §5.2).

**The cast state.**
- **Entry:** invisibility ends and the character turns solid. The animation follows the table in
  ENTITIES_AI §5.3.
  - Weapon casts: when the weapon is charged and the queue is empty, the clip is `cast_sword_*`
    by the weapon's stored spell type; otherwise it is `cast_sword` (blend 0.15).
  - A frozen character casting Self casts at once.
- **Each frame, in order:**
  1. The clip `idle` → Idle.
  2. A push → Pushed; a knockdown → KnockDown.
  3. A looping clip, once its blend ended, unless the cast is Force → Move (walking) or Idle.
  4. The clip finished → Move or Idle.
  5. The effect stopped → Attack if attacking, else Move or Idle.
  6. Frozen → Idle.
- **Exit:** the effect stops, the cast type goes back to none, and the character may turn again.

**The charge state.**
- **Entry:** invisibility ends and the character turns solid. It plays `charge_force` or
  `charge_area` and the `spell_projectile_precharge` sound (Spells bank). The spell power goes to 0,
  and an avatar's charging locks.
- **Each frame:**
  - The hit checks come first.
  - While the clip is not looping:
    - once it has finished, the loop clip (`charge_force_loop` / `charge_area_loop`) plays, the
      loop sound `spell_projectile_charge_loop` starts, and the power is 1;
    - before that, outside a blend, the power is the clip's time over its duration.
  - An avatar whose Force or Area button is no longer held goes to Cast.
- **Exit:** the sound stops. A spell with elements is cast at once, from the staff, so a charged
  projectile flies on release. A charged push has no elements and is cast by the cast clip, which
  plays the push animation.
- **Kept from the original:** the exit casts however the charge ends, a hit or death included, and
  the cast type stays set, so an avatar back on its feet goes on into Cast.

**The `CastSpell` action** (`fromStaff`, or a bone otherwise): on its first frame, if the
character runs no effect, it casts. When the action ends, it tells the running effect that its
animation ended. One cast casts once: a later action of the same cast (a looping cast clip, the
push clip after a charged projectile flew) does nothing until the cast type is set again
(OpenMagicka's; in the original the running effect blocks it, but types without an effect yet
would cast a push).

**Non-players cast through their attacks** (the AI's `CastSpell` ability sets the spell power and
the cast type, then attacks with a clip that holds the `CastSpell` action):
- A non-player's Weapon cast combines its queue and casts it there and then.
- The attack state stops the running effect when it chains into its next clip and when it ends,
  and the end sets the cast type back to none.

**Casting:**
- **A magick** (the combined spell is all elements) comes with the magicks (OM-37); until then it
  does nothing.
- **An avatar's Weapon cast:**
  - with a charged weapon and an empty queue, it casts the weapon's stored spell (which empties the
    weapon);
  - otherwise it imbues the queue into the weapon (§4) and casts nothing.
- **Otherwise,** the spell takes an effect by its type and starts it by the cast type. The spell is
  then spent. An entangled character casting Self loosens its vines by the spell's elements (by 0).
  The effect types arrive phase by phase; a type with no effect yet casts nothing.

**Running an effect.** After the state each frame, the running effect updates:
- It gives the turn-speed share for that frame (§4).
- While charging, the turn speed stays at ×0.125. With no effect, it is the full speed.
- A Weapon effect that stopped is dropped outside the cast state.

**Every effect's base rules:**
- An effect lives at least its minimum time.
- A continuous effect (all but projectiles) stops in three cases:
  - an avatar's held button is none once the minimum time has passed;
  - the caster is dead;
  - an avatar's cast type is none.
- A push, a projectile and a shield keep their own rules for this (below).

---

## 5. Behavior by type

### 5.1 Push (empty queue)

| | Force | Area |
|---|---|---|
| Strength | `50 + 500·SpellPower` | same |
| Cone / radius | angle π/6, range 10 | 360°, radius `3.5 + AdditionalPushRadius` |
| Duration | 0.25 s; the coverage radius grows from 25 % to 100 % | same |

Hit: `Damage(Pushed, Earth, amount = strength, magnitude = 1.5)`, once per target (hit list),
only with line of sight (the ray to the target does not cross level geometry). Entities without
`IDamageable` get the impulse directly (elevation `10° + SpellPower·0.1·π/8`). Self/Weapon do nothing.

In the world (OM-71):
- **Timing.** The push lives at least 0.4 s; its wave runs 0.25 s. It ends once both are over, and
  it does not stop for a released button.
- **Each frame of the wave:**
  - The radius is `R·(0.25 + 0.75·(1 − t/0.25))`, where `t` is the wave time left.
  - It reaches every entity within that radius except the caster: damageable ones, and none hidden
    behind a shield.
  - A Force push also needs the entity inside its cone (the arc test, within 4 m of height). An Area
    push takes the whole circle.
  - Each entity is hit once. It needs a line of sight from the caster, and the dead are skipped.
- **What a hit does:**
  - A damageable entity takes the push damage from the caster's position.
  - A missile takes no damage: it gets the impulse instead, its velocity set by the ballistic rule
    (ENTITIES_AI §1) for the strength and a distance of 1.5.
- **Spell power.** It goes back to 0 once the push is cast.
- **Cues.** The `push_force` effect (at the cast source) or `push_area` (at the caster) plays, with
  the `spell_push_force` or `spell_push_area` sound from the Spells bank. The sound's magnitude is
  the spell power × 3.

### 5.2 Spray

- **Force**: cone ray, angle π/10 (18°), duration up to **4 s** (while the button is held).
  Base range `R = 5 + TotalSprayMagnitude` (10 with 5 elements). The range builds up
  over 0.25 s (multiplier +4/s up to 1). The actual hit range in a frame = `R · (0.1·R·f)`,
  where `f ≤ 1` is the fraction of the ray up to the first obstacle (level geometry or a shield). I.e. with no
  obstacles ≈ `R²/10`.
  Each target in the arc gets the damage collection **with magnitudes ×0.25**, no more than once per 0.25 s
  (hit list with a 0.25 s timer) ⇒ ≈ 1 "full" collection per second.
  Cold/water in the spray freeze liquid surfaces (`Liquid.Freeze`). Decals remain on the floor
  (Fire/Cold/Water); with F ≥ 1, the `fire_scourge` effect.
- **Area**: instant `FullBlast` of radius `BlastSize·10` around the caster (the caster is
  excluded), with falloff (5.8).
- **Self**: the collection `(Spray, Self)` is applied to self once (typically: water puts out fire, fire
  dries).
- **Weapon**: arc strike of radius 6, angle π/2 from the weapon point.
- If the caster is **Greased** and the collection contains fire, the first fire entry is applied to
  the caster (Force/Area/Weapon).
- Water without steam formally triggers "electrification" (`Electrocute`), but in `Character` this function
  is empty (it really works only for electric barriers).

In the world (OM-71):
- **Force.**
  - **Timing.** The spray runs up to 4 s. Each frame its build-up rises by 4 × the frame time, up
    to 1, and the frame's base range is `R × build-up`.
  - **Origin and direction.** It leaves from the cast source along the caster's facing. (A cast
    from the staff would follow the staff's tip, but a Force spray outside panic is never treated
    as from the staff.)
  - **The free fraction `f`.** A ray of the frame's range from the origin is cut at the level, and
    then at each shield it crosses (each shield multiplies the fraction again).
  - **What it reaches.** It looks at every damageable entity within the frame's range of the caster
    (none hidden behind a shield), the caster left out:
    - a shield is hit when the arc test passes over the frame's range;
    - anything else when it passes over the reach `range × 0.1 × range × f`;
    - the arc test is the spray's angle (π/10 either side), within 5 m of height.
  - **The hit.** It takes the Force collection with magnitudes ×0.25, struck from the origin. It
    then waits 0.25 s before the spray can hit it again (a frame-by-frame countdown, as the
    original's: 16 frames at 60 Hz).
  - **The end.** It stops after its 4 s, or by the base rules (§4.1) once its 0.5 s minimum has
    passed. Turning is ×0.6.
- **Area.** At once, a full blast of radius `BlastSize·10` around the caster, the caster left out.
  The effect ends on its next frame.
- **Self.** At once, the Self collection on the caster, from its own position. The effect ends
  after 0.2 s.
- **Weapon.** At once, an arc strike from the weapon's tip along the facing: radius 6, angle π/2,
  each entry ×`1 − (distance − radius)/6`, no line of sight. The effect ends after 0.2 s.
- **Greased.** For Force, Area and Weapon, a greased caster takes the first fire entry among the
  collection's first four, from the cast source.
- **Cues.** Each element of the spell starts its effect. Force uses `spray_<element>1` at the cast
  source, following the spray. Weapon uses `weapon_spray_<element>` (status elements and steam) at
  the weapon. Self uses `self_<element>` at the caster. All of them end with the effect.
- **With other systems.** Freezing liquids, the floor decals and `fire_scourge` come with liquids
  and decals (OM-66). The point light, the spell's sound and the rumble come with the renderer,
  audio and input.

### 5.3 Projectile (Earth/Ice)

**Force** (chargeable):
- Speed `v0 = 25 + 85·SpellPower`; with Earth: `v0 ×= 0.5 + 0.5/E`. Projectile mass `1 + 50·E`,
  radius `0.075 + 0.075·E`, lifetime 10 s.
- With Earth, **one** boulder. Without Earth (Ice only), `N = trunc(3·I)` shards.
- Interval between shots: `(0.0875 + Total/5·0.2625) / N`.
- All projectiles except the last get a random Y rotation within `±(π/4)·(1−charge)`
  and speed `v0·(1.25 + 0.75·rand)`; the last one flies exactly along the direction at speed `v0`.
- If the segment "character center → launch point" crosses a shield, the projectile is not created.
- Models (`Models/Missiles/`): with Earth and Ice, `IceEarth0, IceEarth1, IceEarth2, IceEarth2,
  IceEarth3` by `clamp(trunc(I) + trunc(E) − 1, 0, 4)`; with Earth only, `Earth0, Earth1, Earth2,
  Earth2, Earth3` by `clamp(trunc(E) − 1, 0, 4)`; shards `Ice3`.
- Hit: damage `(Projectile, Force)` (section 6.3); for stone projectiles the first entry
  is scaled by speed: `amount × |v| / 50`. Ice shards are Piercing, magnitudes ×`1/N`.
- A boulder's blast: `remainder_type` = the type of its elements without Earth, and the radius
  `BlastSizeAs(remainder_type)·10` measured on **all** the magnitudes (Earth's included).
  - **An ice boulder (Earth and Ice) never blasts**, whatever the radius. It goes on its first hit,
    and on its second collision with the level (one bounce, then gone).
  - **A boulder without Ice** with a radius above 0 blasts on a hit or a collision with
    `CalculateDamage(remainder_type, Area)` of the full spell, and is removed. A boulder with Arcane
    or Life blasts as a beam, and one with a spray element as a spray.
  - **Otherwise** (Earth alone, or with Lightning: a blast size of 0) its removal counts 25 hits
    down and takes it on the 26th, after that hit's damage. It is not piercing: it bounces off what
    it hits. It goes on its second collision with the level.
- Collision with the environment: `Crater` decal.
- The mass `1 + 50·E` is worked out but never given to the missile: every missile has mass 4.

**Area**: `GroundBlast` of radius `BlastSize·10` (Earth/Ice), no falloff, ignores
levitating targets; with Ice, ice spikes appear (`IceSpikes`). Damage `(Projectile, Area)`.

**Self**: projectiles fall on the caster: shards from random points on a circle
of radius 6 at height +10, the last one (or the single boulder) from height +8 straight down.

**Weapon**: with Ice, an ice blade (`IceBlade`) of length `3.75 + 1.25·I`; with Earth, when the
strike animation ends, an underground wave (`UnderGroundAttack`) is launched with speed 40, range 15;
each target takes damage once.

In the world (OM-71):
- **The cast.**
  - The charge is the caster's spell power for an avatar, 0.5 for anything else.
  - The launch speed comes from the spell power, which then goes back to 0.
  - The interval and count are those above. The splash radius is `BlastSize·10` of the spell with
    Earth's element left out (its magnitude kept).
- **Each frame** (Force and Self), the shot timer runs down by the frame time. While it is at or
  below 0:
  - It gains the interval and one projectile is used up.
  - **The shield check (Force).** If the segment from the caster to the cast source crosses a
    shield (thickened by `0.1 + 0.075·E`), that projectile is lost against the shield.
  - **All but the last.**
    - Force: turned at random within ±`(π/4)(1 − charge)` about the facing.
    - Self: launched from a random point on a circle of radius 6, 10 above the cast source,
      towards the caster.
    - Either way at `v0·(1.25 + 0.75·rand)`.
  - As the original, the frame's shots fire before the caster's death is looked at.
  - **The last.** Force: straight along the facing at `v0`. Self: from 8 above the cast source,
    straight down (direction (0.001, −1, 0)) at `v0`.
  - The effect ends once no projectile is left, so a single boulder ends it on its first frame.
- **The missile.** Radius `0.075 + 0.075·E`, from the cast source. It may hit its caster, but not
  within its first 0.25 s, and it pierces when its damage does. Its events:
  - **On start:** for each element the spell has, the missile's sound and trail effect.
  - **On a hit:**
    - the damage (§6.3, the first entry scaled by speed);
    - for an ice boulder, removal;
    - for a boulder, the blast and removal, or for one with no blast a 25-hit count;
    - shards' damage is piercing and ×`1/N`, its first four entries only.
  - **On a collision:**
    - a `Crater` decal (scale `E + I − 1` for an ice boulder, `1 + (E − 1)/2` for a boulder);
    - the blast and removal; an ice boulder or a boulder with no blast goes on its second
      collision, and a shard on its first;
    - the hit effects and sounds per element.
  - **After 10 s:** removal.
  - The effect and sound names come from the original's per-element tables: `projectile_<e>4`
    trails, `projectilehit_big_<e>` hits, `projectiletrail_<e>` and `projectilehit_<e>` for shards,
    and the `spell_<e>_projectile`, `spell_<e>_hit` and `spell_<e>_blast` sounds.
- **A blast from a missile** strikes at the missile, as its owner, sparing the missile itself (its
  caster is not spared).
- **Area.** At once, the ground blast of radius `BlastSize·10` around the caster, the caster
  spared. It also plays the `spell_<e>_area` sounds. The effect ends on its first frame.
- **Weapon.**
  - **With Ice:** an ice blade of the length above rides the slot-0 weapon. When the cast clip's
    action ends, or the effect stops, the blade strikes once. It hits along the facing from the
    caster for its length: every damageable entity within half the length of the segment's middle
    that the segment, thickened by 1, touches takes `(Projectile, Weapon)` of the spell without
    Earth, from the point it was struck.
  - **With Earth,** when the cast clip's action ends (once, while the effect runs and the caster
    lives; the vanilla `cast_sword_projectile` window ends at 0.45), an underground wave leaves the weapon's tip
    along the facing at 40/s for 15. It carries `(Projectile, Weapon)` of the spell without Ice.
    - It starts only over a floor (normal Y ≥ 0.7, within 4 below a point 1 above the tip).
    - Each frame it follows the floor ahead (within 2 below a point 1 above). It ends at a drop, a
      steep slope, a shield (the step thickened by 1) or a wall (the step, raised 1).
    - It hits each damageable entity within 1 of it once, the caster never.
  - No projectile cast stops for a released button, as the original. A weapon cast's effect lasts
    until its cast state ends it.
- **With other systems.** The missile light, the Area cast's ice spikes, and the blade's model and
  trail are presentation.

### 5.4 Beam (Arcane/Life, "railgun")

- **Force**: a beam lasting `TTL = 1 + 2·(A + L)` s (including `ModifySpellTTL` buffs).
  Length grows at 90 units/s up to a maximum of **75**. Damage to the target (the first one crossed):
  every 0.25 s, the collection with magnitudes ×0.25. The beam reflects off the `Reflect` material, shields and
  characters inside a bubble shield, passes through portals, ignores non-solid barriers and grease
  fields. Maximum depth of the reflection/merge chain is 6.
- **Beam crossing** (distance between segments ≤ 1):
  - "Explosion" after 0.25 s if: the beams share an owner, OR their extra elements
    (`W, C, F, Z, T, P`) are `InclusiveOpposites`, OR one carries Arcane and the other does not
    (an Arcane and a Life beam, or an Arcane+Life and a Life beam), OR the directions are
    nearly opposite (|mean of the directions| < 0.2). Explosion: `FullBlast` of each owner's damage
    collection with radius `3 + number_of_owners`, plus Knockback 500 / magnitude 2 with radius 5.
    Achievement `nevercrossthebeams`.
  - Otherwise the beams **merge**: a child beam leaves the meeting point along the mean direction,
    carrying the damage of all parents.
- Arcane/Life beam damage accumulates on the character (`AccumulateArcaneDamage`); on death from
  a beam the character "bloats" and explodes (`BloatKill`, `BLOATTIME = 0.333`).
- **Area**: `FullBlast` of radius `BlastSize·10` (visually `ArcaneBlast`, no distance falloff
  for Arcane/Life, see 5.8).
- **Self**: the collection `(Beam, Self)` is applied to self (Life heals).
- **Weapon**: `ArcaneBlade`; instant arc strike of radius `1.5 + Total/2`, angle 81° (9π/20).

In the world (OM-71):
- **Force.**
  - **The effect.** It lives `1 + 2·(A + L)` s, after the caster's spell-time buffs. It ends earlier
    when its beam dies, when the caster dies, or when the caster's cast type is none (any caster,
    not only avatars).
  - **Each frame** the effect moves the first beam to the cast source and the caster's facing.
- **The beam** is a segment that runs from its position along its direction. Its length grows by
  90/s, up to 75.
- **What it meets, each frame:**
  - **Entities.** The nearest damageable entity the segment touches, thickened by 0.25. Searched
    within 1.25 × the length; none is hidden behind a shield. It never meets its only owner, or a
    grease field, or a barrier that is not solid. Meeting an entity cuts the beam there. Meeting
    nothing restarts the damage timer at 0.25.
  - **The level** cuts the beam where the segment meets it. Seen from behind, the segment is first
    moved 0.1 along. A level face of the Reflect material, met from the front, reflects it.
  - **Other beams** (not its parents, child or dead). A beam that comes within 1 of another,
    within the parts each one may meet, is cut there:
    - **They explode** when they share an owner (unless it met the other's start), when their
      extra elements are inclusive opposites, or when one is arcane and the other not. The
      explosion comes after 0.25 s, at the meeting point, and the chain is locked until then.
    - **Otherwise they merge.** Meeting the other's start (within 1) with no child of its own, it
      joins that beam as a parent and hands it its damage; an owner it already holds makes them
      explode. Then:
      - their mean direction shorter than 0.2 → explode;
      - else, with no child yet, a child leaves the meeting point along the mean direction with
        both as parents, one level deeper (at most 6 deep).
  - Without a meeting, a beam's child dies, unless the beam is reflecting (off a shield, a bubble,
    or a Reflect face). As the original, this also kills a beam joined at its start the frame
    after (it is the child now, so no longer met); and the meeting test measures the other beam's
    part with this beam's length.
- **A first beam** dies when its caster leaves the level; a release during an explosion's fuse ends
  the chain, and with it the explosion, as the original.
- **On what it meets:**
  - **A shield or a character in a bubble** reflects it, the normal flattened.
    - A reflection that comes back almost on itself (dot < −0.99) explodes at the first junction
      instead.
    - Otherwise a child leaves the hit point along the reflection (6 deep at most), and then
      follows the hit point each frame.
  - **Anything else damageable** takes each owner's damage at ×0.25 once the damage timer is at or
    below 0, struck from the hit point; the timer then gains 0.25. For a character, each Arcane or
    Life entry that leaves it at no hit points bloat-kills it (below).
  - The timer runs down by the frame time while the beam meets something.
- **A child beam** dies when all its parents are dead. One with two parents sits at the mean of
  their ends, along the mean of their directions.
- **The explosion.**
  - The chain is killed back to its first beam, and so is the companion's (OpenMagicka primes a pair
    once: a beam that is priming or locked is not met again).
  - Each owner's damage, of this beam and of its companion, blasts in full at the meeting point
    with radius 3 + the number of owners, as that owner.
  - Then a knockback (500, magnitude 2) blasts with radius 5, with no owner.
- **Bloating** (any character): a bloat kill marks it as bloating with no hit points, when its
  template has gibs. 0.333 s later it bursts:
  - a full blast of radius `radius + 2.5`, its element as Damage (Life −180 or Arcane 225), with
    magnitude `1 + MaxHitPoints/5000`, as the killer;
  - then it is overkilled.

  A character with no gibs that a beam brought to no hit points simply dies, as the original (a
  bloat kill of a living one bursts at once and kills it). A bloating character goes to the bloat
  state and from there to death.
- **Area.** At once, a full blast of radius `BlastSize·10` around the caster, the caster spared,
  with the `spell_arcane_area` or `spell_life_area` sound. The effect ends on its first frame.
- **Self.** On the effect's first frame, the Self collection on the caster. It lives 1 s, ending at
  once when the caster dies or (an avatar) its cast type is none.
- **Weapon.** At once, everything damageable that the arc test from the caster along its facing
  passes takes the Weapon collection, from where the test hit: range `1.5 + Total/2`, angle 9π/20,
  2 of height. The caster itself is not hit (the arc misses at its own centre). The blade is
  presentation. The effect follows the base rules (§4.1).
- **With other systems.** Portals (the beam passes through them) come with the Portal magick.
  The beam's looks are presentation: its cut, light and sounds.

### 5.5 Lightning

| | Force / Weapon | Area | Self |
|---|---|---|---|
| Number of bolts | 8 per 1 s | 32 per 1 s | 1 (on self) |
| Range | `4 + 0.8·Z` | `2 + 0.4·Z` | — |
| Direction | along facing | in a circle, step π/4 | — |
| Magnitude per bolt | ×1/8 | ×1/16 | ×1 |

(Range is modified by `ModifySpellRange` buffs.) If the caster is **Wet** (without the
`WetLightning` passive), range is 2 and the mode is "in a circle".

Bolt (`LightningBolt`): the first target is chosen among `IDamageable` within the radius, in a sector of ±90° from the
direction, not behind a shield; the candidate with the smaller angle and smaller distance is picked. Then the chain:
from the hit target, the nearest new target is searched within radius `remaining_range − traveled`,
in a sector of ±45° from the bolt direction. The chain does not continue from barriers, shields and boss zones.
A blocking character absorbs the lightning into their weapon (adds Lightning to the item's queue).

In the world (OM-71), as the original's spell actually casts it:
- **The bolts.** The effect fires its bolts `1/bolts` s apart, the first at once (Force and Weapon
  8, Area 32, each with its share of the magnitude). It ends once all are fired; it also follows
  the base rules (§4.1). Turning is ×0.1.
- **Each bolt:**
  - Its hit list is emptied, then holds the caster.
  - It goes along the facing from the cast source (the weapon's tip for Weapon).
  - A wet caster without the `WetLightning` passive switches to range 2 all around from that bolt
    on.
  - All around: the n-th bolt turns `(n mod 8)·π/4` from the facing and leaves from the cast
    source.
  - The range is the spell's, after the caster's spell-range buffs.
- **A bolt picks its target** among damageable entities within its range of its source: not dead,
  not on the hit list, not frozen, a character only if not ethereal. One behind a shield (the line
  from the source to it crosses a shield) is skipped, unless it is the shield itself, which is
  aimed at its nearest point.
  - Going through them in order, a candidate within ±90° (flat) of the bolt's direction replaces
    the best so far when both its angle and its distance are smaller; one on the source always
    does.
- **The hit.**
  - A blocking character takes nothing. Its slot-0 weapon takes the caster's spell into its queue
    (lightning first), and the chain stops there.
  - Anything else takes the bolt's damage from the source.
  - Unless the target is a shield or a barrier, the chain goes on from the target's position,
    towards it, with the same range and hit list.
- **No target.** The bolt wanders ±π/16 and fades, drawn up to the level. The hit list is emptied.
- **Self.** At once, the Self collection on the caster; no bolts. The effect lasts until the base
  rules end it.
- **Cues.** A lightning cue for each bolt, from where it leaves to where it ends.
If there is no target, the bolt is drawn with a random deviation of ±π/16 and fades.

### 5.6 Shield (contains Shield)

Let `X = Element without Shield`.

**X empty (pure shield):**

| Cast | Result |
|---|---|
| Force | Shield "disc" radius 5.5, HP 800; center offset 3.5 behind the caster (arc ≈2 m in front) |
| Area | Sphere radius 5.5, HP 1000 around the caster |
| Weapon | Wall radius 5.5, HP 800 in front at `character_radius + 7.5` |
| Self | Bubble (`SelfShield`), see below |

`Shield` entity: loses 10 HP every 0.1 s (**100 HP/s**), max HP 5000; takes damage only
from attacks with the Damage/Status flags (`amount × magnitude`), others are reflected. Blocks sprays, beams
(reflection), lightning, projectiles.

**X without Earth/Ice, but with Arcane or Life → mines (`SpellMine`):**
- Force: an arc of a circle of radius 4 (center 1 m behind), the first two mines on the sides, then
  a chain every 0.1 s up to an arc length of `2π·4·0.125 ≈ 3.14` in each direction.
- Area: a full circle of radius 4 around the caster.
- Weapon: a line forward: `2 + trunc(max(Total/2, 1))` mines with step 2.5.
- Mine: radius 1; activates when the spawn animation ends; triggers **0.5 s**
  after being touched by a character/boss zone/projectile (not ethereal and not levitating; the owner
  too, see "In the world" below); `FullBlast` radius **3** with damage `(Shield, Force)`.
  Explodes if the owner is farther than 30 units. New barriers spawning on top of a mine detonate it.

**Otherwise → barriers/walls (`Barrier`):**
- Force: an arc of radius 4, segment step `2·GetRadius` (1.25 for solid, 0.75 for
  elemental, with scale fitting), a chain every 0.075 s over `≈π` in each direction. If the
  spell has no status elements (`StatusEffects` mask: Water, Cold, Fire, Life, Steam, Poison) and no
  Lightning (i.e. a pure stone/ice wall, possibly with Arcane), the arc center is shifted 2 m back.
- Area: a ring of radius `S + 3` around the caster.
- Weapon: a line forward, as for mines.
- **Solid barrier** (has Earth or Ice):
  - HP `(1 + E)·500`, armor `trunc(50·I)` is subtracted from incoming damage (except fire against ice).
  - Loses 10 HP every 0.2 s (**50 HP/s**).
  - Immune to "contradicting" elements: a cold barrier takes no damage from Fire/Steam,
    a steam one from Cold, a water one from Fire, a fire one from Water.
  - On spawn, hits everyone within radius `1.25·scale·radius` with damage `(Projectile, Area)` once.
  - Contact damage `(Shield, Force|Area)` (once per 0.25 s per target) is active while the "effect" is alive:
    pure ice spikes live `I` seconds (then the barrier disappears); for an ice "geyser" the effect
    time = the magnitude of the dominant extra element; for a "tesla" (Ice+Lightning) = `Z` s;
    for Ice+Earth the ice shell lives `I` s.
- **Elemental barrier** (without Earth/Ice, a "storm"/wall of an element):
  - HP `Total·100` (Shield counts in Total), the same decay of 50 HP/s.
  - Takes damage only from opposite elements: `HP −= trunc(share·amount·5)`, where
    `share = (number of barrier elements that are InclusiveOpposite to the incoming one) / (number of barrier elements without Shield)`.
  - Contact: damage `(Shield, ...)` once per 0.25 s per target; with Lightning, it strikes with a bolt.
- A spawning barrier destroys barriers from other casts and detonates mines within radius `0.8·scale·radius`.
- The camera shakes with strength `0.25..0.75` and duration `0.5·S`.

**Self:**
- X empty → `SelfShield` bubble: 500 HP, −100 HP/s; absorbs all Damage/Status attacks (fully
  reflects); physical hits are turned into knockback.
- X contains Earth/Ice → **armor**:
  - HP 1500 with Earth, otherwise 900 (Ice); does not decay over time; character mass 1001 (cannot
    be pushed); speed `0.9 − (0.075·E + 0.025·I)`.
  - Physical damage: `amount −= 10` (Earth) or `−= 70` (otherwise), then `× (1 − (0.05·I + 0.15·E))`;
    the remainder beyond the armor HP passes through. Fire (DoT) against ice armor ×`(1 + I)`; healing does not pass
    through the armor.
  - When applied, ice armor deals damage `Earth|Piercing` 275 within radius 3, magnitude
    `max(1, I + E)`.
- X contains Lightning/Arcane/Life/Steam/Water/Cold/Fire → **elemental ward aura**: for
  each such element, a resistance buff with multiplier 0 (immunity) for **everyone** within radius
  `1.5·m`, duration `15 + 5·m` s, where `m = max(1, max magnitude of these elements)`.
  Removes the previous bubble (if armor was not applied) and the previous aura.

In the world (OM-71), shields and self-shields:
- **A shield entity** (disc, sphere or wall):
  - **Hit points.** It has at most 5000 and loses 10 every 0.1 s; it goes when they are gone.
  - **Shape.**
    - A disc is a sphere shell of radius 5.5 within π/4 of its facing.
    - A sphere is the whole shell.
    - A wall is a box 0.4 thick and 6 high, running 12 along its facing, centred on its position.
  - **In the world** it is solid: characters and missiles meet it as they meet the level, a shell
    from either side. Segment tests (sprays, beams, lightning, line of sight through shields) meet
    the shell's first crossing within its cap, or the wall's box.
  - **Damage.** It takes attacks that carry Damage or Status: `amount × magnitude` comes off its
    hit points when that is not negative. It deflects anything else. A missile that meets it hits
    it, firing the missile's Hit events with the shield as the target.
  - **Overlap.** A new shield that overlaps an older one kills the older at once, and itself 0.25 s
    later. While that timer runs, or once it is out of hit points, characters pass through it;
    missiles and spells still meet it. An older shield already out of hit points does not count.
    - Two shells overlap when their flat circles cross within both caps.
    - Two walls overlap when their 12-long flat lines come within 0.5.
    - A wall and a shell overlap when the wall's line crosses the shell within its cap.
- **The casts** (a pure shield; the effect lives its 0.5 s minimum, or until its clip ends;
  turning is ×0.1):
  - **Force:** a disc with 800 hit points, 3.5 behind the caster, facing along its facing.
  - **Area:** a sphere with 1000 hit points at the caster.
  - **Weapon:** when the cast clip's action ends, or the effect stops first, a wall with 800 hit
    points, `radius + 7.5` ahead of the caster, along its facing.
- **The bubble** (a pure Self cast).
  - **Casting.** It replaces any self-shield. Casting it while a bubble is up only takes the
    bubble down.
  - **Hit points.** 500, losing 100 a second.
  - **What it stops.** An attack that carries Damage or Status takes `amount × magnitude` (when not
    negative) from the bubble, and none of it gets through: the attack is deflected. A physical
    attack (Earth or Ice) becomes a knockback of the same amount with magnitude 0.698.
  - **Over time.** Damage over time also comes off the bubble (healing too, through a Shield
    element). As the original, it still reaches the character.
  - **Beams** reflect off a character in a bubble.
- **Armour** (Self with Earth or Ice):
  - **Casting.** It replaces any self-shield. Its hit points are 1500 with Earth, else 900, and they
    do not decay. While it is up, the wearer's mass is 1001; its template's mass comes back when it
    goes.
  - **Speed.** Each frame the wearer's speed multiplier is set to `0.9 − (0.075·E + 0.025·I)`.
  - **Ice armour** strikes as it forms: Earth and Piercing, 275 × `max(1, I + E)`, all around within
    3, as an arc strike that spares the wearer.
  - **What it stops.** A Damage attack that is physical first loses 10 (Earth armour without Ice)
    or 70 (else; as the original, Earth and Ice armour has Earth's hit points but this 70),
    then is multiplied by `1 − (0.05·I + 0.15·E)`.
    - The armour takes `amount × magnitude`.
    - What goes beyond its hit points gets through as the new amount (a hit). Otherwise the attack
      is deflected.
    - A knockback without Damage loses its amount and magnitude. Other attacks pass.
  - **Over time.** Healing does not get through. Fire over time is multiplied by `1 + I` and comes
    off the armour, and it still reaches the character.
- **Either self-shield** goes when its hit points are gone, or when its wearer dies.
- **The ward** (Self with Lightning, Arcane, Life, Steam, Water, Cold or Fire): one aura for each of
  those elements in the spell.
  - Each aura gives every character within `1.5·m` (the caster too) a resistance buff that makes it
    immune to that element.
  - It lasts `15 + 5·m` s, where m is the largest magnitude among the spell's elements, at least 1.
  - Before the auras start, the ward ends the caster's earlier self-cast auras, and its
    self-shield (as the original, armour too) unless this cast also made armour.
  - Making a bubble or armour ends the self-cast auras too.

In the world (OM-71), mines and barriers:
- **Chains.** A layout places its first links; each link grows the next after a delay (0.1 s for a
  mine, 0.075 s for a barrier), while the chain's range lasts.
  - Each link spends one spacing of the range. A link grows the next only while what is left after
    its own spacing is above zero.
  - The next link stands at the link's ground plus a step. Along a ring the step turns by one link's
    angle at each link, and so does the facing.
  - The next link's ground is looked for 0.75 above that spot, 1.5 down, on the level only. A
    mine looks from 0.75 above its own height (0.85 over its ground), as the original.
  - There is no next link when no floor is found, or when a shield stands on the line from the
    link to that probe point (a shield's segment test with tolerance 1).
- **Layouts.** A ring of radius r and n links:
  - The first two links are r ahead, turned half a link's angle (π/n) each way.
  - The step is the chord between them; each chain goes its own way round.
  - The first links' ground is looked for 4 down from the ring point. Force barriers look from
    1 higher; lines look from 1 (mines) or 2 (barriers) higher.
  - **Mines:** n = ⌊πr⌋ = 12 for r = 4.
    - Force: centred 1 behind the caster, range `2π·4/8` each way (four mines).
    - Area: centred on the caster, range `π·4 − chord` each way (the full ring).
  - **Barriers:** n = ⌊2πr / (2R)⌋ with R = 1.25 (solid) or 0.75 (elemental).
    - Each barrier is scaled by `1 + frac/n` to close the ring.
    - Force: r = 4, range `2π·4/8` each way. It is centred 2 behind for a spell with no status
      element and no Lightning, else on the caster. Its blows have a quarter of the magnitude.
    - Area: r = S + 3, range `(2πr − chord)/2` each way. As the original, with an odd n each
      chain makes `(n + 1)/2` barriers, so the last two stand on each other (one cast's barriers
      spare each other).
    - Force and Area barriers shake the camera, 0.25..0.75 for `0.5·S` s.
  - **Lines** (Weapon, raised when the cast clip's action ends, as the wall):
    - The first link is `radius + 2.5` ahead, then a link every 2.5.
    - There are `2 + ⌊max(Total/2, 1)⌋` links.
    - Each faces across the line.
- **A mine.**
  - **Placing.** It stands 0.1 above its ground. Laying it sets off the mines and breaks the
    barriers within 1.
  - **Arming.** It arms when its rising clip ends (2.167 s at 3×, about 0.72 s).
  - **Fuse.** Once armed, a touch within 0.9 lights a 0.5 s fuse: a character that is neither
    ethereal nor levitating, or a missile.
    - Its owner too. The original keeps a hold for the owner standing on it, but it only lasts
      until the mine is armed, so in effect it never holds.
  - **Blows.** A Damage or Status blow of any strength sets the fuse to 0.1 s.
  - **Going off.** It goes off when its fuse runs out, or when its owner is farther than 30: a
    full blast of radius 3 with `(Shield, Force)` damage, sparing itself.
- **A barrier.**
  - **Placing.** It stands `R·scale + 0.05` over its ground.
    - Raising it breaks the barriers of other casts within `0.8·R·scale` and sets off the mines
      there. A chain's later links clear `1.5·R·scale`.
    - Then everyone within `1.25·R·scale` but its owner, and not its own cast's barriers, is put
      on the cast's shared hit list.
    - A solid barrier also strikes them with `(Projectile, Area)` damage.
  - **Hit list.** All barriers of one cast share one list. A target stays on it for 0.25 s, timed
    by the cast's first barrier still standing.
  - **Touch.** It strikes anything damageable that touches it, once per 0.25 s, its owner too.
    Other barriers and ethereal characters are spared.
    - The touch reaches `R` around its centre, and `R` (elemental) or `2R` (solid) around its foot.
    - A solid barrier strikes only while its effect lasts; an elemental one always.
    - With Lightning, the strike is a bolt to the toucher from 1 (elemental) or R (solid) above
      the barrier's centre, with a range of 1 (the owner's spell-range buffs count).
      - From each target it chains to the nearest thing within the range left and π/4 of the
        bolt's way. Each link's length comes off the range.
      - It chains to characters that are neither ethereal nor frozen, solid barriers, missiles
        and other damageable things, never to mines, shields or elemental walls. It skips anything
        on the cast's hit list, and adds every target it strikes.
      - It stops at a barrier, a shield or a wholly deflected strike. A blocking character takes
        it into its weapon as plain Lightning.
      - With no next target it fades on along its way.
  - **Solid barriers** stand in the world as a ball of radius `R·scale`: characters and missiles meet it.
  - **Effect times** (solid):
    - Ice: I s, or Z s with Lightning (a tesla). An ice geyser (another element of Water, Cold,
      Fire, Steam or Poison, and no Arcane or Life) instead uses the dominant element's magnitude.
    - Earth: only as a volcano (Water, Cold, Fire, Steam or Poison, no Arcane or Life), five times
      the dominant element's magnitude.
    - The dominant element is Lightning if present, else the strongest of Water, Cold, Fire and
      Steam (the first on a tie). As the original, Water wins with magnitude 0 when none of them is
      in the spell.
    - A barrier whose physical part is Ice alone melts (its hit points go to 0) after I s, or Z s
      for a tesla.
  - **Hit points** wear away by 10 every 0.2 s (as the original, at most once a frame).
  - **Blows** follow the rules above, with Arcane taken out of the blow first.
    - A solid barrier's contradicting element puts out its effect (the effect time goes to 0) and
      does no harm.
    - A blow its armour stops entirely is deflected.
    - A solid barrier loses the amount, not `amount × magnitude`.
  - **Going.** At no hit points it breaks.
    - A solid barrier with Arcane or Life blasts (full, radius 5, its touch damage) and goes.
    - Another solid barrier sinks for 0.25 s first, still in the characters' way.
    - A broken barrier leaves its cast's hit list at once.
    - An elemental wall goes at once.

### 5.7 Magick: see section 10.

### 5.8 Blasts (`Blast`)

- `FullBlast(radius)`: all `IDamageable` within the radius (except the excluded one), with line of sight from
  the center; distance = to the center minus the target radius. Magnitude falloff
  `k = 1 − (d / radius)³`, **except** for collections containing Arcane/Life. Camera shake
  `radius/3`, 0.3 s.
- `GroundBlast(radius)`: no falloff, does not hit levitating targets, shake 2 / 0.9 s.
- Both call `Liquid.Freeze` in a circle (cold freezes water).

In the world (OM-71), for a damage collection:
- **Who is reached.** Every damageable entity within the radius of the centre: none hidden behind
  a shield, none ethereal, and the excluded one left out. A ground blast also leaves out the
  levitating.
- **Distance.** It is measured along the line from the centre to the entity, up to where that line
  first meets the entity's surface, minus the entity's radius once more. This is as the original:
  the radius comes off twice. A distance below 0 counts as 0.
- **Line of sight.** The line, up to the surface, must not cross the level.
- **Falloff.** A target at a distance up to the radius takes the collection with its magnitudes ×
  the falloff (a full blast; none for Arcane or Life, and none for a ground blast). It is struck
  from the centre.
- **Cues** (from the owner, or the blast's source when it has none: a missile, a barrel). A camera
  shake, at radius/3 for 0.3 s (a full blast) or 2 for 0.9 s (a ground blast).
  Each element of the collection also raises a blast cue, which carries how far the blast reaches
  in each of eight directions before the level or a shield cuts it. A ground blast ignores both.
  The renderer draws the element's blast and its decal from that cue; a full blast with Arcane or
  Life draws the arcane blast.
- **With other systems.** Liquid freezing (OM-66) and the ground blast's ice spikes (phase 3).

---

## 6. Spell damage calculation (`Spell.CalculateDamage`)

The result is a `DamageCollection5`: up to 5 entries `Damage {AttackProperty, Element, Amount, Magnitude}`.
An empty entry also takes a slot. `ScaleAmount(base, m) = base·√m`.

`AttackProperties` (flags): Damage 1, Knockdown 2, Pushed 4, Knockback 6 (= Pushed | Knockdown),
Piercing 8, ArmourPiercing 0x10, Status 0x20, Entanglement 0x40, Stun 0x80, Bleed 0x100.

There is damage for Spray, Projectile, Shield, Beam and Lightning; a push or a magick has none (the
original throws for a push). Spray × Magick and Projectile × Self give an empty collection: a
projectile cast on oneself is damaged by the projectiles as they fall (Force). Two entries combine
as `amount = (a₁m₁ + a₂m₂)/(m₁ + m₂)`, `magnitude = m₁ + m₂`, the flags and elements joined.

OpenMagicka: an empty entry carries nothing, so it is left out. A magnitude below zero (possible after
cancelling, §3.1) counts as zero here; the original would take its square root and get NaN.

### 6.1 Base constants (`Defines`, static, the "balance table")

| Name | Value | Name | Value |
|---|---:|---|---:|
| `SPELL_DAMAGE_EARTH` | 150 | `SPELL_DAMAGE_BARRIER_LIGHTNING` | 130 |
| `SPELL_DAMAGE_ICEEARTH` | 275 | `SPELL_DAMAGE_BARRIER_ICE` | 275 |
| `SPELL_DAMAGE_ICE` | 180 | `SPELL_DAMAGE_BARRIER_LIFE` | −600 |
| `SPELL_DAMAGE_LIGHTNING` | 250 | `SPELL_DAMAGE_BARRIER_ARCANE` | 225 |
| `SPELL_DAMAGE_LIFE` | −180 | `SPELL_DAMAGE_BARRIER_STEAM/FIRE/POISON` | 225 |
| `SPELL_DAMAGE_ARCANE` | 225 | `SPELL_DAMAGE_SELF_FIRE` | 60 |
| `SPELL_DAMAGE_FIRE` | 60 | `SPELL_DAMAGE_SELF_EARTH` | 100 |
| `SPELL_DAMAGE_COLD` | 25 | `SPELL_DAMAGE_SELF_ARCANE` | 200 |
| `SPELL_DAMAGE_STEAM` | 280 | `SPELL_DAMAGE_SELF_ICE` | 120 |
| `SPELL_STRENGTH_WATER` | 70 | `SPELL_DAMAGE_SELF_STEAM` | 80 |
| `STATUS_BURN_DAMAGE` | 60 | `SPELL_DAMAGE_SELF_LIGHTNING` | 80 |
| `STATUS_POISON_DAMAGE` | 50 | `SPELL_DAMAGE_SELF_POISON` | 50 |
| `STATUS_LIFE_DAMAGE` | −80 | `MJOLNIR_DAMAGE` | Damage+Pushed, Lightning, 1000, 1 |

(A negative amount = healing. `SPELL_DAMAGE_ICE`, `SELF_EARTH/ICE/POISON`, `BARRIER_STEAM/FIRE/POISON`
are not used in the spell core.)

Common "building blocks":

- **StatusMix** (if W/C/F/T/P present): `Status`, elements `Element ∩ StatusEffects`,
  amount `50·min1(P) + 60·min1(F)`, magnitude `min1(W + F + C + P + T)`.
  Note: all statuses of this entry get the same amount (as DPS).
- **LifeStatus** (if L present): `Status`, element Life, amount `−80·√L`, magnitude 1
  (for Self, `L`).
- **StatusB** ("CalculateStatus"): `Status`, elements `Element ∩ (StatusEffects ∪ Arcane)`, plus
  Water if Steam is present; amount `60·√F`; magnitude `min1(W + F + C + 0.5·T)`.
- **Misc(cast)**: if W or A present, flag `Pushed`; elements Water (+ Earth/Arcane for
  Area/Weapon); amount `trunc(70·W)` (+ `trunc(120·A)` for Area/Weapon); magnitude
  `W` (+ `E + A` for Area/Weapon); with Earth (Area/Weapon), also the `Knockdown` flag.

### 6.2 Spray

| Entry | Force / Area | Weapon | Self |
|---|---|---|---|
| StatusMix | yes | yes | yes |
| LifeStatus | (unreachable for Spray) | | |
| Direct (if C/F/Z/T) | `Damage`, elements `∩{C,F,Z,T}`, amount `60√F + 25√C + 280√T`, mag `min1(F+C+T+Z)` | same | amount `60√F + 25√C + 80√T` |
| Water push (if W) | `Pushed`, Water, amount `trunc(70·W)`, mag `1 + W/4` | mag `W` | — |
| Water marker (if W) | `Damage`, Water, amount 0, mag 1 | same | — |

Lightning in a spray (Steam+Lightning) adds no amount, but adds the Lightning flag (⇒ double
damage against Wet targets, see 7).

### 6.3 Projectile

- **Force, with Earth**: `Damage`, elements `Element ∩ {E, I}` (+Cold with Ice), amount
  `150·E² + 275·I²` (quadratic!), mag 1; × speed/50 (5.3).
- **Force, Ice only**: `Damage`, elements `Element ∪ {Cold, Earth}`, amount
  `275·√I + 250·√Z + 225·√A − 180·√L − 180·√L` (Life is counted twice), mag 1; per shard mag
  ×1/N. The shards pierce, but the Piercing flag is not part of this entry.
- **Force, any Ice + statuses** (with Earth too): additionally `Status`, elements `∩StatusEffects`,
  amount `50·min1(P) − 80·min1(L)`, mag `min1(W + C + P + L)`.
- **Area**: 
  - Status (if W/C/F/T): elements `∩StatusEffects`, amount `60·min1(F)`, mag `min1(W+F+C+T)`;
  - LifeStatus;
  - `Damage`, elements `(Element ∩ Instant) ∪ Earth` (fire/cold do not get into the mask!),
    amount `60√F + 25√C + 280√T + 250√Z + 275√I − 180√L + 225√A`, mag `min1(F+C+T+Z+I+A)`;
  - Water: `Pushed`, amount `trunc(70W)`, mag `W`;
  - Earth: `Knockdown`, amount `trunc(150E)`, mag `E`.
- **Weapon**: if Ice, `Damage` (+Piercing), Earth, amount `2·275·√I + 150·√E`, mag 1;
  then Misc(Weapon), StatusB, an empty entry.
- **Self**: no entries; the projectiles that fall on the caster carry Force damage.

### 6.4 Shield (damage of mines, elemental walls, Wave, etc.)

1. Life (ignoring Shield) → `Damage`, elements `∩Beams`, amount −600, mag 1;
   otherwise Arcane → `Damage`, Arcane, 225, mag 1.
2. Statuses → a StatusB-like entry: elements `∩(StatusEffects ∪ Arcane)` (+Water with Steam),
   amount `60·√F`, mag `min1(W + F + C + 0.5·T)`.
3. W and (E or I) → `Pushed`, Water, amount `trunc(75·√W)`, mag `√W`.
4. Z → `Damage`, Lightning, amount `130·√Z`, mag 1.
5. A and no E/I → `Knockback`, None, amount 500, mag 2.

### 6.5 Beam

1. StatusMix (if W/C/F/T/P), with the extra `Damage` flag when W is present, +Water with Steam.
2. LifeStatus.
3. `Damage`, elements `Element ∩ {Z, A, L, T, C, F}`, mag 1, amount:
   - not Self: `60√F + 25√C + 125√Z + 280√T − 180·min1(L) + 225·min1(A)`;
   - Self: `60√F + 25√C + 80√Z + 80√T − 180·min1(L) + 200·min1(A)`.
   Arcane/Life strength does **not grow** with count; the count increases the beam duration.
4. Water → `Pushed`, amount `trunc(70W)`, mag `W`.

### 6.6 Lightning

1. `Damage`, all elements of the spell, amount `250√Z + 225√A − 180√L`, mag 1.
2. StatusMix (if W/C/F/T/P), +Damage flag with W.
3. An empty entry.
4. Misc(Area).
5. StatusB, if there are statuses or Steam.

### 6.7 Resulting DPS (reference)

For continuous effects (Spray Force, Beam), magnitudes are divided by 4 and applied once per 0.25 s,
so "amount per second" ≈ entry amount × entry magnitude. Example: a 5×Steam spray ≈
`280·√5 ≈ 626` before resistances; an Arcane beam = 225/s for the whole duration of `1 + 2A` s.

---

## 7. Applying damage to a target

### 7.1 Attacker modifiers

Before each entry is sent: `amount = amount·M + B`, where over all of the attacker's buffs of type
`BoostDamage` that overlap by element: `M = Π buff_magnitude`, `B = Σ buff_amount`.
(Network mode: the client sends `DamageRequestMessage`; the `DamageFeatures` flags D/N/K/E decide
whether damage/notification/knockback/effects are applied.)

### 7.2 `Character.InternalDamage`: order for one entry

1. Dead / immortal / no resistance table → nothing.
2. `Entanglement`: if the target is not burning, entangle with strength magnitude, done.
   An entangled target is freed by Damage and by fire Status.
3. An active `SelfShield` processes the entry (5.6) and may change/zero it.
4. Knockback:
   - `Knockback` (= Pushed|Knockdown): impulse with elevation 0.698 rad (40°), the target is knocked down
     (if possible);
   - otherwise `Pushed`: impulse with elevation 0.1745 rad (10°); `Knockdown`: knockdown if
     `magnitude ≥ KnockdownTolerance` (5000 = immunity).
   - Impulse: "power" = amount, "distance" = magnitude;
     `d' = d · max(0, 2·√(1 − (mass − power/4)/power))`; then a ballistic velocity
     for a flight of `d'` at the elevation angle, ×1.4. Immunity: immortality, ethereal, solid
     armor, mass > 1000, entanglement, death.
5. Block (Earth/Lightning Damage while blocking with an item): the blocked part is subtracted; if
   fully blocked, `Deflected`.
6. **Statuses before resistances** (section 9): Fire→Burning, Cold→Cold, Water→Wet, Poison→Poisoned,
   Life→Healing, Steam→Wet. DPS = entry amount, magnitude = entry magnitude.
7. **Resistances** (section 8) → modified amount and magnitude. `|magnitude| ≈ 0` → `Deflected`, done.
8. `Stun` (amount > 0) → stun timer; `Bleed` (amount > 0) → Bleeding status (DPS = the template's `BleedRate`).
9. For the `Damage` flag:
   - Lightning against a **Wet** target → `amount ×2`;
   - Life → reduces the Poisoned magnitude by the entry magnitude (until removed);
   - Earth/Ice/Arcane against a **Frozen** target → `amount ×3` (otherwise blood/gore);
   - `HP −= amount × magnitude`; HP is capped above at MaxHP.
   - Results: Damaged / Healed / Deflected (0) / Hit (`amount × magnitude > HitTolerance`) / Pierced.
10. Death: if HP ≤ 0 → Killed; if the target was Frozen or HP ≤ `−0.5·MaxHP − 500` (the latter only
    with blood and gore on) → **OverKill** (shatters into pieces, no corpse; HP = `−MaxHP − 1000`, but
    only if the entry has an element other than Arcane; the OverKilled result is reported either way).
    Achievement for damage > 9000.

The result for the collection is the OR of the entry results, but `Deflected` remains only if all
five entries were "deflected".

In OpenMagicka, steps 6–10, the push velocity and regeneration are `OpenMagicka.Gameplay.Combat.DamageRules`
(with `StatusSet` for §9 and `Resistances` for §8); steps 1–5 (immunity, entanglement, the
self-shield, applying pushes and knockdowns, blocking) belong to the character (OM-32).

Status DoT damage goes a separate path (`Damage(float, element)`): resistances are not applied
(they are already accounted for in the DPS on application), but fire against Greased ×2, and shield/armor absorb it.

Pushes (step 4) in detail: the direction is from the attack point to the target, flattened. The
reach is `magnitude · max(0, 2·√(1 − (mass − amount/4) / amount))`; nothing happens below 10⁻⁶ or
for an immortal, ethereal, solid-shielded, entangled or dead target, or one heavier than 1000. The
velocity flies that reach on flat ground at the elevation angle, ×1.4: `1.4·√(g·d / sin 2θ)`.

### 7.3 Regeneration

`HP += RegenRate·dt` (from the template), only if HP is in (0, MaxHP] and the target is not Burning/Poisoned.

---

## 8. Resistances

Structure `Resistance { ResistanceAgainst: Elements, Multiplier: float, Modifier: float, StatusResistance: bool }`.

Sources for a character:
1. The character template (`CharacterTemplate`, `Content/Data/Characters/*.xnb`): 11 entries, by
   default `×1, +0, false`; the file contains a list of overrides
   `(element:int32, multiplier:float, modifier:float, statusResistance:bool)`.
2. For the avatar, additionally the staff (`Equipment[1]`, `Item.Resistance`, `Content/Data/items`).
3. `BuffType.Resistance` buffs (auras, magicks, the ward from 5.6).

`Damage.ApplyResistances`: for **each** element flag of the entry, the template's entry for that
element, then the staff's (avatars):
- `mag_mult *= |Multiplier|`; if Multiplier < 0, an "inversion" flag (Life is marked separately);
- `amount += Modifier`, clamped at zero (a positive amount does not go below 0, a negative one does not
  go above 0). The sign is taken from the amount as it stands at each step, so a heal clamped to 0 is
  then treated as damage by the next modifier, and an entry of amount 0 (the spray's water marker)
  takes a positive modifier as damage.
Then each `Resistance` buff whose elements overlap the entry's, **once per buff** (not per
element): the same, except that the multiplier is used with its sign (`mag_mult *= Multiplier`), and
"Life" means the buff's elements include Life.
- After the loops: if Life had a negative multiplier → `amount = |amount|` (healing hurts,
  for example, undead); otherwise if any negative multiplier occurred → `amount = −amount`
  (element absorption heals). `magnitude *= mag_mult`. (A buff with a negative multiplier
  therefore flips both the amount and the magnitude: the damage keeps its sign.)
- `ArmourPiercing`: multipliers ≤ 1 and modifiers ≤ 0 are ignored.

Physics props (`DamageablePhysicsEntity`) use an "inclusive" variant: the magnitude is multiplied by
the **largest** multiplier among the entry's elements (no product, no inversion, armour piercing
ignored), and the modifiers apply as above.

Result: `damage ≈ (amount + ΣModifier) · magnitude · Π|Multiplier| · special_multipliers`.

Status resistance (`AddStatusEffect`, section 9): the multiplier/modifier of the matching
element (Fire for Burning, Cold, Water for Wet and Greased, Steam, Poison, Life) is applied to the
status DPS/magnitude; `StatusResistance = true` forbids the status completely. For the
Wet→Frozen conversion, the `StatusResistance` of the **Ice** element is checked.

**Scaling by player count** (when loading the template of a non-friendly character):
`k = 1 − (players − 1)·0.00666` (`DIFFICULTY_RESISTANCE_MODIFIER`); positive multipliers and
modifiers `×k`, negative ones `÷k`.

`ResistanceAgainst(elements)` (for AI and some magicks): `1 − clamp(ΣModifier/300 + ΣMultiplier, −1, 1)`,
where Frozen gives −350 to the modifier and Greased doubles the multiplier. 1 = full immunity. The
original checks the whole query, not the element at hand: with Frozen, every element's modifier loses
350 when the query includes Earth; with Greased, every multiplier doubles when it includes Fire.

---

## 9. Status effects

`StatusEffects` (short, flags): Burning 1, Wet 2, Frozen 4, Cold 8, Poisoned 0x10, Healing(=Life) 0x20,
Greased 0x40, Steamed 0x80, Bleeding 0x100; `NR_OF_STATUSEFFECTS = 9`. A character has an array of 9
slots (index = log2); the mask of current statuses is recomputed after every change.

Each status has: `magnitude` (alive while > 0), `DPS`, and an accumulator of fractional damage (damage is dealt
in whole units).

| Status | Mag. cap | Mag. decay | Effect |
|---|---:|---|---|
| Burning | 3 | 0.2/s (1 unit ≈ 5 s) | DPS grows by 25 %/s of itself, up to `500·magnitude`; Fire damage; causes panic |
| Cold | 3 | 0.1/s | speed multiplier `0.333 + 0.667·0.001^magnitude` |
| Wet | 1 | **none** (until removed) | Lightning ×2; conjuring Lightning hits self |
| Frozen | 3 | 0.25/s (1 unit = 4 s) | immobilized, cast is interrupted; Earth/Ice/Arcane ×3; kill → OverKill |
| Poisoned | 1 | none (except special "expiring" ones) | damage `DPS·magnitude`/s (Poison); speed ×0.5; no regen |
| Healing | 3 | 1/s; at full HP, immediately | heals `DPS`/s (DPS is negative); on creation `magnitude = 0.5 + 0.5·m` |
| Greased | — | none | Fire ×2; ignition: DPS ×4 and the grease is removed |
| Steamed | 1 | 1/s | causes panic (source not found in vanilla code) |
| Bleeding | — | none (except "expiring" ones) | damage `BleedRate`/s; removed by healing |

Stacking identical statuses: magnitudes add up (Healing takes the maximum); DPS: Burning takes the maximum,
others take the magnitude-weighted average. Fire DoT is dealt with the Fire element, poison with Poison,
healing with Life, others with Earth.

Details of each tick (in this order: the cap, then the decay, then the damage):

- **Burning**: magnitude capped at 3, then −0.2·dt; DPS `min(DPS + 0.25·DPS·dt, 500·magnitude)`;
  damage `DPS·dt` (the magnitude does not multiply it).
- **Cold**: −0.1·dt, then capped at 3; no damage.
- **Wet**: capped at 1; **Frozen**: capped at 3, then −0.25·dt.
- **Poisoned**: magnitude capped at 1 (or, for an expiring poison, −dt instead); damage
  `DPS·magnitude·dt`; speed ×0.5 (not during cutscenes).
- **Healing**: damage `DPS·dt` (negative: healing); capped at 3; at full HP the magnitude drops to
  `dt`, so it ends this tick; then −dt.
- **Steamed**: capped at 1, then −dt. **Bleeding**: damage `DPS·dt`; an expiring one −dt. **Greased**:
  nothing.
- Damage is dealt in whole units: the fractional rest is carried, and the whole part (rounded towards
  zero) is dealt.
- Statuses decay only through their magnitude: a Wet, Greased or plain Poisoned character stays so
  until another status or damage removes it (§13, question 2: confirmed in the status code).

The magnitude a new status starts with: as given, except Healing `0.5 + 0.5·m`, Steamed 1, Bleeding 1
(an expiring bleed keeps its own). Expiring statuses ("effect stops", used by abilities that inflict
bleeding or poison for a while): when both sides of a stack expire, the newer one replaces the older
(DPS and magnitude). Healing stacks are quirky: the magnitude is the larger one, but the DPS weights
are `m₁/max` and `m₂/max`, which need not add up to 1.

Interactions on application (`AddStatusEffect`):

| Applied | Condition | Result |
|---|---|---|
| Burning | has Cold | Cold removed, Burning is **not** applied |
| Burning | has Wet | Wet removed (drying 0.9 s, effect `drying_steam`), Burning not applied |
| Burning | has Frozen | Frozen mag. −= fire mag.; Burning not applied |
| Burning | Greased | DPS ×4, Greased removed |
| Burning | — | `DPS = (DPS + modFire)·multFire` |
| Cold | Burning or drying in progress | Burning removed, Cold not applied |
| Cold | Wet (and Ice has no StatusResistance) | **Frozen** with mag. 1, Wet and Cold removed, cast interrupted |
| Cold | already Frozen | Frozen mag. = 1 (refresh) |
| Wet / Steam | Burning or drying in progress | Burning removed, Wet not applied |
| Wet | Greased | Greased removed |
| Greased | Wet | Wet removed |
| Steamed | Cold | Cold removed |
| Healing | Poisoned / Bleeding | both removed |

The "drying" state (`DryTimer`, 0.9 s ≈ `STATUS_DRYING_TIME`) blocks new Wet/Cold right after
evaporation.

Resistances on application, per status, with the element in brackets: Burning (Fire): `DPS = (DPS +
modifier) · multiplier`, the magnitude unchanged. Cold (Cold), Wet (Water, also from Steam), Greased
(Water), Steamed (Steam), Poisoned (Poison), Healing (Life): `DPS += modifier`, `magnitude ×=
multiplier`. The multiplier is the template's × the staff's (avatars) × every resistance buff that
covers the element (whole, not just overlapping); the modifiers add up the same way. Only the
**template's** `StatusResistance` forbids a status. A status whose DPS or magnitude ends up zero is
not applied, except that Cold and Wet only check the magnitude. So a grease with no DPS never sticks
(the Grease magick makes the ground slippery instead; only grease lumps, DPS 10, grease a
character), and nothing in vanilla applies Steamed (§13, question 3).

More detail on the interactions above: grease is removed by Burning even when Burning itself is
stopped (by Cold, Wet or Frozen), and by Wet even when Wet is stopped by burning; Burning on a Frozen target only
thaws it when the target is not also Wet. Cold that meets Burning (or drying) is not applied; if it
meets Wet it freezes the target even so. Drying starts when burning removes Wet (or thaws the last of
Frozen) unless it is already running, and restarts when Cold or Wet puts out a fire. (The original
starts it only while the status's sound plays; OpenMagicka does not tie rules to sounds.)

Reported outcomes: a status that took is "added" (also when a resistance turned its magnitude
negative, as a Life multiplier below zero does to healing: it then holds no slot, but the cure of
poison and bleeding already happened); one that met an opposite, froze the target or refreshed Frozen
is "removed"; one resisted to nothing or forbidden is neither, even if it washed off grease or cured
poison on the way. An expiring status stacked with a lasting one lasts.

OpenMagicka difference: the original refreshes its status mask after an addition for the first four
statuses only (Burning, Wet, Frozen, Cold), so a fresh Poisoned, Healing or Greased is invisible to
the rules until the next tick; ours sees it at once.

---

## 10. Magicks

### 10.1 Recognition

`MagickTree`: a prefix tree; a node has 11 children (by element index), a leaf stores a
`MagickType`. The sequences (below) are given as **queue elements after merges** (Steam/Ice are
separate elements).

- `IsMagick` / `CombineMagick`: walk the queue from the first element; every step must
  exist, the final node must hold a magick, and the magick must be allowed. Then
  `CombineMagick` replaces the queue with a single entry `{Element = All, MagickType}`.
- `GetMagickType` (HUD hint): walks every entry without stopping. When the current node has no
  child for an entry, the hint becomes that node's magick (possibly none) and the walk restarts
  from the root with the next entry; otherwise it becomes the child's magick. The last value is
  the hint.
- An entry's child slot is its element index, `round(log2(mask))` (a magick entry, `All`, has
  index 15, beyond the 11 slots, so it never matches).
- `Equatable(combo, queue)`: prefix comparison by flags (for highlighting in the magick book).
- Cast: Boost (Space) → `CombineMagick` or an already present magick entry → `CastType = Magick` →
  `SpecialAbility.Execute`. **Instant** magicks (Revive, Haste, Invisibility, Teleport, Napalm,
  ChainLightning) can be cast even while Frozen / in flight / knocked down.

### 10.2 Permission (unlock)

Unlocking is the bit `1 << MagickType` in `Player.UnlockedMagicks` (ulong; stored in the save).
- Campaign / Mythos / Challenge / StoryChallenge: a magick is allowed if it is unlocked for at least one
  playing player. Other modes (Versus, etc.): per player. NPCs (player = null) can
  use any.
- Sources: the trigger action `<unlockMagick magicktype="…" delay="…"/>` and the book
  `<spawnMagick magick="…" area="…" immovable="…"/>` (`BookOfMagick`, pickup → unlock) in scene
  XML (`Content/Levels/**.xml`). In Versus: `VersusRuleset`/`Krietor`.
- `VANILLA_CAMPAIGN_MAGICKS = 0x7FEFFE` (types 1–22, except MeteorS) is the progress for the achievement
  `iputonmyrobeandwizar`. `MYTHOS_CAMPAIGN_MAGICKS` = Revive, Haste, Portal, TractorPull,
  ProppMagick, Levitate, ChainLightning.

### 10.3 Player magick list

Keys: Q Water, W Life, E Shield, R Cold, A Lightning, S Arcane, D Earth, F Fire. The "Keys"
column is one of the shortest layouts, computed by our own simulator of the section 2 rules
(not the only one).

| # | MagickType | Element sequence | Keys | Instant | Where unlocked (campaigns) |
|---:|---|---|---|:-:|---|
| 1 | Revive | Life, Lightning | W A | ✓ | WizardCastle/wc_s2, Tsar/twc_s1 |
| 2 | Grease | Water, Earth, Life | Q D W | | WizardCastle/wc2_s1, wc_s4 |
| 3 | Haste | Lightning, Arcane, Fire | A S F | ✓ | WizardCastle/wc_s3, Tsar/twc_s1 |
| 4 | Invisibility | Arcane, Shield, Steam, Arcane | S E Q S F | ✓ | Ruins/ru_s1 |
| 5 | Teleport | Lightning, Arcane, Lightning | A S A | ✓ | WizardCastle/wc2_s1, wc_s4 |
| 6 | Fear | Cold, Arcane, Shield | R S E | | Swamp/sw_s6 |
| 7 | Charm | Life, Shield, Earth | W E D | | Mines/mi_s4H, Swamp/sw_s6 |
| 8 | ThunderB (Thunder Bolt) | Steam, Lightning, Arcane, Lightning | Q F A S A | | Highlands/hl_s3 |
| 9 | Rain | Water, Steam | Q Q F | | MountainDale/md_s1 |
| 10 | Tornado | Earth, Steam, Water, Steam | D Q F Q Q F | | Havindr/havindr_s1 |
| 11 | Blizzard | Cold, Ice, Cold | R R Q R | | EndofWorld/ew_s1 |
| 12 | MeteorS (Meteor Shower) | Fire, Earth, Steam, Earth, Fire | F D F Q D F | | WizardCastle/wc_s2 |
| 13 | Conflagration | Steam, Fire, Steam, Fire, Steam | Q F F F Q F F Q | | Havindr/havindr_s5 |
| 14 | ThunderS (Thunder Storm) | Steam, Steam, Lightning, Arcane, Lightning | Q Q F F A S A | | Mines/mi_s1, Swamp/sw_s4 |
| 15 | TimeWarp | Cold, Shield | R E | | (not found in the campaign; Challenges: chs_osotc_s3, chs_vietnam_s3; BattleField) |
| 16 | Vortex | Ice, Arcane, Ice, Shield, Ice | Q R S Q E Q R R | | EndofWorld/ew2_s2 |
| 17 | SUndead (Raise Dead) | Ice, Earth, Arcane, Cold | Q R D S R | | Swamp/sw_s3 |
| 18 | SElemental | Arcane, Shield, Earth, Steam, Arcane | S E D Q S F | | Ruins/ru_s3s |
| 19 | SDeath | Arcane, Cold, Ice, Cold, Arcane | S R R Q R S | | Niflheim/nh_s3BF |
| 20 | SPhoenix | Life, Lightning, Fire | W A F | | Mines/mi_s1 |
| 21 | Nullify | Arcane, Shield | S E | | MountainDale/md_s4 |
| 22 | Corporealize | Arcane, Steam, Lightning, Shield, Arcane | S Q F A E S | | Ruins/ru_s6BF |
| 23 | CTD (Crash to Desktop) | Lightning, Lightning, Fire, Life | A A F W | | WizardCastle/wc_s2 |
| 24 | Napalm | Steam, Earth, Life, Fire, Fire | Q D W F F F | ✓ | Challenges only (chs_fields, chs_vietnam_s1..s3, Vietnam DLC) |
| 25 | Portal | Steam, Lightning, Shield | Q F A E | | Tsar_Rlyeh/tr_s2 (Mythos) |
| 26 | TractorPull | Earth, Arcane | D S | | Tsar_WizardCastle/twc_s1 (Mythos) |
| 27 | ProppMagick | Fire, Steam, Arcane | F F Q S | | Tsar_Mountaindale/tmd_s2 (Mythos) |
| 28 | Levitate | Steam, Arcane, Steam | Q S Q F F | | Tsar_Mountaindale/tmd_s5BF (Mythos) |
| 29 | ChainLightning | Lightning ×3 | A A A | ✓ | Tsar_Mountaindale/tmd_s4 (Mythos) |
| 30 | Confuse | Arcane, Shield, Lightning | S E A | | WizardCastle/wc_s2 |
| 31 | Wave | Earth, Steam, Earth, Steam, Earth | D Q D Q D F F | | Challenges (chs_woot_bout_of_madness) |
| 32 | PerformanceEnchantment | Life, Fire, Lightning, Fire, Life | W F A F W | | Challenges (chs_woot_bout_of_madness) |
| 33 | JudgementSpray | Ice, Ice, Arcane, Shield | Q Q R R S E | | Challenges (chs_dungeons_ch2_s2, chs_woot_bout_of_madness) |
| 34 | Amalgameddon | Arcane, Water, Lightning | S Q R A F | | (requires the Ice+Fire→Water trick) |

The value `NrOfMagicks = 35`. Types ≥ 36 (Grow, Shrink, Etherealize, Polymorph, Earthquake, Votal,
KnockDownChance, SummonCross, VladZap, SpawnSlime, SpawnSlimeOverkill, GreaseSplash, EtherealClone,
InflictBleedDamage, InflictPoisonDamage) are internal NPC/item abilities with no combination.

Effect classes (`Magicka.GameLogic.Entities.Abilities.SpecialAbilities`): Haste, Revive, Rain,
Thunderbolt, Teleport, MeteorShower, Charm, SummonZombie, Conflagration, SummonPhoenix, Fear,
Invisibility, Thunderstorm, TimeWarp, Blizzard, Tornado, Grease, Vortex, SummonElemental,
SummonDeath, Corporealize, Nullify, CTD, Napalm, Portal, TractorPull, ProppMagick, Levitate,
Zap (ChainLightning), Confuse, Wave, PerformanceEnchantment, JudgementSpray, Amalgameddon.
Their behavior, casting and unlocks are in [`MAGICKS.md`](MAGICKS.md).

Localization: `#magick_<name>` (title) and `#magick_<name>d` (description), name in lower case
(`thunderb`, `sundead`, `performance` …).

---

## 11. What lives in data and what lives in code

| What | Where |
|---|---|
| Rules for the queue, merges, types, damage formulas, statuses, durations | **code** (hardcoded) |
| Magick combinations | **code** (`SpellManager`, table 10.3) |
| Character resistances, HP, regen, BleedRate, KnockdownTolerance, HitTolerance | `Content/Data/Characters/*.xnb` (`CharacterTemplateReader`) |
| Resistances and passives of staffs/weapons (`WetLightning`, `MeleeBoost`…) | `Content/Data/items/*.xnb` (`ItemReader`) |
| Magick unlocks | Scene XML: `unlockMagick`, `spawnMagick` |
| Spell visual effects | `Content/Effects/Spells/**` (`Spray/Spray_Fire1.xml`, `Self/Self_Life.xml`…), `Effects/Barriers`, `Effects/StatusEffects`; the key is the hash of the file name (`spray_<el>1`, `self_<el>`, `barrier_<el>1_b`, `projectile_<el>4`, `projectiletrail_<el>`, `projectilehit[_big]_<el>`, `statuseffect_*`, `drying_steam`, `fire_scourge`, `push_force`, `push_area`, `elementeffect_<el>`) |
| Sounds | XACT `Content/Audio/Spells.xsb/.xwb`: `spell_<el>_<spray|projectile|hit|blast|barrier|area|self|force|chasm>`, `spell_push_force/area`; UI: `ui_element_<el>` |
| Projectile and wall models | `Content/Models/Missiles/*` (Earth0–3, Ice3, IceEarth0–3…), barrier models |
| Beam texture | `Content/EffectTextures/Beams` |

---

## 12. Other `Defines` constants

Some constants are declared `const` and are inlined in IL at the use site, so their actual
use must be confirmed by literals in the code (the sections above give the values from the code).

| Constant | Value | Comment |
|---|---:|---|
| `SPELL_QUECAP` | 5 | queue capacity |
| `SPELL_RANGE_PROJECTILE_BASE/VAR` | 25 / 85 | projectile speed |
| `SPELL_STRENGTH_PUSH_BASE/VAR` | 50 / 500 | push strength |
| `SPELL_ANGLE_PUSH/SPRAY/LIGHTNING` | π/6, π/10, π/4 | angles |
| `SPELL_RANGE_PUSH/AREA_PUSH` | 1.5 / 3.5 | |
| `SPELL_RANGE_SPRAY/AREA/LIGHTNING/AREA_LIGHTNING` | 10 / 10 / 8 / 4 | maximums with 5 elements |
| `SPELL_RANGE_SHIELD/SHIELD_MIN/BUBBLE` | 5.5 / 3 / 1.3 | |
| `SPELL_RANGE_RAILGUN/_WIDTH` | 15 / 0.75 | (actual beam up to 75) |
| `SPELL_RAILGUN_BASE_TTL/MOD_TTL` | 1 / 2 | `1 + 2(A+L)` |
| `SPELL_TIME_SPRAY/LIGHTNING/PUSH/ICE/GROUND` | 4 / 1 / 0.25 / 0.35 / 15 | |
| `SPELL_AMOUNT_LIGHTNINGS/ICE` | 8 / 3 | bolts / shards per Ice |
| `SPELL_STRENGTH_SHIELD/BARRIER/BARRIER_ELEMENTAL` | 500 / 500 / 100 | HP coefficients |
| `SPELL_ARMOR_EARTH/ICE` | 90 / 50 | |
| `SELF_SHIELD_HEALTH/DECAY` | 500 / 100 | |
| `SELF_STONE_HEALTH/MULTIPLIER/MODIFIER` | 1500 / 0.15 / 10 | Earth armor |
| `SELF_ICE_HEALTH/MULTIPLIER/MODIFIER` | 900 / 0.05 / 70 | Ice armor |
| `SELF_ELEMENTAL_AURA_BASE_TTL/MODIFIER_TTL/RADIUS_MODIFIER` | 15 / 5 / 1.5 | ward |
| `BARRIER_DAMAGE_OVER_TIME` | 10 | barrier HP loss per step |
| `BARRIER_BLAST_RADIUS` | 2.6 | |
| `STATUS_DRYING_TIME` | 0.9 | |
| `STATUS_FROZEN_ARMOR/MAGNITUDE` | 50 / 10 | |
| `MAX_STATUS_MAGNITUDE` | 3 | status cap |
| `HITLIST_COOLDOWN` | 0.25 | repeat hit on a target |
| `KNOCKBACK_ELEVATION / PUSH_ELEVATION` | 0.698 / 0.1745 rad | |
| `BLOCKANGLE` | 1.0996 rad (63°) | block sector |
| `KNOCKDOWN_IMMUNITY_TOLERANCE` | 5000 | |
| `FALLING_DAMAGE_POWER / KNOCKDOWN_THRESHOLD` | −50 / 100 | falling |
| `LIGHTNING_FRIEDTIME` | 0.1 | |
| `MAX_NR_OF_SUMMONS_PER_CHARACTER / UNDEAD / FLAMER` | 16 / 1 / 4 | |
| `DIFFICULTY_RESISTANCE_MODIFIER` | 0.00666 | scaling by players |
| `PASSIVE_ABILITY_*` | 0.25 / 2 / 4 / 1.5 / 7 | item passives |

Element colors (`Spell.*COLOR`, HDR RGB): Earth (0.3, 0.2, 0.1), Water (0, 0.7, 1.3), Cold
(1, 1, 1.4), Fire (1.8, 0.6, 0.4), Lightning (0.75, 0.5, 1), Arcane (2, 0.4, 0.6), Life
(0.2, 1.6, 0.2), Shield (2, 1.5, 1), Steam (1, 1, 1), Ice (0.8, 0.9, 1.4), Poison (1, 1.2, 0).
The spell color is the average of the colors (Fire and Lightning weighted by magnitude).

---

## 13. Open questions

1. **Spray range**: the formula `R·(0.1·R·f)` gives ≈ `R²/10` (1×Fire ≈ 3.6, 5 elements ≈ 10).
   Is this intentional or an artifact? Needs checking against the game; for 1:1 we reproduce it as is.
2. **Wet and Greased do not expire** on their own (magnitude does not decay). Confirmed in the status code
   (§9): only other statuses remove them. Whether level water removes them is checked with the liquids
   (OM-29). Questions 6 and 11 are checked in play under OM-72.
3. **Steamed** (closed): nothing in vanilla code creates it. It is kept as a working status in case
   something applies it.
4. `const` constants from `Defines` (SPELL_TIME_*, SELF_*_MODIFIER, STATUS_*_TIME …): which are actually
   used (inlined in IL) needs a search for literals in IL, not by name.
5. Life is counted twice in ice projectile damage (Force, Ice only), a bug in the original (closed:
   kept, as the game's balance grew with it; a later "fixes to the original" option could change it).
6. In `(Projectile, Area)`, fire/cold are included in the amount but not in the entry's element mask (the damage
   is resisted as Earth/others). Confirmed in the damage code; not yet seen in play.
7. `Electrocute` in `Character` is empty: "water + lightning" in sprays/blasts does not shock Wet targets
   through this path. Check whether there is another mechanism (for example, via statuses).
8. The exact geometry of barrier/mine chains (step, segment scale, fitting of the segment count)
   is described approximately; needs a separate analysis of `Barrier.SpawnNextBarrier` / `SpellMine`.
9. Behavior of blades (`IceBlade`, `ArcaneBlade`), `UnderGroundAttack`, `IceSpikes` and blocking
   (`BlockDamage`): hit rates and damage are not analyzed in detail.
10. TimeWarp, Wave, PerformanceEnchantment in the campaign: found only in Challenges; clarify
    (they may drop from characters via `DropMagicks` or come from DLC campaigns).
11. Applying statuses **before** resistances means that a status's magnitude does not
    depend on the attack's `Multiplier` (only on status resistance). Confirmed in the damage code
    (§7.2 order); not yet seen in play on targets with fire immunity.
12. How exactly player-count scaling affects already cached templates (it is applied at
    template load, and templates are cached by `ContentManager`): possible dependence on load
    order.
13. Network model (who computes damage, `DamageFeatures`): whether compatibility is needed is decided outside
    this document.
