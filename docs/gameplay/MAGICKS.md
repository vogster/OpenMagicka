# Magicks (reimplementation spec)

Source of behavior: `Magicka.exe` 1.10.4.2 (see [`ENGINE_ANALYSIS.md`](../ENGINE_ANALYSIS.md)).
Scope: `Magicka.GameLogic.Entities.Abilities.SpecialAbilities` (the magicks' effects),
`SpellManager`'s magick rules, the boost state, `BookOfMagick`, the `unlockMagick` action.
Recognition, the combination table and where each magick is unlocked are in
[`SPELLS.md`](SPELLS.md) §10.

> **Clean-room.** Below are only rules, constants and identifiers in our own words. No original
> code is carried over. Code: `OpenMagicka.Gameplay.Spells.Magicks` (OM-37).

---

## 1. Games, players and unlocks

### 1.1 The game

- **Game types** (original numbers): Campaign 1, Challenge 2, Versus 4, Mythos 8,
  StoryChallenge 22.
- **Players.** A game has up to four players. Each player:
  - may or may not be playing;
  - has a 64-bit mask of unlocked magicks (bit `1 << MagickType`);
  - has its wizard (its avatar), alive or dead;
  - has a *dead age*: how long its wizard has been dead (0 while it lives).
- **May a magick be cast?**
  - A character with no player (an NPC) may cast any magick.
  - In Versus, each player has its own unlocks.
  - In every other game type, a magick any *playing* player unlocked serves everyone.

### 1.2 Unlocking

- **The trigger action** `unlockMagick` unlocks the magick:
  - in Versus, for every playing player;
  - elsewhere, for every player.
- **A book of magick** (`spawnMagick` in a scene, `BookOfMagick`) lies in the world:
  - An *immovable* book stays where it was put. Another one falls and tumbles; characters pass
    through it.
  - A *timeout* makes it vanish after that long; 0 means never.
  - Picking it up unlocks its magick and removes it. In Versus only the picker gets the magick
    (as the original, and its team mates; teams come with Versus). Elsewhere every player gets it.
  - A wizard picks up a book within 2.5.
- **Dying in Versus** drops a wizard's magicks as books (comes with Versus).

---

## 2. Casting

### 2.1 The magick button (boost)

An avatar's boost button does the first of these that applies:

1. **Nothing** while charging or dead.
   - A cast under way (another cast type, or a running spell) keeps its spell: steps 2 and 3 are
     skipped.
2. **Recognize.** A queue that spells out a magick, if the character may cast it, becomes one
   magick entry ([`SPELLS.md`](SPELLS.md) §10.1).
3. **Cast.** A queue holding a magick entry casts it: the cast type becomes Magick. An *instant*
   magick (Revive, Haste, Invisibility, Teleport, Napalm, ChainLightning) is cast at once when the
   wizard is frozen, flying or knocked down, since its state would not get to it.
4. **Vines.** An entangled character tears at the vines (0.5 of Earth off the entanglement) and,
   unless casting, plays `spec_entangled_attack`. Breaking a grip comes with grips.
5. **Boost.** With a standing shield ahead (§2.3) or a bubble on itself, the press counts as a boost.
   - A first press counts 1.
   - While boosting, each press adds 1, or 2 with the `ShieldBoost` passive.
   - Boost auras (its own and its staff's) and Boost buffs then multiply the count, each by its whole
     part. As the original, they multiply the whole count, the presses already counted too.
   - The press keeps the boost going for 0.25 s.
6. Otherwise nothing.

### 2.2 The cast

- **The clip.** The cast state plays the magick's clip, blended in over 0.075 s, and the wizard
  cannot turn. Each magick names its clip: `cast_magick_self`, `_direct`, `_sweep` or `_global`;
  a few use others (§3–§7).
- **The effect.** The clip's `CastSpell` action runs the magick. The spell is cleared first.
  - The cast type stays Magick until the clip ends, so no other cast starts under it.
  - An instant magick cast outside the cast state clears its cast type at once.
- **Failure.** A magick that cannot work plays `magick_fail`, as each of the original's says:
  - Revive with nobody to revive;
  - a sky magick indoors;
  - Crash to Desktop with nobody to take;
  - Napalm while its plane passes.

  Teleport over nothing fails silently.
- **NPCs** cast magicks through the AI (OM-40).

### 2.3 The boost state

- **Entering.** A character with boosts goes into the boost state if it has a shield to boost or
  any self-shield (armour too; it then leaves at once). Otherwise its boosts are dropped. This is part of the third common check, after
  the jump.
- **The clip.** `boost`, blended in over 0.2 s.
- **Each frame:**
  - Death, then hits (a push, a knockdown, vines, a stun) take it out first.
  - A bubble heals by 50 per boost.
  - Otherwise a shield ahead is mended by 100 per boost. Its most, 5000, comes back the next frame.
  - The boosts are then spent.
  - With neither a bubble nor a shield ahead, the state ends.
  - It also ends once 0.25 s pass without a press.
- **The shield to boost** is the nearest standing shield that a segment 20 along the flat facing
  meets. The meeting point must be nearer than 20 on the flat and within π/4 of the facing.

---

## 3. Self magicks

### 3.1 Haste (`cast_magick_self`, instant)

- **Duration.** 10 s. Casting it again sets the time back to 10 s.
- **Speed.** Each frame, with t the time left, the speed is multiplied by `2 − 0.5^t`. That stays
  close to ×2 until the last few seconds, then falls to ×1.
- **Order.** It applies after the armour's speed.
- **Death** ends it.
- **Sound.** `magick_haste`.

### 3.2 Invisibility (`cast_magick_self`, instant)

- **Effect.** The caster is invisible for 15 s, as with the item ability.
- **Sound.** `magick_invisibility`.

### 3.3 Levitate (`cast_magick_self`)

- **Duration.** 30 s. While it lasts the caster has no gravity and counts as levitating. Ground
  blasts and mines spare it, and its movement slides.
- **Hovering.** Each frame a probe looks for the ground, from halfway down the body to 0.75 below
  its feet.
  - Found at fraction f: the vertical speed gains `(1 − f) × 0.75`. That is per frame, as the
    original, not per second.
  - Not found: the vertical speed loses `dt × (0.5 + 1.5·k)`, where k grows by `dt/2` per frame up
    to 1 while nothing is found, and is reset by ground.
- **Liquids.** In the original the probe also meets liquids; that comes with liquids.
- **The end.** When the time runs out, gravity comes back. Death ends it at once.
- **Sound and effect.** It sounds like Haste (`magick_haste`); the effect is `magick_levitate`.

### 3.4 Teleport (`cast_magick_self`, instant)

- **Destination.** 10 ahead along the facing, moved to the nav mesh's nearest point (water
  allowed).
- **Landing.** The caster lands 0.1 above the floor found within 4 below that point. Its vines go.
- **Failure.** With no floor there, nothing happens, without a sound.
- **Walls.** As the original, it can pass walls.
- **Triggers.** Trigger areas the jump crosses see the caster enter them (comes with triggers).
- **Sounds.** `magick_teleporta` where it leaves, `magick_teleportb` where it arrives.

### 3.5 Revive (`cast_magick_self`, instant)

- **Who rises.** The playing player who has been dead the longest, among those whose wizard will
  not rise by itself: overkilled, or dead and not undying. A player a revival is already on its way
  to is skipped.
- **Failure.** With no such player, or no room in the level for the effect and the new wizard,
  it fails.
- **The spot** is the nav mesh's nearest point to 4 ahead of the caster. After 0.25 s the player's wizard rises there, unless it
  rose meanwhile some other way:
  - a new wizard from the old one's template, clips and script name, on the floor (at the spot
    itself with no floor);
  - with a tenth of its hit points, through the resurrection state and its `revive` clip;
  - the corpse goes.
- **Light.** The god rays fade out over 3 s more; they come with the renderer.
- **Not yet:**
  - Versus revival rules.
  - Dropping the old weapons (the original gives the new wizard none).
- **Sounds.** `magick_revive_cast` on the cast; `magick_revive` when the wizard rises.

---

## 4. Striking magicks

A *sky* magick fails indoors: the scene's `Indoor` flag. Those are Thunder Bolt, Meteor Shower,
Napalm, and in §5 Thunder Storm.

### 4.1 Chain Lightning (`cast_magick_direct`, instant)

- **The bolt.** A spell bolt ([`SPELLS.md`](SPELLS.md) §5.5) from the staff along the facing:
  - range 15;
  - Lightning, 850, Damage;
  - it chains as a spell's bolt does.
- **A wet caster** may be struck by its own bolt; a dry one is never.
- **Blocking.** A blocking target takes plain Lightning into its weapon.
- **Sound and shake.** `magick_thunderbolt`; the camera shakes 1.5 for 0.333 s.

### 4.2 Thunder Bolt (`cast_magick_direct`, sky)

- **The target.** Lightning falls from the sky within 8 (on the flat) of a point 8 ahead of the
  caster. It strikes the thing whose top is highest:
  - something damageable;
  - not the caster, a missile, an ethereal character, or anything behind a shield.
  - A character's top is the top of its capsule.
- **Under a shield.** A caster under a shield strikes its shield instead.
- **The blow.** Lightning, 5000, Damage and Knockback.
- **No target.** With nothing to strike, the bolt lands at a random spot within 4 of that point on
  each axis (a square, as the original). The original keeps that spot at height 0; ours keeps
  the caster's height, so the bolt and its scorch are drawn on the floor.
- **Heights.** The point and the blows' origin are at height 0, as the original.
- **Presentation:**
  - the bolt from 40 above;
  - `magick_thunderbolt` (effect and sound);
  - a `scorched` decal (size 4) on the floor below, but not on a shield;
  - the camera shakes 1.5 for 0.333 s;
  - a screen flash (renderer).

### 4.3 Crash to Desktop (`cast_magick_sweep`)

- **The victim.** One character at random within 20 (on the flat; shields hide), the caster
  included. It vanishes on the spot: dead, with no corpse, its spell, auras, buffs and self-shield
  gone. An undying character does not rise from it.
- **Spared:** characters only a script may kill, and bosses (over 100 000 hit points).
- **Failure.** With nobody to take, it fails.
- **Effect and sound.** `magick_ctd` (sound in the Additional bank).

### 4.4 Conflagration (`cast_magick_sweep`)

- **The wall.** It starts 3 ahead of where it was cast and sweeps on by `20·t²` over its 1 s.
  It is a box along its way: 11 across, 2 high, 2 deep.
- **What it burns.** Each damageable thing it touches, once: Fire 600 Damage, then Fire 75 Status
  with magnitude 2, as two blows. The test is made once a frame, as the original.
  - Spared: the caster, ethereal characters, and anything with a shield on the line from where
    the wall was cast.
  - The blows come from where the wall started.
- **Effect and sound.** `magick_conflagration`; the heat haze comes with the renderer.

### 4.5 Meteor Shower (`cast_magick_global`, sky)

- **Lifetime.** 17.5 s. Only one shower runs at a time: casting it again only starts its time over.
  It ends when its caster leaves the level (a revival makes a new wizard), so a new cast starts
  a new shower.
- **Meteors.** Every 0.4–0.8 s, one or two meteors:
  - each a spell boulder of Earth 5 and Fire 5, blasting with radius 4 ([`SPELLS.md`](SPELLS.md)
    §5.3);
  - from 146 above a point within 20 of the caster (uniform over the disc);
  - falling at `(0.01, −20, −0.01)`.
- **Presentation.** `magick_meteor_rumble` while it lasts, `magick_meteor_preblast` for each
  meteor; the scene's light dims to 0.4 (renderer).

### 4.6 Napalm (`cast_magick_sweep`, instant, sky)

- **The marker.** A smoke marker (`magick_smoke_target`) 15 ahead.
- **The wait.** After 5 s a plane passes over the marker for 2 s:
  - along the line across the cast;
  - `magick_plane_passby`;
  - the camera shakes 0.5 for 2 s.
- **Recasting.** Only one strike at a time:
  - while waiting, a new cast moves the marker (the line keeps its way);
  - while the plane passes, a cast fails.
- **Bombs.** Six bombs fall along the line, as the plane gets past 0.705, 0.72, … of its run. One
  bomb falls per frame at most.
  - Each lands 0.25 of the run behind the plane, at the moment it falls.
  - At an even frame rate that is about 10.8, 7.2, 3.6, 0, −3.6 and −7.2 from the marker along the
    line. As the original, a slow frame moves it a little.
  - A bomb needs a floor within 10 above or below the line.
  - It blasts everything damageable within 5 that no shield hides, but the ethereal. The caster
    is not spared.
  - The blast: Fire 2000 Damage, Earth 500 Knockback (magnitude 2), Fire 300 Status
    (magnitude 3).
  - Presentation: `magick_napalm` (effect, and sound in the Additional bank); the camera shakes
    0.25 for 0.5 s.
- **Later:**
  - The burning grease each bomb leaves (Grease, §5).
  - Melting frozen water (liquids).
  - The plane's model and the bomb's light.

### 4.7 Performance Enchantment (`cast_magick_self`)

- **Duration.** 10 s.
- **The boost.** Two self-cast auras on the caster for 10 s:
  - all damage it takes is halved (a resistance of ×0.5 against every element);
  - its spells of any element deal `amount × 2 + 2`.
- **Speed.** While more than 4 s are left, ×`(2 − 0.5^(t − 4))`.
- **The withdrawal.** In the last 4 s:
  - the speed is ×`(1 − 0.75^t)`, falling to nothing;
  - the withdrawal sound plays once.
  - The original also means to swap the auras for doubled damage taken and quartered spells. It
    swaps a copy, so nothing changes; ours keeps that.
- **The end.** The caster's self-cast auras end (a ward's too).
- **Recasting** while it lasts starts the 10 s over and the boost again, but the auras keep their
  first 10 s.
- **Sounds and effect.** `woot_magick_enchantment_buff` on the cast, `…_debuff` at the
  withdrawal (Additional bank).
  - `special_woot_performance` follows the caster until the withdrawal; a recast starts it anew.
  - As the original, the enchantment runs on through its caster's death.

### 4.8 Amalgameddon (`cast_magick_global`)

- **The pair.** The first two living characters of one kind (one template) within 20, the caster
  aside.
  Without such a pair, or while another runs, it fails.
- **Rising.** Their gravity goes. Each frame they rise 0.015 and are stunned for 10 s, until the
  first is higher than 5 (world height).
- **Apart.** They are pushed apart 0.25 a frame each for 0.2 s.
- **Together.** Then they are drawn together 0.6 a frame each, until they touch (their centres
  nearer than their radii summed).
- **The smash.** The second is overkilled. The first gains the second's hit points as they were
  when it got to 5, is unstunned, and has its gravity back.
- **The end.** After 10 s it ends whatever happened, and gravity comes back (not to one still
  levitating).
  - As the original, a timeout leaves the pair stunned for what is left of their 10 s.
  - Ours also ends it when either leaves the level, and holds their gravity off while it runs, even
    if a levitation ends.
- **Frames.** The moves are per frame, as the original.

### 4.9 Judgement Spray (`cast_magick_global`)

- **The volley.** For 1 s, one missile every 0.05 s (20), from the staff.
  - The missiles go left and right in turn, flat and square to the facing. As the original, the way
    is worked out from the facing tilted up 45°, so its speed is that of 22.5–37.5 times sin 45°,
    about 15.9–26.5.
  - They home (0.55) on one target, with danger 30. They do not face their way and never hit
    their caster.
- **The target.** A character, not the caster, within 20 and in front: the way from it to the
  caster lies between π/2 and 5π/4 of the facing.
  - The one picked has its distance nearest 12. As the original, the best so far is kept signed,
    so one farther than 12 sets a bar the nearer ones cannot beat.
- **A missile ends** on a hit, on a collision, after 2 s, or when it takes 20 of Fire, Lightning
  or Arcane. However it ends:
  - `magick_judgementspray_hit` and `wep_handgrenade_explode` (Additional bank);
  - what it struck takes Cold 273 Damage, then Cold 100 Status. As the original's damage
    events, only a hit has something to strike.
- **Looks.** Trails `magick_judgementspray_trail` and `_missile`.
- **Sound.** `woot_magick_judgment_fire` on the cast.

### 4.10 Wave (`cast_magick_sweep`)

- **The pillars.** Rock pillars rise one by one in a line along the facing:
  - each 0.1 s after the last;
  - 1.5 apart, starting 3 past the floor in front of the caster, six in all.
- **No floor.** With no floor found in front (looked for from 2 above, 4 down), nothing rises,
  but the cast counts.
- **A pillar.** A solid ball of radius 1.25, 0.05 over its ground. It lasts its rising clip,
  0.833 s, and nothing breaks it.
  - Like a barrier's link, it breaks other casts' barriers and sets off mines on its spot, and a
    shield in the way ends the line.
- **The toss.** A living character standing on the ground that touches a pillar is struck, once
  for the whole wave (an ethereal one is spared altogether):
  - Earth, Knockback and Damage of nothing;
  - unless levitating, immortal, ethereal or of mass 3000 or more, it is tossed up at 10.
  - The touch reaches as a solid barrier's: R around its centre, 2R around its foot.

## 5. Field magicks

### 5.1 Rain (`cast_magick_global`, sky)

- **Duration.** 17.5 s. One rain at a time: casting it again starts its time over, and the new
  caster takes it over (its storm then follows the new caster).
- **Soaking.** Every 0.25 s, everything damageable in the level takes a Water status at
  magnitude 0.25. Spared: missiles, anything under a shield, and characters immune to Water.
- **Indoors** it does nothing, silently.
- **Presentation.** `magick_rain` (ambience and effect, around the camera); the scene's light
  dims to 0.333 (renderer).

### 5.2 Blizzard (`cast_magick_global`, sky)

- **Duration.** As Rain, with Cold for 10 s.
- **The fade.** It goes on for its 1 s fade, still chilling. Casting it during the fade starts it
  over.
- **Liquids.** The original also freezes the level's water every 0.25 s; that comes with liquids.
- **Sound.** `magick_blizzard`; the fog comes with the renderer.

### 5.3 Thunder Storm (`cast_magick_global`, sky)

- **The rain.** A Rain, and it lives as long as the rain.
- **Bolts.** From 2 s on, every 0.5–1 s, one or two bolts:
  - each near a random spot within 20 of the caster (uniform over the disc);
  - the tallest damageable thing within 5 (on the flat; shields hide; not missiles or the ethereal)
    is struck, the caster too;
  - the blow is Thunder Bolt's, from 20 above the target;
  - with nothing there, the bolt lands on the spot.
  - The spot is first moved to the nav mesh's nearest point, whatever the triangle asks (any
    movement).
- **Presentation** as Thunder Bolt's, the camera shaking 1.2; `magick_thunderstorm` while it lasts.

### 5.4 Time Warp (`cast_magick_self`)

- **Duration.** 15 s of real time. Only one at a time; a second cast does nothing, silently.
- **The slowdown.** The whole level's time is multiplied by m, which starts at 1. However the
  warp goes (a level cleared too), m comes back to 1.
  - m closes its gap to 0.5 at a rate of 1 per real second.
  - Over the last second it moves back toward 1, and is 1 again at the end.
  - The caster is slowed too: in the original only its after-images differ.
- **Sound.** `magick_timewarp`; the colours drain (renderer).

### 5.5 Tractor Pull (`cast_area_ground`)

- **What is pulled.** Everything within 15 is pulled to the caster. Shields hide; the caster and
  ethereal characters are spared. A character held in a grip is let go first (comes with grips).
- **The strength** is `2.5 × √(d/15)`, at distance d.
  - Damageable things (not missiles) take an Earth pull of their own mass, from the point as far
    beyond them as they are from the caster.
  - Others (missiles, items) are thrown toward the caster: elevation 10°·(1 + π/8), power their
    mass. With a shield in range our query leaves items out, so they are not thrown then.
- **Presentation.** The camera shakes 1 for 0.5 s; `spell_push_area`; a radial blur (renderer).

### 5.6 Propp's Magick (`cast_spell1`)

- **The ball.** A ball of plasma (radius 0.6, danger 30) from 0.5 above the staff:
  - lobbed up at 18;
  - ahead at `√(10..28)`, within ±π/4 of the facing;
  - spinning (its looks; the body is not set spinning).
- **Blasts.** On a collision it blasts once: radius 4, Arcane 6000, Fire 2000, Earth 200 Knockback
  (magnitude 3). As the original:
  - a hit blasts twice;
  - after 8 s it just goes;
  - the cast reports no success (no fail sound).
- **Presentation.** `magick_propp_explosion`, `magick_propps_plasma_explosion` and `_loop`
  (Additional bank).

### 5.7 Grease (`cast_magick_sweep`)

- **The sweep.** For 0.5 s a sweep of grease turns from 1.4·π/4 to one side to π/4 to the other.
  - It is 6 long, cut by shields and walls.
  - Characters it touches (within 10°, 5 high), the caster aside, get a grease status with no
    damage per second. As the original, such a status does not take, so the sweep greases nobody
    (unless the character's Water modifier is not 0).
- **Fields.** As it passes each sixth of its time, a grease field lands on the floor 1.5 short of
  the sweep's end (looked for from 2 above, 4 down): six at most.
- **A field** (radius 1.5) lasts 60 s, three times faster while burning. A level holds at most 72
  (the original's pool); a new one ends the oldest.
  - Characters on the ground on it slide, for the step after each frame they stand on it.
  - Fire warms it by the blow's magnitude: at 1 it burns. Water or cold cool it: at 0 it goes out.
  - Burning, it warms by 0.5/s, and sets fire to everything damageable within 1.5: Fire, 240
    Status. All fields share one memory of whom they set alight, 0.25 s each.
  - Napalm's bombs leave a field set alight (heat 3).
- **Presentation.** `magick_grease_spray`, `magick_grease`; the `grease` decal and the
  `grease_burning` effect.

### 5.8 Tornado (`cast_magick_direct`)

- **The whirlwind.** A column of radius 2.5 and length 6, at the nav mesh's nearest point to 4
  ahead of the caster. It lasts 15 s, and a kill ends it within a second.
- **Wandering.**
  - It picks a point 5–15 away within ±π/4 of its way, at its own height, and a path to it
    (ENTITIES_AI.md §11). It heads for the path's corners; with none left it picks again.
  - As the original, it takes the corners two at a time: when within 1 of one, that one and the
    next go, and it heads for the next only for a frame before turning to the one after. It so
    cuts the corners in between (it is no physics body, and goes through walls).
  - Likewise a new path's first corner (or, with no full path, the point itself) is aimed at for
    one frame only.
  - Its speed is `|4·sin t|`, with t the time left. Its way is normalised with its rise, so it is
    slower going up or down.
  - It eases its height toward the floor within 3 below, at a rate of 1 per second.
  - Without a nav mesh it goes straight, and picks again when a wall stands in the way (four
    tries, else it turns back).
- **A push** sends it along a path to 10 along the push's way (on the flat; the original's way
  rises with the push). Without a nav mesh, it stops short of a wall.
- **The blow.** Each damageable thing it touches (not missiles; the caster too), at most once a
  second: Earth 800 Knockback, magnitude 2, from the point of its surface facing the thing.
- **Presentation.** `magick_tornado_rumble`, `magick_tornado`, `magick_tornado_hit` at each blow.

### 5.9 Vortex (`cast_magick_global`)

- **The hole.** It opens 10 ahead of the caster with a mass m of 0.25.
  - m eases toward a target: `m ← target + (m − target)·0.1^dt`.
  - The target wanes by 0.05 a second; the hole is gone when m is.
- **The pull.** It pulls everything within 10·m (shields hide), toward its centre, with a strength
  of `15·(1 − d/(10·m))`. Spared: other holes, the dead, ethereal characters, portals.
  - A character gets it as an extra force on its body for the step: the share already along its
    motion is taken off.
  - Anything else gains it as velocity each frame.
- **Swallowing.** What touches its core (radius 0.5) is swallowed:
  - a character without gibs vanishes (as Crash to Desktop); one with gibs is overkilled;
  - a missile ends, an item goes, a barrier, a shield or a grease field ends, a mine goes off.
- **Growth.** Each thing swallowed raises the target, by the thing's mass m′:
  `target ← 1 − (1 − target)·0.75^m′`. A wizard of mass 75 brings it to nearly 1.
  - The original counts a thing when it is dead right after; ours counts each thing once, so one
    that dies over its death clip counts too.
- **Notes.** As the original:
  - the pull on missiles and items is added once a frame, not scaled by time;
  - a character's extra force is the last one set in the frame, so two pulls do not add up.
  - With a shield in range our query leaves items out (see Tractor Pull).
- **Sound and effect.** `magick_vortex`.

## 6. Mind and spirit magicks

A character's **factions** are its template's, unless confusion took them for a while. Charms,
fears and friendship between characters go by these.

### 6.1 Fear (`cast_magick_self`)

- **Who flees.** Every character within 6 (shields hide), the caster aside, that shares none of
  the caster's factions. Spared: one in a bubble (armour does not spare), demons, the undead, and
  the fearless.
- **Fear** lasts 5 s from each scare; the feared flee the one who scared them. Death, or Nullify,
  ends it.
- **The panic state** comes in the third common check, after the jump, unless entangled:
  - It plays `move_panic`, can move and turn, and stops being invisible.
  - Its wander heading drifts by up to ±30 radians a second.
  - Feared, it runs at 0.8 speed, away from what it fears. If it was going somewhere, it takes the
    halfway way between the two.
  - It leaves for death, for vines, or (an avatar with a cast type) for the panic cast state. When
    the fear is over it goes idle.
  - Where it was going when the fear took over is kept for the blend and given back when it
    leaves (the original's AI and input set it again each frame).
- **The panic cast state** casts at once, then flees along away plus the wander heading.
  - It ends (back to panic) when the spell is over, when none runs, or for a weapon cast.
  - A running spell stops on leaving.
  - Death and vines take it out first.
- **Presentation.** `magick_fear` (effect and sound); `magick_feared` on the feared (renderer).
- **Later:** panic and stumbling from other causes.

### 6.2 Charm (`cast_magick_sweep`)

- **The target.** Of the characters within 14 (shields hide), the caster aside, not yet charmed
  and sharing none of its factions: the one whose way is nearest the caster's facing.
- **The charm.** It is charmed by the caster for 15 s; the uncharmable are not. A charmed NPC is
  Friendly while it lasts, and its mind starts over (ENTITIES_AI.md §7.10).
- **Failure.** With no target, it fails.
- **Sound.** `magick_charm`.

### 6.3 Confuse (`cast_magick_direct`)

- **The target.** The character (any faction, the caster aside) within 14 whose way is nearest the
  facing; shields hide. With none, it fails.
- **The confusion.** For 15 s; a new confusion starts the time over.
  - An NPC belongs to no faction while it lasts, and gets its template's back after.
  - An avatar's controls are turned around (input, OM-31).
  - It ends early when the character's hit points are gone.
- **Presentation.** `boss_fafnir_confuse` (Characters bank); `confused` on the target.

### 6.4 Nullify (`cast_magick_self`)

- **The running magicks end:** weather, meteor showers, napalm strikes, time warps (the time comes
  back), revivals on their way, conflagrations, judgement volleys, amalgamations, grease sweeps,
  raisings of the dead, phoenixes on their way down, and Death (the time comes back).
- **What goes:** whirlwinds, vortices, missiles, barriers, mines, grease fields, shields and both
  portals.
- **Every character:**
  - loses its statuses;
  - loses its vines, self-shield, fear, charm, confusion, invisibility, running spell, combined
    spell still to go (a charge, a clip before its action) and queue;
  - loses its levitation, haste, enchantment and auras.
  - A summoned character is killed.
  - The original spares the auras of a cross's bubble, which comes with that ability.
- **Glow.** `magick_nullified` plays on the barriers, mines and fields that went, and on each
  character that had any of its vines, self-shield, fear, charm, confusion, invisibility, spell
  or queue.
- **Sound.** `magick_nullify`; a screen flash (renderer).

### 6.5 Corporealize (`cast_magick_self`)

- **Effect.** Every character within 30 stops being ethereal. Ethereal bosses (Grimnir, Death)
  follow with the bosses.
- **Sound.** `magick_corporealize`; a screen flash (renderer).

## 7. Summons and the portal

- **Summoned characters.** A summon takes its master's factions (and gets them back after a
  confusion). Its mind is ordered to attack, reacting to attacks and nearness (ENTITIES_AI.md §7.10).
- **The bestiary.** A level finds the templates it makes at run time by asset name, with their
  clips. The engine's comes from the game files (attached to a level with level play); a missing
  or broken asset makes nothing.
- **Spots.** Each spot below is first moved to the nav mesh's nearest point (ENTITIES_AI.md
  §11.2); without a nav mesh the point is used as it is.

### 7.1 Raise Dead (`cast_magick_direct`)

- **The spot.** 4 ahead of the caster, on the nav mesh, then on the ground (a probe 3 down).
- **The rising** lasts 8.1 s. One zombie (`data/characters/zombie`) rises at once and one every
  2 s after, five in all. Each stands at a random spot within ±1.5 of the centre (a probe 10
  down), faces a random way, plays `spawn` (only the clip: it does not go through the rising
  state), and is summoned by the caster.
- **A full level.** The cast still counts; nothing rises.
- **Presentation.** `magick_summonundead_ground` at the spot, four times its size, for as long as
  the rising lasts (renderer); `magick_raise_dead`.

### 7.2 Summon Elemental (`cast_magick_direct`)

- **The egg** lies 3 ahead, on the ground (from 1 above, 10 down), with 1 hit point.
- **Memory.** For each element of a blow among the status elements, Lightning and Arcane, it
  keeps the blow's amount × magnitude; a later blow of the element replaces it. Other blows do
  nothing to it. Ours has no body yet, so only blows that reach what is near (areas, beams,
  sprays) strike it, not missiles.
- **Hatching.** Each update it picks the element with the largest kept value, and it is done as
  soon as there is one. As it goes, the elemental of that element
  (`data/characters/elemental_<element>`) rises where it lay, summoned by the caster, with
  `special0` as its rising clip.
- **Killed outright** it would hatch a Shield elemental. But its next update picks again from its
  blows, and a Shield egg removed first hatches nothing. So, as the original, it never does.
- **A level's eggs** may have a proximity. Once a second, a wizard within it hatches the egg into
  one of Arcane, Cold, Poison, Fire, Life, Water, Steam or Lightning, at random.
- **Presentation.** `magick_summonelemental` at the egg; `magick_summon_elemental`.

### 7.3 Summon Phoenix (`cast_magick_direct`)

- **Indoors** it only cries (`magick_summon_phoenix_indoors`). The original's camera shake there
  is never reached.
- **One at a time.** A cast while a phoenix is on its way does nothing, and counts as cast.
- **The descent.** The phoenix comes down at a random spot within ±2 of the caster, on the
  ground. When its clip ends (2.083 s):
  - fire bursts within 6 of the spot (0.5 up): Fire damage 400, a Fire status of 200 at
    magnitude 5, and a Fire knock-down of 400 at magnitude 2;
  - each player a revival may raise (§3.5) rises around the phoenix with all its hit points, a
    quarter turn apart by player number.
- **Presentation.** `magick_summon_phoenix`; at the burst `magick_phoenix_blast`,
  `spell_fire_area` and a camera shake of 1 for 1 s.

### 7.4 Summon Death (`cast_magick_direct`)

- **One at a time;** a second cast fails.
- **The victim.** Of the living characters within 40, undead aside, the one with the smallest
  share of its hit points (more than none). The scan starts at a random place and a later equal
  one wins; the caster may be it. With none, it is the caster.
  - The original searches around its view centre; we search around the caster.
- **The coming.** Death appears 7 from the victim at a random angle, on the ground (from 1 above,
  6 down), 1.3 up. It hovers toward 1.2 above the floor (with no floor, it keeps its height).
- **Time.** While it is about, the world's time eases toward a quarter: each update it moves by
  its distance times dt over the aim. Death's own time is real time, worked out (as the
  original) with the modifier it has just set.
- **The chase.** Ethereal, after 2 s it glides at 8 straight to the victim, turning toward it.
- **The swing.** Within `1.2·(0.396 + r²)` of the victim (squared, flat; r its radius):
  - it stops, becomes solid, and raises its scythe (0.833 s);
  - then it brings it down (1.667 s). From 10% to 80% of the fall, the blade takes all the hit
    points (as Arcane, plain) of every character it cuts, each once in 1.5 s; the caster too.
  - The blade follows the scythe's bones in the original. Until those come with the renderer, it
    cuts whatever stands within 2 (plus its radius) in front of Death.
- **Going.** After the fall, or when struck by a damaging Life blow while solid, it vanishes
  (1.042 s) and time eases back. When gone, time is whole again.
- **Its victim dead** (or gone), it goes at once, in any phase, as the original. So after a kill
  it does not vanish: time is whole again at once.
- **Presentation.** `magick_summon_death`, and `magick_timewarp` while it is about;
  `death_ethereal_spawn` and `boss_death_proximity` (Characters bank) as it comes;
  `wep_death_scythe_swing` and `wep_death_scythe` (Weapons bank); `boss_death_death` and
  `death_ethereal_despawn` as it vanishes; the screen at half saturation while it is about
  (renderer).

### 7.5 Portal (`cast_area_fireworks`)

- **The gates.** A level has two, orange and blue. Each cast opens a gate at the nav mesh's
  nearest point (water and moving parts allowed) to 2 ahead of the caster, 1.5 above the floor
  under it (a probe from 2 above, 4 down; with no floor, at the point itself). With two gates up,
  the older one moves there and keeps its colour. The first gate is orange.
- **Failure.** With the nav mesh more than 5 away, it fails. Without a nav mesh, it needs the
  floor under the spot.
- **Connection.** The gates are connected while both are up and within 30 of each other.
- **Going through.** While connected, a character or missile that touches a gate (an upright
  capsule, radius 0.5, 2 tall) comes out of the other one. Its offset from the gate it touched,
  times 0.8, is kept. Barriers, shields and grease fields never go through. Of our other
  entities, none has a body to move yet.
- **Ignore memory.** Each touch keeps the entity out of both gates for 0.1 s, so what comes out
  stays until it steps off.
- **Presentation.** `magick_teleportb` as a gate opens; `magick_teleporta` for each passage;
  `magick_portal_orange` / `magick_portal_blue` on each gate while connected (renderer). The
  pair's `spell_shield` hum, louder the nearer they are, comes with the audio.
