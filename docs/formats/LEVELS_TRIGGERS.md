# Magicka levels, scenes and triggers (format and semantics)

> Clean-room document for OpenMagicka. It describes the **data formats** (`.lvl`, scene XML, dialogs,
> cutscenes) and the **observed behavior** of the original scripting system (Magicka 1.10.4.2, XNA 3.1).
> No decompiled code is copied here. Only schemas, tables and prose descriptions.
> Usage counts were computed by a script over all XML in `Content/Levels` of the Steam version
> (142 scenes, 48 `.lvl`, 18 dialog files).

Related documents: [`../ENGINE_ANALYSIS.md`](../ENGINE_ANALYSIS.md) (general engine overview).

## 1. Overview

```
Level (.lvl)                      — chapter / challenge / versus map
 ├─ DialogCollection              — Dialog_*.xml (+ always Dialog_Fairy.xml)
 ├─ counters (int) and timers (float) — SHARED by all scenes of the level
 └─ GameScene ×N (scene XML)      — one "room", id = file name
     ├─ LevelModel (.xnb)         — geometry + locators, trigger areas, animated parts,
     │                              lights, effects, physics objects, navmesh
     ├─ visual settings           — fog/bloom/clouds/skymap/reverb…
     ├─ Ruleset (optional)        — Survival / TimedObjective / Versus (+ Wave for Survival)
     ├─ Trigger ×M                — <trigger><if>…</if><then>…</then></trigger>
     └─ Interactable ×K           — <OnInteract locator=…>actions</OnInteract>
```

The script is a **declarative set of triggers**. Every frame, each trigger with `autorun=true`
checks its conditions (`<if>`). If they hold, it queues its actions (`<then>`).
Triggers without autorun are called by the `executeTrigger` action, from AI events (`trigger=` on move
etc.), from `onDeath`/`onDamage`/`onPickup`/`onSkip`, and so on.

## 2. Identifiers, hashes and case

### 2.1 String hash

Almost all by-name references (scenes, locators, areas, entities, triggers, counters, timers,
dialogs, effects, sounds, templates) are turned into an `int` inside the engine by a
"custom hash" function. This is the **classic 32-bit string hash of .NET Framework 2.0–3.5 (x86)**:
two accumulators with initial value 352654597 (= 5381·2¹⁶+5381); the UTF-16 string
is read in blocks of 2 characters as a little-endian int32; on each step
`acc = ((acc << 5) + acc + (acc >> 27)) ^ block` (arithmetic right shift); blocks
alternate between the first and second accumulator; the result is `acc1 + acc2 · 1566083941`
(all modulo 2³²). For an odd length, the last block holds the string's terminating NUL character
as its upper half; the loop stops when ≤ 2 characters remain.

For OpenMagicka, exact reproduction of the hash is needed **only** for compatibility with the original's binary
checkpoint saves and network protocol. For our own implementation it is simpler
to use normalized (lower-case) strings as keys.

### 2.2 Case rules

| Where | Rule |
|---|---|
| Element names (`Scene`, `trigger`, `If`, `Then`, `Random`, `Looped`, `OnInteract`, all visual tags) | case-insensitive (Ordinal IgnoreCase) |
| Action/condition tag | case-insensitive comparison with the **class name** (`spawn`, `Spawn`, `SPAWN`, `executetrigger`…). The game uses all variants: `spawnEffect` in 4 spellings, `IDPresent` in 4 |
| Action/condition attribute names | case-insensitive, matched to the class's public writable properties |
| String values of actions/conditions | **converted to lower case** on read, then hashed |
| Enum values | case-insensitive; numeric form is allowed (`roomType="10"`), as is a comma-separated list of flags (`factions="evil,friendly"`, `reactto="proximity,attack"`) |
| bool | `true`/`false` in any case (.NET `bool.Parse`), otherwise an exception |
| float | invariant culture (dot); `.1`, `-12` are allowed |
| Scene names | scene id = scene file name without extension, in lower case |
| Trigger areas in LevelModel | names are lowered before hashing |
| Locators and lights in LevelModel | names are hashed **as is** (no lower) → references from XML (always lower) find only locators whose names are lower case |
| `area=` on `<Start>` in `.lvl` and `spawnPoint=` | the player index `0..3` is appended to the value, then it is hashed **without** lower |
| Exceptions | the `color` attribute of `<SkyMap>` is compared case-sensitively |

### 2.3 Namespaces

- **Locator** (`Locator`: matrix + radius) and **trigger area** (`TriggerArea`: oriented box)
  come from the scene's `LevelModel`. Locators are looked up first in the model, then in the
  AnimatedLevelPart hierarchy (with their current transform applied). When an action needs a "point" by name
  (`area=` on spawn/spawnPhys/setPosition…), a locator is searched first, then a trigger area.
  In that case a **random point inside the box** with a random Y rotation is used.
- Area `any` is built in: "the whole level" (all EntityManager entities).
  - Ours fills an area once a frame, when first asked after the frame begins: an entity is in when the box comes within its radius of its centre (the
    original's own reckoning when it fills an area by hand; its physics fills it by contact). The dead are present but not counted
    (by type, by faction or in all), unless undying; the factions counted are the template's.
  - Locators of animated parts come with the parts (OM-47). In
  Survival/Versus rules, `any` as a spawn point means a random area from the ruleset's list.
- **Entities** with a unique `id` (the `id` attribute on spawn/spawnPhys/spawnItem…) are looked up globally
  (`Entity.GetByID`). Avatars are addressed via `player1`..`player4` where this is explicitly
  supported (setPosition, setFaction), or via their id.
- **Character type** (`type=`) is a template name from `Data/Characters/<type>`; the name hash = Type ID.
- **Triggers** and **OnInteract** live in one scene dictionary (`OnInteract` is keyed by the hash of `locator`).
- **Counters and timers** are level (`Level`) dictionaries; they survive scene changes.
- **Minds.** A mind's reaction trigger and its scripted events' triggers run the scene's trigger with conditions, the mind's
  character as the sender. The scene's areas and locators are the minds' zones and locators.

## 3. Level file `.lvl`

XML, root `<Level>`. The list of campaign chapters is **hardcoded** (order Wizard_Castle,
Mountaindale, Highlands, Havindr, Battlefield, EndOfWorld, Wizard_Castle2, Mines, Swamp,
Niflheim, Ruins, EndOfWorld2; Mythos: Tsar_WizardCastle, Tsar_Mountaindale, Tsar_Rlyeh;
Dungeons: `Challenges/ch_dungeons_ch1/ch2`). Challenges and versus are all `*.lvl` in
`Levels/Challenges/` and `Levels/Versus/` (`ch_vietnam`, `ch_osotc`, `ch_dungeons_ch1/2` are
"story challenges"). Localized level names are also partly hardcoded
(a file name → loc key table).

| Element | Used (of 48) | Content / attributes | Semantics |
|---|---|---|---|
| `Name` | 48 | text, loc key (`#Chapter_04`) | required; level name |
| `Description` | 48 | loc key | required |
| `ShortDescription` | 2 | loc key | defaults to Description |
| `Loading` | 48 | path to the loading image | required |
| `Start` | 48 | `scene` (scene name), `area` (locator prefix `area0..3`), `spawnPlayers` (bool, default true), `noItems` (bool — give everyone `weapon_unarmed`), `forceCamera` (bool; default true in Campaign/Mythos), `spawnFairy` (bool, default true) | start point; ignored when loading from a save |
| `Dialogs` | 26 | path to Dialog XML relative to `.lvl` (`../Dialog_Tutorial.xml` is allowed) | without it — only Dialog_Fairy |
| `Scenes` / `Scene` | 48 / 141 | path to scene XML relative to `.lvl` | required ≥1; other children of `Scenes` → error |
| `PreferredAvatar` | 4 | avatar name (default `wizard`) | character selection menu |
| `AllowedAvatars` / `Avatar` | 2 / 8 | list of names | restricts selection |
| `AdditionalItems` / `Item` | 3 / 3 | `item` (item asset), `bone` | extra items attached to avatars |

Notes:
- The game's reader needs a top-level `<Level>` element (names compared ignoring case). Element
  and attribute names are compared ignoring case. An element given twice: the last value wins
  (for `Start` attribute by attribute), except `Scenes`, `AllowedAvatars` and `AdditionalItems`,
  whose entries accumulate. Inside `Scenes` every node except comments must be a `Scene`
  element, or loading fails. Avatar names are lowered. An empty `Scene`, `PreferredAvatar`,
  `Avatar` or `Dialogs` makes the original fail (whitespace-only text counts as empty).
- Paths (`Scene`, `Dialogs`, and in scenes `Model`, `SkyMap`, cloud `texture`) are combined with
  the file's directory. Trailing spaces and dots are dropped, as Windows does when the original
  opens the file: `Tsar_Mountaindale.lvl` has
  `<Scene>Tsar_Mountaindale\tmd_s1.xml  </Scene>`.
- `Start`: `scene` is lowered (it is hashed), `area` is not. An `AdditionalItems/Item` without
  both `item` and `bone` is dropped.
- The menu reads `.lvl` with a streaming parser (only Name/Description/ShortDescription/Loading/
  Scenes/PreferredAvatar/AllowedAvatars, plus `<model>` and `<ruleset type>` of each scene).
  On launch the level is read in full (DOM), with a different set of elements (Start, Dialogs,
  AdditionalItems). Both parsers silently ignore unknown elements. The menu fails when `Name`,
  `Description` or `Loading` is missing or empty, or there is no scene. Its scan of `<Scenes>`
  runs to the end of the file, so `Name`, `Description` and `Loading` count only before the
  first `<Scenes>` (in every shipped level `Scenes` is the last element).
- Content integrity: the SHA-256 of the `.lvl`, all scene XML and their `.xnb` models are XORed into a "combined
  hash" (DLC licensing/anti-cheat + level matching over the network). Not needed for OpenMagicka.
- The chapter ruleset is taken from the first scene with a non-null `<ruleset type>`; in the menu `versus`
  is treated as "none".
- Scene `Havindr/havindr_s5HBF.xml` is not referenced by any `.lvl` (and contains an invalid
  attribute — see §12).

## 4. Scene XML

Root `<Scene>` (attributes `forceNavMesh` ×29, `forceCamera` ×12 — bool; other attributes are
ignored). Children are read in order; **an unknown top-level element or a text node →
exception** (comments are skipped). Scene name = file name without extension, lowered; the
scene ID is its hash. Triggers and OnInteract points share one table keyed by the hash of
their id/locator: two with the same key → exception (a trigger without id has key 0).
Repeated settings elements: the last value wins (attribute by attribute).

| Element | Used | Attributes / content | Semantics |
|---|---|---|---|
| `Model` | 142 | text: model name | `LevelModel` = `<scene dir>/<name>.xnb` (as written; the shipped names are lower case); required |
| `Trigger` | 2519 | see §5 | trigger |
| `OnInteract` | 286 | see §5.5 | interactable point |
| `Wave` | 316 | children are action blocks (§7.1); attribute `value` (×36) is ignored | Survival wave; allowed only **after** `<Ruleset type="survival">`, otherwise an "unknown element" exception. Waves belong to the ruleset object: a later `<Ruleset>` with a known type replaces it and drops the waves read so far |
| `Reverb` | 116 | `roomType` (enum RoomType, a number is allowed), `mix` (float) | reverb |
| `Indoor` | 103 | bool text | interior (affects weather/lighting) |
| `Bloom` | 90 | `threshold` (0.8), `multiplier` or the typo `multiplyer` (1; whichever comes last), `blur` (2.5) | post-effect |
| `Fog` | 49 | `start`, `end`, `color` = `r,g,b[,a]` (a defaults to 1; fewer than 3 numbers → exception, more than 4 are not read); all 0 when absent | fog; the element's presence enables it |
| `Clouds` | 33 | `intensity`, `scale` (`s` or `sx,sy`, other counts → exception), `movement` (exactly `x,y`), `texture` (path from scene), `worldRelative` (bool); all 0/none by default | cloud projection onto the directional light |
| `RimLight` | 0 | `glow` (0.8), `power` (1.5), `bias` (0.2) | rim lighting of characters |
| `Ruleset` | 32 | `type` = survival / timedobjective / versus (+ own attributes, §7) | mode rules |
| `KillPlanePosition` | 21 | float text (default −50) | horizontal plane: everything below it is destroyed |
| `SkyMap` | 19 | text: texture (path from scene); `color` = `r,g,b` (attribute name case-sensitive!; at least 3 numbers, further ones not read). Each `SkyMap` element resets the colour to 1,1,1 first | sky |
| `Filter` | 1 | `brightness`, `contrast`, `saturation` (all 1) | color correction |
| `Magnify` | 0 | float text | initial camera zoom |

## 5. Trigger model

### 5.1 The `<trigger>` element

| Attribute | Used | Type | Default | Semantics |
|---|---|---|---|---|
| `id` | 2518 | string → lower → hash | 0 | trigger key; the game has one trigger without an id |
| `autorun` | 1556 | bool | **true** | check conditions every frame |
| `repeat` | 2444 | float **or** bool | `false` | `false` = −1 (once); `true` = 0 (every frame while conditions hold); number N ≥ 0 = cooldown of N seconds. The number is read with NumberStyles.Number (thousands separators, no exponent); anything that is neither a number nor a bool is logged and means −1 |

Any other attribute on `<trigger>` → exception. Values in the game (on `<trigger>`): `repeat="false"` ×1643,
`"true"` ×616, number (0.5–32 s) ×185; `autorun="false"` ×1208, `"true"` ×348.

Children: `<if>` (0..n), `<then>` (1). Other children are silently ignored. If `<then>` is given
several times, the last one wins. Without `<then>` the trigger has no actions and the original
fails when the scene initializes its triggers; OpenMagicka rejects it when loading.

### 5.2 Conditions: `<if>`

- Each `<if>` is a **conjunction** (AND) of its child conditions.
- Several `<if>` form a **disjunction** (OR): the trigger fires if at least one block holds.
- No `<if>` or an empty `<if/>` → the condition counts as satisfied.
- Every condition has the base attribute `invert` (bool): the result is XORed with it
  (used 353 times). Exception: `bossDead`/`bossHealth` with no boss present
  return false **before** invert is applied.
- Comparisons: `compareMethod` ∈ `less | equal | greater` (= −1/0/1), meaning "value in the world
  *op* value in the attribute". Seen: equal ×1007, greater ×820, less ×260.

### 5.3 Actions: `<then>`

- Children of `<then>` are actions; the tag = class name (§9). Unknown attributes are **silently ignored**
  (the game has one such case: `spawn delayed=`).
- The base attribute of every action is `delay` (float, seconds; set 8426 times).
- `<random>` (×245): a group of alternatives. All `<random>` groups of one `<then>` form a
  set of variants. On firing, **one** group is chosen with equal probability, and all regular
  (non-random) actions of `<then>` are appended to it. Final order: first the
  actions of the chosen group, then the regular ones — regardless of position in the XML.
- `<looped>` (×18): its contents (actions and nested `<random>`) are included **only if
  the current campaign save is "looped"** (a replay playthrough); decided when the scene loads.
- Comments are allowed directly inside `<then>`, `<OnInteract>` and `<if>`, but **not** inside
  `<random>` or `<looped>` (there a comment is taken as an action tag → load error). A text node
  where an action or condition is expected is a load error too.
- Plain actions inside `<looped>` join the plain actions in document order, and its `<random>`
  groups join the others; a variant is a random group followed by all plain actions.

### 5.4 When and how it runs

Frame order for the current scene (`GameScene.Update`):
1. scene lights/sounds;
2. **all triggers** of the scene in ascending order of id hash (not file order!); for each one:
   decrease the repeat timer → if `autorun` and we are not a network client, try `Execute`
   → `Update` all its actions (process delay queues);
3. ruleset (`Update`, or `LocalUpdate` on the client);
4. level model (animated parts, water, etc.), effects;
5. clear the contents of all trigger areas; area `any` is recomputed right away from all entities.
   Other areas are refilled by physics callbacks before the next frame.

`Execute(sender, ignoreConditions)`:
- allowed if `ignoreConditions`, or the trigger has not fired yet, or
  (`repeat ≥ 0` and the repeat timer has expired);
- conditions are checked (OR over `<if>`, AND inside); on failure — return;
- `hasTriggered = true`, repeat timer = `repeat`;
- a variant (`random`) is chosen; on the server a network message with the variant index is broadcast;
- `OnTrigger(sender)` is called on each action of the variant.

Action queue: `OnTrigger` with an empty queue sets the countdown = `delay`; otherwise it increments
the pending run counter. On each `Update` the countdown decreases by dt; while it is ≤ 0 and there are
pending runs, `Execute` runs and `delay` is added to the countdown (repeated calls
are spaced by delay). So an action with `delay=0` runs **in the same frame** as the
firing. `clearDelayedActions` clears the queues.

`sender` (initiator): the character that caused the trigger (interaction, AI event, `onDeath`…).
It is used by `startDialog position="interactor"` (the default!) and the `interactorIs` condition.
`executeTrigger`/`startDialog` keep a FIFO of senders for each pending run. `clearDelayedActions` drops the pending runs but
not those senders, so later runs take older senders (as the original).

**Further, as the original** (ours: `OpenMagicka.Engine.Scripting`, `ScriptScene`):
- The plain actions are the **same objects** in every variant, and every variant's actions are updated each frame. With N random
  groups, a plain action's delay runs out N times faster (its runs come N times sooner).
- The conditions are never initialized: the original calls `Initialize` on each block's array, not on its conditions.
- A target with a lower key than the trigger that fires it has already updated this frame, so its actions run the next frame.
- The condition blocks short-circuit: within a block at the first that fails, across blocks at the first that holds.
- Missing targets: `executeTrigger`, `clearDelayedActions` with a name, and `enablePoi`/`disablePoi` for a key that is not there (or
  not a point) crash the original; ours does nothing.
- A trigger that sets itself off again with no delay hangs the original (its action's run loop never ends); ours runs only
  the runs waiting as the frame began, so such a trigger runs once a frame.
- An unknown tag in `<if>` or `<then>` crashes the original's loading; ours keeps a stand-in (never holds, does nothing) and lists
  it, while the kinds are being written (the actions: OM-45, OM-46; every condition is known).
- On all 141 scenes: 2,801 triggers and points, 1,296 areas; in their first 2 s, 736 firings (with all the conditions known).

`executeTrigger` calls the target **with condition checks** and the repeat rule: a one-shot trigger that
already fired will not run again. A typical "function" pattern in scripts:
`autorun="false" repeat="true"` with no `<if>`.

Network mode: autorun does not work on the client. The client receives "trigger execute" from the server
(scene id, trigger id, variant index, sender handle) and runs the same actions
locally. Actions that create entities do nothing on the client and wait for separate
spawn messages.

### 5.5 `<OnInteract>` (Interactable)

Attributes (strict — an unknown attribute → exception):

| Attribute | Used | Semantics |
|---|---|---|
| `locator` | 286 | name of the point locator (lower); interaction id = hash |
| `type` | 286 | InteractType: None, Pick_Up, Use, Next, Push, Pull, Boost, Examine, Talk, Interact, Activate, Attack, BreakFree — icon/label |
| `enabled` | 0 | bool, default true; toggled by `enablePoi`/`disablePoi` |
| `animHighlight` | 223 | comma-separated list of AnimatedLevelPart paths (`a/b/c`) to highlight; lowered, spaces removed |
| `physHighlight` | 6 | list of physics object ids to highlight; lowered, spaces removed |

Without `locator` the key is 0, like a trigger without id.

Children are actions (same syntax as inside `<then>`, including `random`/`looped`). There are no conditions;
autorun is off. It fires when the player interacts and the cursor ray/segment passes
within `Locator.Radius` of the locator point (the nearest one is chosen). Interactions of type
Talk are unavailable while "combat" music (threat) plays. Repeats are unlimited — blocking
is done via `disablePoi`.

### 5.6 Counters and timers

- Counter: `int`, default 0, created on first access. Action `counter`
  (INC/DEC/SET), condition `counter`. 1020 changes and 955 checks in the game.
- Timer: `float`, created by the `addTimer` action (re-creation is ignored),
  changed by the `timer` action (`value`, `paused`), checked by the `timer` condition. Two kinds:
  regular (always grow while the level updates) and "played" (`playedTimer=true`, do not grow
  in cutscenes, on pause and after game end). The `TimedObjective` ruleset timer is a regular timer.
- Both stores belong to the level and are saved in the checkpoint.
- The level's update (not paused, not over) runs the timers: the played ones out of cutscenes, the plain ones always; the scene
  updates after. Timers look in the played list first. A timer condition's or counter condition's "less" is the world's value below
  the attribute's.

### 5.7 AI events (children of `spawn`, `giveOrder`, `spawnGenericBoss`, `spawnCloneWizards`, `activateDispenser`, `avatarMove/PlayerN`)

The tag is the event type (case-insensitive): `move`, `animation`, `face`, `kill`, `loop`.
Common attributes: `delay`, `trigger` (trigger on event completion, sender = the character).

| Event | Attributes |
|---|---|
| `move` | `position` (`x,y,z` or a locator/area name — area → random point; a locator also sets the direction), `facingDirection` (bool or vector), `speed` (default 1) |
| `animation` | `animation`, `idleAnimation`, `blendTime` |
| `face` | `target`, `speed` |
| `kill` | `remove` |
| `loop` | `type` = Loop / Reverse (loop the event list) |

## 6. Scene changes and checkpoints

### 6.1 Transitions

`changeScene`/`enterScene` (and the level's `<Start>`) set a **SpawnPoint**: scene id + 4 locators
`<spawnPoint>0..3` + a `spawnPlayers` flag. Process:
1. start the render transition (`Fade`/`CrossFade`; `None` or time 0 → immediately);
2. at the end of the transition (in background loading): reset the boss fight and the generic bar; the current scene
   is "torn down" — effects, physics, sounds (paused with `saveNPCs`, otherwise stopped); the state of
   AnimatedLevelPart is stored in the scene; entities are stored in the scene with `saveNPCs`,
   otherwise removed; when changing to a different scene — its content is unloaded;
3. load the new scene's LevelModel and `Initialize`: initialize triggers (they are **not
   reset** — scene state lives until the end of the level), restore animations,
   ruleset, render parameters, first creation of the model's physics objects (only on the first
   entry), return of stored entities, reset of trigger repeat timers, registration of
   trigger areas;
4. with `spawnPlayers`: player *i* is placed at locator `<sp><i>`; if it does not exist — at the nearest
   lower one found, offset by 2 m along X for each gap; then snapped to the ground
   with a downward ray; camera — at the players' center; fairies;
5. run the "start actions" (replay, §6.2), the callback (for enterScene — start avatar
   movement and `instantTrigger`), exit the transition.

If there are > 300 start actions, a loading screen with progress is shown.

### 6.2 Checkpoint and replay

`triggerCheckpoint` takes a snapshot of the **level state**: current scene, timers, counters,
and for each scene: the first-entry flag, stored entities, global and 3D sounds,
animation states, the state of each trigger (repeat timer, hasTriggered, action
queues) and the scene's **log of executed actions**. Plus camera, dialogs (which are finished,
positions), music, unlocked magicks, player weapons. In Campaign/Mythos the snapshot
is written to the save (binary, with SHA-256 of the level and dialogs in the header).

Log: each executed action is appended to the scene's list (re-running the same
`spawn` moves it to the end; `spawnPhys`/`spawnAnimProp` additionally go to a separate
"world sync" list for the network). When a checkpoint is loaded, the log is replayed as
**QuickExecute** — an "instant" version of the action. The "Replay" column in §9:
`yes` — QuickExecute = normal execution; `spec.` — custom logic (spawn recreates
only surviving NPCs with their HP, cutscenes without bar animation, lights without transition, effects —
only continuous ones); `—` — nothing on replay (the effect is either already in the saved state —
counters, entities — or temporary: sound, camera, dialog, titles).

`<ignore trigger="id"/>` inside `triggerCheckpoint`: on load these triggers are reset to
their initial state (as if they never fired), and their actions are excluded from the replay. This
is how encounters that repeat after death are made.

## 7. Rulesets

### 7.1 Survival (`<Ruleset type="survival" waves="N">`, 17 scenes — challenge arenas)

- `waves` — number of waves (informational; the real one = number of `<Wave>`).
- Child `<spawnAreas>` with a list of `<area>name</area>` — spawn areas for `any`.
- `<Wave>` (scene top level, after `<Ruleset>`): **every** non-comment child
  is treated as a "WaveActions" block regardless of the tag name (in the game `waveActions` ×1727,
  `waveAction` ×3, and also `GiveOrder` ×20, `playAnimation`, `playMusic`, `executeTrigger`
  directly under `<Wave>` — their attributes other than `area`/`delay` are lost and their bodies are empty, i.e.
  they effectively **do nothing**).
- Block: `area` (area name; `any`/none — a random one from spawnAreas, chosen at wave start and
  **forcibly substituted** into all actions of the block that have an `Area` property),
  `delay` (float, delay from wave start). Children are actions read as in `<then>`. More than
  one variant is an exception: one `<random>` group is allowed, two are not (random groups in
  `<looped>` count only in a looped campaign). A text node directly under `<Wave>` fails.
- A wave ends when all blocks have finished and in area `any` there are no more characters of factions
  EVIL|WILD|DEMON than "luggage" items. Then a 5 s pause and the next
  wave; after the last one — victory. The wave HP bar = total HP of spawned units.
- Every 30 s, if there is no "luggage" on the map, a luggage with a magick (first) or an
  item (second) appears; at most 2 per wave. Contents come from the enabled Item/Magick packs
  (packs are hardcoded, tied to DLC; Magicka.Levels.Packs).
- Score: time multiplier, damage, pop-up numbers (scrollScore) — details are in the UI code.

### 7.2 TimedObjective (`type="timedobjective"`, 3 scenes — Vietnam)

Attributes: `texture` (frame, default `UI/HUD/Dialog_Say`), `bordersize` (32), `timer`
(id of the level timer with elapsed time, default `timestorage`). Children:
- `<objective name="#loc" id="counter" max="N" score="S"/>` — a HUD line; the value comes from
  counter `id`;
- `<timeBonus minTime maxTime minTimeBonus maxTimeBonus counter/>` — score multiplier, linear
  from maxTimeBonus to minTimeBonus by elapsed time.
The ruleset only displays; level completion is done by a trigger (`endGame`).

### 7.3 Versus (`type="versus"`, 12 scenes)

The concrete mode (DeathMatch, Brawl, Pyrite, Krietor, King) is chosen by lobby settings,
not XML. Children: `<Areas>` with a list of `<area team="A|red|B|blue" tourney="true">name</area>`
(spawn areas, per team; Krietor takes only `tourney="true"`); Krietor also has `<Tournament>`
with a list of `<magick unlock="start|<sec>">MagickType</magick>`.

## 8. Cutscenes

There are two different mechanisms:

1. **In-level cutscene** — the `cutscene` … `endCutscene` actions: letterbox mode,
   UI off, nearby magicks/barriers/mines/tornadoes are removed, the camera is controlled by
   `cameraMove`/`cameraRelease`, dialogs go to the bottom bar. Skipping is available only if
   `onSkip` is set: on skip, the onSkip trigger runs (with condition checks), usually
   with `clearDelayedActions` and `endCutscene`.
2. **Campaign map cutscenes** — `Levels/Cutscenes.xml`, `Levels/Tsar/Cutscenes.xml`,
   `Challenges/Dungeons_Cutscenes.xml`, `Dungeons2_Cutscenes.xml`; shown in the menu between
   chapters (a pan over the world map with voice-over):

```
<Cutscenes>
  <Cutscene id="1,2">                 ← chapter indices (comma-separated); Dungeons have no id
    <camera>
      <zoom time="0.0">1.05</zoom>     ← zoom keys
      <position time="8.0">1090,1400</position>  ← x,y position keys on the map
    </camera>
    <Dialog subtitles="#Cutscene_02">waveBank/CutS_cut02</Dialog>  ← [bank/]cue, default bank WaveBank
  </Cutscene>
</Cutscenes>
```

Keys are sorted by time, with smoothstep interpolation between neighbors; duration =
the maximum of the last keys' times. At least one key of each kind is required.

## 9. Action catalogue (113)

> The `Triggers.Actions` namespace has 116 files: the abstract base `Action`, two enums
> (`OperationAction` = Add/Remove/Set, `WizardAction` = StaffLightOn/Off) and **113** concrete
> actions. Likewise `Triggers.Conditions`: the base + **24** conditions. A tag is matched only
> against direct subclasses of the base class.

Tag = class name (any case). Columns: **Used** — number of occurrences across all scenes
(including waves and OnInteract), **Files** — in how many scenes. Attributes: `name`:type[=default].
Type `id` — a string lowered and hashed (by-name reference); `str` — a string (lower)
without hashing; `vec3` — `"x,y,z"`; `rgba` — `"r,g,b,a"` (0..1). No default given = the type's zero
(0, false, empty, the enum member with value 0 — for CompareMethod this is `equal`). `⁰` — the attribute is supported but never appears in the game.
All actions have the base attribute `delay` (float). Enum values: Order = None/Idle/Attack/Defend/
Flee/Wander/Panic; ReactTo — flags None/Attack/Proximity; Transitions = None/Fade/CrossFade;
Factions — flags EVIL, WILD, FRIENDLY, DEMON, UNDEAD, HUMAN, WIZARD, NEUTRAL(=all), PLAYER0..3,
TEAM_RED, TEAM_BLUE, PLAYERS; Banks = WaveBank/Music/Ambience/UI/Spells/Characters/Footsteps/
Weapons/Misc/Additional/AdditionalMusic; EndGameCondition = Victory/LevelComplete/Defeat/
VersusPlayer/VersusTeam/EightyeightMilesPerHour/ChallengeExit/EndOffGame/Disconnected/
ToBeContinued; LightTransitionType = None/Linear/Flicker.

The "by area" filter of kill/remove/overkill/releaseGrip/giveOrder: a character in the area matches
if `type="any"`, **or** the type matches, **or** the faction intersects (OR!). For `damage` the logic
is inverted: skip only if not `any`, the type does not match **and** the faction does not intersect (the same
set). If `id` is given, it works only on that entity and the area is ignored.

| # | Tag | Used | Files | Attributes | Semantics | Replay |
|---|---|---|---|---|---|---|
| 1 | `spawn` | 5188 | 119 | `area`:id, `snapToNavMesh`:bool, `targetArea`:id, `speed`:float=1, `type`:id, `nr`:int=1, `health`:float=1, `id`:id, `dialog`:id, `order`:Order=Attack, `reactTo`:ReactTo=Attack,Proximity, `reaction`:Order=Attack, `trigger`:id⁰, `neverRemove`:bool, `animation`:Animations, `idleAnimation`:Animations, `spawnAnimation`:Animations, `priorityTarget`:id, `priorityAbility`:int=-1⁰, `onDeath`:id, `onDamage`:id, `forceDraw`:bool, `meshId`:int=-1, `forceCamera`:bool, `forceNavMesh`:bool, `cannotDieWithoutExplicitKill`:bool⁰ | Creates `nr` NPCs of template `Data/Characters/<type>` at locator/area `area` (area → random point), sets the AI order (`order`/`reactTo`/`reaction`), `id`, dialog, spawn/idle animations, `onDeath`/`onDamage` triggers; child elements are an AIEvent script. Replay: recreates only survivors with saved HP. | spec. |
| 2 | `executeTrigger` | 1420 | 99 | `trigger`:id | Calls trigger `trigger` of the current scene **with checks** of its conditions and repeat; passes the initiator (sender) on. | — |
| 3 | `counter` | 1020 | 93 | `name`:id, `action`:CounterAction, `value`:int | Changes level counter `name`: `action` = INC/DEC/SET by `value`. | — |
| 4 | `giveOrder` | 978 | 96 | `order`:Order, `reactTo`:ReactTo, `trigger`:id, `reaction`:Order=Attack, `animation`:Animations, `idleAnimation`:Animations, `factions`:Factions, `id`:id, `type`:id, `area`:id, `targetArea`:id, `speed`:float=1, `priorityTarget`:id, `priorityAbility`:int=-1⁰, `explicit`:bool | Changes the AI order of NPC `id` or of all NPCs in area `area` selected by `type` OR `factions` (`explicit` — exact faction equality); child AIEvents; `animation`/`idleAnimation`. | spec. |
| 5 | `playAnimation` | 889 | 92 | `name`:id, `speed`:float=1, `start`:float=-1, `end`:float=-1, `loop`:bool, `children`:bool=true, `resume`:bool | Plays the animation of AnimatedLevelPart `name` (path `parent/child`), range `start`..`end` (−1 = whole clip), `speed`, `loop`, `children`, `resume`. | — |
| 6 | `playSound` | 814 | 130 | `id`:id, `cue`:id, `area`:id, `volume`:float, `radius`:float, `apply3D`:bool | Plays XACT cue `cue` (`bank/cue`, default bank Ambience). With `id` — a named scene sound (stopSound/setVolume); with `area` — a 3D sound at the locator (`radius`, `apply3D`). `volume` — the cue variable `Volume`. | — |
| 7 | `startDialog` | 515 | 95 | `dialog`:id, `forceOnScreen`:bool, `position`:id="interactor" | Shows the next `<interact>` of dialog `dialog`. `position`: `interactor` (default, the trigger initiator), an entity id, a locator name, `x,y` (screen) or `x,y,z` (world). | — |
| 8 | `spawnPhys` | 388 | 39 | `area`:id, `object`:str, `onDeath`:id, `onDamage`:id, `id`:id | Creates physics object `Data/PhysicsEntities/<object>` at locator `area`; `onDeath`/`onDamage` are triggers (for destructibles). No replay — the state is stored as saved entities. | — |
| 9 | `avatarMove` | 361 | 106 | — | Assigns an AIEvent script to avatars. Children: `<Player1>`..`<Player4>` (slot by the last digit; taken slot → next free one) or `<PlayerID name="<avatar id>">` (resolved at scene Initialize); inside — `move/animation/face/kill/loop`. Other children → exception. | — |
| 10 | `spawnEffect` | 310 | 28 | `effect`:id, `name`:id, `area`:id, `range`:float | Starts particle effect `effect` at locator `area` under name `name`; `range` > 0 — a beam, shortened to an obstacle/shield. | spec. |
| 11 | `stopEffect` | 309 | 56 | `name`:id | Stops the named scene effect `name` (from LevelModel or spawnEffect). | spec. |
| 12 | `cutscene` | 285 | 83 | `skipBarMove`:bool, `killDialogs`:bool=true, `onSkip`:id | Cutscene mode: letterbox, UI off, nearby magicks/projectiles are cleared. `onSkip` — trigger on skip (without it skipping is unavailable), `killDialogs`, `skipBarMove`. | spec. |
| 13 | `disableInput` | 254 | 87 | — | Blocks player controls (ControlManager.LimitInput). | yes |
| 14 | `setPosition` | 252 | 40 | `id`:id, `facingDirection`:bool, `area`:id | Teleports entity `id` (or `player1`..`player4`) to locator `area` with ground snapping; `facingDirection` — turn along the locator. | yes |
| 15 | `cameraMove` | 250 | 85 | `lockInput`:bool, `magnification`:float=-1, `time`:float=1, `target`:id, `offset`:vec3, `interpolation`:CameraInterpolation=Interpolated | Smoothly moves the camera to locator `target` (+`offset`) over `time`; `magnification` > 0 — zoom; `lockInput`; `interpolation`. | — |
| 16 | `enableInput` | 239 | 81 | — | Removes the control block. | yes |
| 17 | `endCutscene` | 230 | 80 | `skipBarMove`:bool | Exits cutscene mode. | spec. |
| 18 | `cameraRelease` | 222 | 79 | `time`:float=1 | Returns the camera to following the players over `time`. | — |
| 19 | `startEffect` | 197 | 46 | `name`:id | Starts the named scene effect `name` (empty name → generated id). | yes |
| 20 | `fade` | 194 | 56 | `color`:rgba, `fadeIn`:bool, `override`:bool, `time`:float | Screen fade: `fadeIn=true` — fade to `color` over `time`, `false` — fade out; `override` cancels the scene-change fade. | — |
| 21 | `remove` | 191 | 60 | `factions`:Factions, `id`:id, `type`:id, `area`:id, `force`:bool | Removes (kill + terminate) entity `id` or characters in area `area` by `type`/`factions`; `force` lifts "unkillability". | yes |
| 22 | `playMusic` | 136 | 103 | `cue`:id, `focusValue`:float | Starts music `cue` (`bank/cue`, default bank Music), `focusValue`. | — |
| 23 | `triggerCheckpoint` | 123 | 81 | `spawnPoint`:id, `spawnFairy`:bool=true, `saveToDisk`:bool=true⁰, children: `ignore`/`trigger` | Checkpoint: level state snapshot, respawn points `spawnPoint0..3`, resurrect/heal players, fairy (`spawnFairy`), write to disk (`saveToDisk`). Child `<ignore trigger="…"/>` — triggers reset on load. | — |
| 24 | `disablePoi` | 111 | 46 | `id`:id | Disables `<OnInteract locator="id">`. | spec. |
| 25 | `clearDelayedActions` | 101 | 59 | `trigger`:id | Cancels the delayed (not yet run by delay) actions of trigger `trigger`, or `all`/empty — of all triggers, `allbutthis` — of all except the current one. | — |
| 26 | `changeScene` | 94 | 77 | `scene`:id, `transition`:Transitions=Fade, `transitionTime`:float=1, `spawnPlayers`:bool, `spawnPoint`:id, `saveNPCs`:bool | Transition to scene `scene` (scene file name) at locators `spawnPoint0..3`; `transition`/`transitionTime`; `spawnPlayers`; `saveNPCs` — keep the NPCs of the scene being left. | — |
| 27 | `cameraShake` | 90 | 20 | `area`:id, `magnitude`:float, `time`:float | Camera shake `magnitude`/`time`, global or with falloff from locator `area`. | — |
| 28 | `cameraMagnify` | 84 | 58 | `magnification`:float=1, `time`:float=1 | Camera zoom to `magnification` over `time`. | — |
| 29 | `spawnItem` | 78 | 46 | `area`:id, `item`:id, `ignoreTractorPull`:bool, `attachTo`:id, `nr`:int=1, `physicsEnabled`:bool, `onPickup`:id, `id`:id | Creates item `Data/Items/Wizard/<item>` in `area`; `physicsEnabled`, `ignoreTractorPull`, `attachTo` (AnimatedLevelPart), `onPickup` trigger, `id`. | — |
| 30 | `stopSound` | 77 | 30 | `id`:id, `instant`:bool | Stops the named sound `id` (`instant` — without a tail). | — |
| 31 | `kill` | 71 | 22 | `factions`:Factions, `id`:id, `type`:id, `area`:id | Kills entity `id` or characters in area `area` by `type`/`factions` (clears CannotDieWithoutExplicitKill). | yes |
| 32 | `disableLight` | 61 | 10 | `id`:id, `effect`:LightTransitionType, `time`:float | Turns off LevelModel light(s) `id` (comma-separated list), `effect` None/Linear/Flicker over `time`. | spec. |
| 33 | `stopMusic` | 56 | 39 | — | Stops the music. | — |
| 34 | `spawnDispenser` | 52 | 1 | `area`:id, `model`:Dispensers, `types`:id, `amount`:str, `time`:float, `id`:id⁰, `active`:bool=true | Creates a "dispenser" `model` (SpiderHole/GoblinTent/SpiderEggs) in `area` that releases `types` (list) in amounts `amount` (list) with period `time`; `active`. | yes |
| 35 | `setFaction` | 49 | 6 | `id`:id, `faction`:Factions, `area`:id, `action`:OperationAction, `type`:id | Changes the faction (`action` Add/Remove/Set, `faction`) of `id`/`player1..4` or of characters in area `area` by `type`. | yes |
| 36 | `enableElement` | 48 | 6 | `element`:Elements | Tutorial: allows element `element`. | yes |
| 37 | `unlockAchievement` | 42 | 28 | `achievement`:str | Grants achievement `achievement`. | — |
| 38 | `overkill` | 40 | 8 | `factions`:Factions, `id`:id, `type`:id, `area`:id | Overkill (gibbing) for `id` or characters in the area by `type`/`factions`. | yes |
| 39 | `spawnMagick` | 38 | 28 | `area`:id, `immovable`:bool=true, `magick`:MagickType, `onPickup`:id, `id`:id, `timeOut`:float⁰ | Places magick book `magick` in `area`; `immovable`, `timeOut`, `onPickup`, `id`. | — |
| 40 | `setHint` | 35 | 9 | `appendController`:bool, `id`:str, `animation`:HintAnimations, `position`:Position=BottomRight | Tutorial hint: loc key `id` (`appendController` → suffix `_key`/`_pad`), `animation`, `position`. | — |
| 41 | `removeHint` | 34 | 10 | — | Removes the tutorial hint. | yes |
| 42 | `spawnBoss` | 32 | 22 | `id`:id, `boss`:str, `prop`:bool, `generic`:bool, `template`:str⁰, `delayed`:bool, `area`:id, `healthAppearDelay`:float⁰, `freezeTime`:float⁰, `healthBarWidth`:float=0.8, `meshId`:int=-1, `onDeath`:id, `onDamage`:id | Creates boss `boss` (hardcoded class by name; `prop` — PropBoss from PhysicsEntities; `generic` — GenericBoss from Characters) in `area`; `delayed` — do not start the fight; HP bar (`healthBarWidth`, `healthAppearDelay`, `freezeTime`). | yes |
| 43 | `cameraLock` | 30 | 18 | `time`:float=1, `area`:id | Locks the camera onto the box of trigger area `area` over `time`. | — |
| 44 | `enablePoi` | 29 | 13 | `id`:id | Enables `<OnInteract locator="id">`. | spec. |
| 45 | `printDebug` | 28 | 8 | `entityPos`:str, `break`:bool⁰, `counterName`:str=""⁰, `message`:str="" | Debug output; a no-op in release. | — |
| 46 | `unlockMagick` | 26 | 15 | `magickType`:MagickType | Unlocks magick `magickType` (in Versus — for all players) + pop-up text. | yes |
| 47 | `enterScene` | 25 | 16 | `scene`:id, `transition`:Transitions=Fade, `transitionTime`:float=1, `spawnPoint`:id, `moveTarget`:id, `facingDirection`:bool, `instantTrigger`:id, `trigger`:id, `ignoreTriggers`:bool, `saveNPCs`:bool | Like changeScene, plus after entry the avatars walk to locators `moveTarget0..3` (`facingDirection`, `ignoreTriggers` while walking); on arrival — trigger `trigger`; `instantTrigger` runs right after entry. | — |
| 48 | `spawnAnimProp` | 25 | 2 | `area`:id, `object`:str, `onDeath`:id⁰, `onDamage`:id⁰, `id`:id, `animation`:str="", `damageable`:str, `gravity`:str | Creates animated prop `Data/AnimatedProps/<object>` in `area`, start `animation`, `damageable`/`gravity` (bool strings), `id`. | — |
| 49 | `timer` | 25 | 5 | `name`:id, `value`:float, `paused`:bool | Sets the value (`value`) and/or pause (`paused`) of timer `name`. | yes |
| 50 | `activateBoss` | 24 | 16 | — | Starts the fight with a boss spawned with `delayed=true`. | yes |
| 51 | `enableLight` | 22 | 5 | `id`:id, `effect`:LightTransitionType, `time`:float | Turns on light(s) `id` with `effect`/`time`. | spec. |
| 52 | `endGame` | 22 | 19 | `type`:EndGameCondition, `freezeGame`:bool=true, `phony`:bool | Ends the game with outcome `type` (Victory/LevelComplete/Defeat/…); `freezeGame`, `phony`. | — |
| 53 | `disableElement` | 21 | 9 | `element`:Elements | Tutorial: forbids element `element`. | yes |
| 54 | `resumeAnimation` | 21 | 13 | `name`:id, `speed`:float=1, `length`:float, `children`:bool=true, `loop`:bool | Resumes the animation of AnimatedLevelPart `name` (`length`, `speed`, `loop`, `children`). | — |
| 55 | `stopAnimation` | 21 | 9 | `name`:id, `children`:bool=true | Stops the animation of AnimatedLevelPart `name` (`children`). | — |
| 56 | `damage` | 20 | 7 | `factions`:Factions, `id`:id, `type`:id, `area`:id, `source`:id, `ignoreShields`:bool=true, children: `amount`/`attackProperty`/`damage`/`element`/`magnitude` | Deals damage (child `<damage attackProperty element amount magnitude/>`) to entity `id` or to characters in the area by `type`/`factions`; `source` — source locator (direction, shield check), `ignoreShields`. | yes |
| 57 | `bossTitle` | 19 | 18 | `title`:id, `subTitle`:id, `displayTime`:float=-1, `fadeIn`:float=1, `fadeOut`:float=1 | Shows the boss title: loc keys `title`/`subTitle`, `displayTime` (−1 → 2 s), `fadeIn`/`fadeOut`. | — |
| 58 | `setDialog` | 19 | 7 | `dialog`:id, `id`:id | Assigns dialog `dialog` to character `id`. | yes |
| 59 | `disableCastType` | 18 | 4 | `type`:CastType | Tutorial: forbids cast type `type`. | yes |
| 60 | `cameraBias` | 17 | 8 | `bias`:vec3, `time`:float=20 | Shifts the camera point by `bias` over `time`. | — |
| 61 | `foundSecretArea` | 17 | 15 | `id`:str | Profile: secret `id` found (statistics/achievements). | — |
| 62 | `bossMessage` | 15 | 6 | `message`:str | Passes string `message` to the script logic of the current boss. | yes |
| 63 | `enableCastType` | 15 | 2 | `type`:CastType | Tutorial: allows cast type `type`. | yes |
| 64 | `killDialogs` | 14 | 9 | — | Closes all active dialogs. | yes |
| 65 | `addTimer` | 13 | 8 | `name`:id, `value`:float, `playedTimer`:bool | Creates timer `name` with value `value` (if it already exists — ignored); `playedTimer` — a "played" timer (paused in cutscenes). | yes |
| 66 | `assignItem` | 13 | 3 | `id`:id, `player`:int, `area`:id, `animation`:Animations | Gives item `Data/Items/Wizard/<id>` to player `player` (1–4), to the avatar in area `area`, or to everyone; `staff_default`/`weapon_default` — restore the defaults; `animation`. | spec. |
| 67 | `endBossFight` | 13 | 8 | — | Resets the boss fight (HP bar). | yes |
| 68 | `foundMoose` | 13 | 13 | `id`:str | Profile: moose `id` found. | — |
| 69 | `setDialogHint` | 12 | 3 | `trigger`:id, `scale`:float⁰, `size`:vec2⁰, `element`:Elements, `magick`:MagickType, `custom`:int⁰, `id`:id | Tutorial hint for an element/magick (`element`/`magick`/`custom`), text — loc key `id`, `trigger`, `scale`, `size`. | — |
| 70 | `killedAPony` | 11 | 3 | `id`:str | Profile: pony `id` killed. | — |
| 71 | `telemetryTutorialStep` | 11 | 1 | `tutorialName`:str="", `stepName`:str="" | Tutorial telemetry (a no-op for OpenMagicka). | yes |
| 72 | `scrollScore` | 10 | 3 | `value`:str, `ttl`:float, `area`:id, `offset`:str | Pop-up number `value` at area/locator `area` + `offset` (`x,y,z`), lifetime `TTL`. | yes |
| 73 | `magick` | 8 | 5 | `magickType`:MagickType, `id`:id, `position`:vec3⁰ | Casts magick `magickType` on behalf of `id` (or at `position`). | yes |
| 74 | `activateDispenser` | 7 | 1 | `order`:Order⁰, `reactTo`:ReactTo⁰, `id`:id⁰, `type`:Dispensers, `area`:id | Activates dispenser `id` or dispensers of type `type` in area `area`. | yes |
| 75 | `addCameraEffect` | 7 | 7 | `effect`:id | Enables camera visual effect `effect`. | yes |
| 76 | `displayTitleInLevel` | 7 | 5 | `noTranslation`:bool⁰, `title`:id, `subTitle`:id, `displayTime`:float=-1, `fadeIn`:float=1, `fadeOut`:float=1, `textAlignment`:str⁰ | In-level title: `title`/`subTitle` (loc keys; `noTranslation` — raw text), `displayTime`, `fadeIn`/`fadeOut`, `textAlignment` L/C/R. | — |
| 77 | `highlightAnim` | 6 | 1 | `name`:id, `time`:float=1 | Highlights AnimatedLevelPart `name` for `time`. | — |
| 78 | `setVolume` | 6 | 1 | `id`:id, `volume`:float | Changes the volume of named sound `id`. | — |
| 79 | `conditionalUnlockAchievement` | 5 | 5 | `achievement`:str, `victim`:id | Conditional achievement, only 4 hardcoded ones: `thisismagicka`/`therecanbeonlyone` (the last attacker of `victim` is a local player), `drivenmad`/`breezedthrough` (nobody died on the level). | — |
| 80 | `highlightPhys` | 5 | 1 | `id`:id, `time`:float | Highlights physics object `id` for `time`. | — |
| 81 | `setMusicFocus` | 5 | 4 | `id`:id, `radius`:float, `clearTo`:float=1 | Music "focus" from the distance to locator `id` (`radius`, `clearTo`). | yes |
| 82 | `wizardControl` | 5 | 5 | `action`:WizardAction | Staff light: `action` StaffLightOn/StaffLightOff. | yes |
| 83 | `disableUI` | 4 | 4 | — | Hides the HUD. | yes |
| 84 | `rollCredits` | 4 | 4 | `file`:id | Starts the credits from file `file`. | yes |
| 85 | `cameraFollow` | 3 | 3 | `id`:id | The camera follows entity `id`. | — |
| 86 | `disablePush` | 3 | 3 | — | Tutorial: forbids push. | yes |
| 87 | `flash` | 3 | 3 | `duration`:float=0.5 | White screen flash for `duration`. | yes |
| 88 | `spawnElemental` | 3 | 1 | `id`:id⁰, `area`:id, `snapToNavMesh`:bool, `nr`:int, `proximity`:float | Creates `nr` "elemental eggs" in `area` (`proximity` — wake-up radius, `snapToNavMesh`). | spec. |
| 89 | `spawnGenericBoss` | 3 | 3 | `area`:id, `targetArea`:id⁰, `speed`:float=1⁰, `type`:id, `health`:float=1⁰, `id`:id, `dialog`:id⁰, `order`:Order=Attack, `reactTo`:ReactTo=Attack,Proximity, `reaction`:Order=Attack, `trigger`:id⁰, `neverRemove`:bool, `animation`:Animations, `idleAnimation`:Animations⁰, `spawnAnimation`:Animations⁰, `priorityTarget`:id⁰, `priorityAbility`:int=-1⁰, `onDeath`:id⁰, `onDamage`:id⁰, `forceDraw`:bool⁰, `meshId`:int=-1⁰, `forceCamera`:bool⁰, `forceNavMesh`:bool⁰, `delayed`:bool⁰, `healthAppearDelay`:float⁰, `freezeTime`:float⁰, `healthBarWidth`:float=0.8, `cannotDieWithoutExplicitKill`:bool | Spawn-like (the same attributes as spawn) + registration as a boss with an HP bar. | — |
| 90 | `enablePush` | 2 | 1 | — | Tutorial: allows push. | yes |
| 91 | `endUnequipWeaponsSection` | 2 | 1 | `animation`:Animations | End of the "no weapons" section; `animation` — the avatars' attack animation. | — |
| 92 | `removeItemFromAvatars` | 2 | 1 | `item`:id | Removes item `item` from avatars (restores the template item). | yes |
| 93 | `spawnCloneWizards` | 2 | 2 | `area`:id, `snapToNavMesh`:bool⁰, `targetArea`:id⁰, `speed`:float=1⁰, `nr`:int⁰, `health`:float=1⁰, `id`:id⁰, `dialog`:id⁰, `order`:Order=Attack, `reactTo`:ReactTo=Attack,Proximity, `reaction`:Order=Attack⁰, `trigger`:id⁰, `neverRemove`:bool⁰, `animation`:Animations⁰, `spawnAnimation`:Animations⁰, `priorityTarget`:id⁰, `priorityAbility`:int=-1⁰, `onDeath`:id⁰, `onDamage`:id⁰ | Creates clones of each living player (their template/color) at locators `area0..3`, id `CloneWizard<i>`; AI attributes as in spawn. | yes |
| 94 | `telemetryTutorialEnd` | 2 | 1 | — | Telemetry (no-op). | yes |
| 95 | `activateHealthBar` | 1 | 1 | — | Shows the configured generic bar (HealthBar). | yes |
| 96 | `addItemToAvatars` | 1 | 1 | `item`:id, `bone`:str | Attaches item `item` to bone `bone` of all avatars. | yes |
| 97 | `beginDefaultAvatarSection` | 1 | 1 | — | Flag PlayState.DefaultAvatarSection = true (a section with the default avatar). | — |
| 98 | `beginUnequipWeaponsSection` | 1 | 1 | — | Start of the "no weapons" section. | — |
| 99 | `cameraPosition` | 1 | 1 | `magnification`:float=-1, `target`:id, `offset`:vec3 | Instantly places the camera at locator `target` (+`offset`), `magnification`. | — |
| 100 | `endDefaultAvatarSection` | 1 | 1 | — | DefaultAvatarSection = false. | — |
| 101 | `healthBar` | 1 | 1 | `displayName`:id⁰, `normalizedHealth`:float=1⁰, `displayHealth`:float⁰, `time`:float, `type`:GenericHealthBarTypes, `power`:float=1.1⁰, `alpha`:float⁰, `count`:float⁰, `graphicsType`:GenericHealthBarGraphics, `initialTimerDelay`:float, `isScaled`:bool, `isColoredRed`:bool⁰, `showDisplayName`:bool⁰, `hasAnimatedSprite`:bool, `animationOffsetPositionY`:float=16⁰, `fadeTime`:float, `onEndTrigger`:id | Configures the generic bar: `type` HP/TimerIncreasing/TimerDecreasing/Counter, `time`/`count`, graphics, `onEndTrigger`, etc. | yes |
| 102 | `removeAllStatusEffects` | 1 | 1 | — | Removes all status effects/auras/shields/levitation/spells from all characters. | yes |
| 103 | `removeDialogHint` | 1 | 1 | — | Removes the tutorial dialog hint. | yes |
| 104 | `removeHealthBar` | 1 | 1 | — | Hides the generic bar. | yes |
| 105 | `telemetryTutorialBegin` | 1 | 1 | `tutorialName`:str="", `stepName`:str="" | Telemetry (no-op). | yes |
| 106 | `telemetryTutorialSkip` | 1 | 1 | — | Telemetry (no-op). | yes |
| 107 | `teleport` | 1 | 1 | `effect`:id, `sound`:id, `id`:id, `target`:id | Teleports entity `id` to locator `target` with effect `effect` and sound `sound`. | yes |
| 108 | `disableDark` | 0 | 0 | — | Tutorial: turns off darkening. | yes |
| 109 | `enableDark` | 0 | 0 | — | Tutorial: turns on screen darkening. | yes |
| 110 | `enableUI` | 0 | 0 | — | Shows the HUD. | yes |
| 111 | `physAnimation` | 0 | 0 | `id`:id⁰, `name`:id⁰, `speed`:float=1⁰, `start`:float⁰, `end`:float⁰, `children`:bool=true⁰ | Binds the animation of AnimatedLevelPart `name` to destructible physics object `id`. | yes |
| 112 | `releaseGrip` | 0 | 0 | `factions`:Factions⁰, `id`:id⁰, `area`:id⁰, `type`:id⁰ | Releases characters grabbed by NPC `id` or by NPCs in the area. | yes |
| 113 | `removeCameraEffect` | 0 | 0 | `effect`:id⁰ | Removes camera effect `effect` (empty — all). | yes |

### 9.1 Ours: the characters' actions (OM-45)

Ours is `OpenMagicka.Engine.Scripting` (`CharacterActions.cs`, `ScriptEvents.cs`); a scene being played is a `RunningLevel`
(the world given the level's bestiary, nav mesh and places; a frame runs the world, then the level's timers, then the
triggers, as the original).

**Places.** An action's `area` is the original's scene locator: a locator's transform; else an area's random point, turned a
random way about the vertical; else the origin. The animated parts' locators come with the parts (OM-47).

**AI events** (`<move>`, `<animation>`, `<face>`, `<kill>`, `<loop>`, children of `spawn`, `giveOrder` and the `avatarMove`
players) are read when the scene is built, as the original:
- a `move` to an area takes one random point of it then, for good; a name that is neither a locator nor an area walks to the
  origin; `position="x,y,z"` is a point;
- `facingDirection` is either `x,y,z` (then fixed) or a bool;
- an unknown event or attribute fails the loading, and so (ours) does a direction of no length, which would turn the original
  to NaN.

**`spawn`**:
- each of the `nr` characters is placed anew (so an area scatters them); it stands on the floor up to 9 below, 0.1 over it
  (none found: at the point);
- it faces the place's way; `snapToNavMesh` moves the point to the nav mesh first;
- the order, triggers, health (not clamped), name, dialog and clips are set as given;
- a `spawnAnimation` other than `idle`/`idle_agg` makes it rise with that clip;
- a network client spawns none, and a full level stops the spawning;
- `forceDraw`, `forceCamera`, `forceNavMesh` and `meshId` are read and wait for the renderer and camera.

The survivors and their hit points are what a checkpoint replays (OM-47).

**`giveOrder`**:
- by `id`: the NPC of that name, which panics at once on a `panic` order;
- else the NPCs in the area by template, **or** template faction, or with exactly those factions with `explicit`;
- with neither type nor factions, an area order reaches none, as the original;
- grips are let go when grips come (OM-33).

**`avatarMove`**: each player's avatar runs its events through a mind of its own, its states acting every frame while events
remain (no reactions or abilities); its death ends them. `<Player1>`..`<Player4>` take that slot or the next free one, and `<PlayerID name=>` takes
the slot of the avatar of that name when the scene is built. Input waits on `Character.IsInEvent` (OM-31).

**`setPosition`**:
- the named character, or `player1`..`player4` when playing, moves onto the floor up to 4 below the place;
- it faces the place's way with `facingDirection`, or when no floor was found;
- other entities wait for their bodies.

**`remove`**:
- the named character, or those in the area by template or template faction, the players too, vanish dead at once, with no
  corpse and no `onDeath` trigger (the original's kill only empties the hit points, and its removal marks them dead first);
- `force` lifts a named one's need of an explicit kill (in an area it is not looked at, as the original).

**Triggers of characters:**
- A character's `onDeath` trigger fires as it dies, and its `onDamage` trigger on each hit that hurts it. Both run with
  conditions, with the character as the sender.
- With a death clip, a killing hit fires `onDamage` first and `onDeath` when the clip ends.
- A `spawn` whose health or speed is not finite fails the loading.

**Where the original crashes**, ours does nothing: an area the scene lacks, or a missing name.

## 10. Condition catalogue (24)

All conditions have the base attribute `invert` (bool).

| # | Tag | Used | Files | Attributes | Semantics |
|---|---|---|---|---|---|
| 1 | `counter` | 955 | 95 | `name`:id, `compareMethod`:CompareMethod, `value`:int | Compares level counter `name` with `value` (`compareMethod`; LESS = counter < value). |
| 2 | `present` | 887 | 111 | `includeInvisible`:bool=true, `area`:id, `type`:id, `nr`:int, `compareMethod`:CompareMethod | The number of living characters of template `type` (`any` — all) in area `area` is compared with `nr`; `includeInvisible=false` subtracts invisible ones. |
| 3 | `dialogDone` | 259 | 72 | `dialog`:id, `interactIndex`:int=-1 | Dialog `dialog` has finished at least once and > `interactIndex` interacts were played (−1 = any). |
| 4 | `idPresent` | 253 | 34 | `area`:id="any", `id`:id | The entity with unique `id` is present and alive in area `area` (default `any`). |
| 5 | `triggered` | 203 | 51 | `trigger`:id | Trigger `trigger` has already fired (HasTriggered). |
| 6 | `factionPresent` | 111 | 34 | `includeInvisible`:bool=true, `area`:id, `factions`:Factions, `nr`:int, `compareMethod`:CompareMethod | The number of living characters with a faction from `factions` (flags) in area `area` is compared with `nr`. |
| 7 | `characterHealth` | 35 | 8 | `id`:id, `compareMethod`:CompareMethod, `health`:float | The HP fraction (0..1; dead/missing → 0) of character `id` is compared with `health`. |
| 8 | `interactorIs` | 24 | 3 | `id`:id | The trigger initiator (sender) has unique id `id`. |
| 9 | `robeInUse` | 24 | 3 | `name`:id, `player`:int | Player `player` (1–4; 0 = any) plays with robe template `name`. |
| 10 | `timer` | 13 | 7 | `name`:id, `compareMethod`:CompareMethod, `value`:float | Level timer `name` is compared with `value` (EQUAL — exact float equality). |
| 11 | `bossHealth` | 10 | 8 | `compareMethod`:CompareMethod, `value`:float | The boss's normalized HP is compared with `value` (no boss → false, even with invert). |
| 12 | `noCutsceneActive` | 9 | 4 | — | No cutscene is active. |
| 13 | `hasAchievement` | 6 | 1 | `name`:str | The player has achievement `name`. |
| 14 | `anyPlayerHasDied` | 4 | 2 | — | One of the players has died on this level. |
| 15 | `characterCasting` | 4 | 1 | `id`:id="", `castType`:CastType, `element`:Elements | Character `id` is currently casting a spell of type `castType` that contains element `element`. |
| 16 | `bossDead` | 3 | 3 | — | The boss is dead (no boss → false). |
| 17 | `dialogActive` | 3 | 1 | `dialog`:id | Dialog `dialog` is currently shown (`any`/empty — any message box). |
| 18 | `hasDLC` | 3 | 1 | `type`:DLC | DLC `type` is installed (WizardHat/Vietnam). |
| 19 | `playerCount` | 2 | 2 | `compareMethod`:CompareMethod, `nr`:int | The number of players is compared with `nr`. |
| 20 | `playersHaveStatusEffect` | 2 | 1 | `statusEffect`:StatusEffects | At least one player has **any** status effect (the `statusEffect` attribute is effectively ignored). |
| 21 | `playerHasElement` | 1 | 1 | `element`:Elements | At least one player has element `element` in the spell queue. |
| 22 | `dialogHintDone` | 0 | 0 | — | The tutorial dialog hint is closed. |
| 23 | `isCampaignLooped` | 0 | 0 | — | The campaign has been completed again (NG+); outside Campaign — exception. |
| 24 | `physicsPresent` | 0 | 0 | `onlyDamagable`:bool⁰, `area`:id="any"⁰, `nr`:int⁰, `compareMethod`:CompareMethod⁰ | The number of physics objects (`onlyDamagable` — only destructible ones) in area `area` is compared with `nr`. |

**Further, as the original** (ours: `OpenMagicka.Engine.Scripting`, the `*Condition` kinds; the game state beyond the world in
`ScriptGame`):
- `present` with `includeInvisible="false"` takes off every invisible character of the template that is in the area, the dead too
  (who were not counted), so the count can go below the true one; with `type="any"` it takes off none.
- `factionPresent` counts a character once for each of its template's factions among `factions`; `includeInvisible="false"`
  takes each invisible one off once, the dead too, if its faction now (charmed, say) is among them.
- `characterHealth`: a `health` outside 0..1 fails the loading; the share is not clamped; equal is within 0.0001.
- `bossHealth`: equal is within 0.000001 (`timer` and `counter`: exact).
- `characterCasting` asks the spell for one element at a time: a combination (`element="fire,water"`) is never in it.
- `playerHasElement` holds for any element of a combination in `element`.
- `playersHaveStatusEffect` holds when a player has any of the nine statuses; `statusEffect` is read but not asked about.
- `robeInUse` with no `name` never holds; `dialogActive` with no `dialog` or `any` asks whether any message box is up.
- `bossDead` and `bossHealth` with no boss fight do not hold, inverted or not.

**Ours, where the original crashes:** an area the scene does not have (the area conditions do not hold, inverted or not; no game
scene has one), a trigger it does not have (`triggered` is false), a `robeInUse` player beyond the slots (false), and
`isCampaignLooped` outside a campaign (false). Within one it is the scene's looped flag, the one that lets the `<looped>` blocks
in.

**Not yet fed:** the dialogs (OM-49), the boss fight, the cutscenes, the deaths on the level, the achievements, the packs and the
save's loop flag reach the scripts through `ScriptGame`, which their own tasks fill; until then nothing there is shown, done,
won or owned (`ScriptGame`); the campaign's loop is the scene's own flag. `physicsPresent` counts entities that are physics objects (`Entity.IsPhysicsObject`), none until the props come
(`spawnPhys`, OM-45).

## 11. Dialogs (`Levels/Dialog_*.xml`)

They are included via `<Dialogs>` in `.lvl`; `Levels/Dialog_Fairy.xml` is **always** loaded as well
(a shared dictionary — an id collision causes an error). The schema is strict: unknown elements and
attributes at the Dialog/Interact/Message levels → exception.

```
<Dialogs>
  <Dialog id="horse" iconText="EXAMINE" repeatFrom="0">      ← 615 total
    <interact lock="false" focusOwner="false">              ← 728; one "showing"
      <message character="owner" type="say" facing="false"> ← 759; one line window
        <text sound="wavebank/POI_generic" anim="none" TTL="2">#PoI_Horse1</text>  ← 907
      </message>
    </interact>
  </Dialog>
</Dialogs>
```

| Level | Attribute | Default | Semantics |
|---|---|---|---|
| Dialog | `id` | — | lower → hash; referenced by `startDialog`, `spawn dialog=`, `setDialog`, `dialogDone`, `dialogActive` |
| Dialog | `iconText` | Talk | InteractType for the icon when hovering over the NPC |
| Dialog | `repeatFrom` | **0** | after the last interact, jump to this index (and reset repeat counters). Negative — the dialog "ends" |
| Interact | `lock` | false | freeze physics and input while shown |
| Interact | `focusOwner` | true | camera/focus on the speaker |
| Interact | `count` | 1 | how many times to show this interact before moving to the next one; ≤0 — forever (not used in the game) |
| Message | `character` | — | speaker id; `owner` — the dialog owner (NPC/initiator) |
| Message | `type` | Say | the only MessageType value |
| Message | `facing` | true | the speaker turns to face the listener |
| Message | `forceOnScreen` | true | keep the window within the screen |
| Message | `width` | 600 | line wrap width, px |
| text | `sound` | — | `[bank/]cue`, default bank Characters |
| text | `anim` | talk_casual0 | speaker animation (enum Animations) |
| text | `TTL` | −1 | auto-continue after N s; −1 — wait for input |
| text | (text) | — | `#locKey[,arg1,arg2…]` — loc key; in the translation, placeholders `#N;` insert the loc string of argument N, `#key;` — the loc string of key (highlighted in color) |

Showing semantics: each `startDialog` (or a click on an NPC with a dialog) takes the **next**
interact (`Pop`) and plays its messages in turn (player input or TTL). A dialog
is marked "finished" when any of its interacts has played to the end. If the dialog is already shown,
a repeated start is ignored. Up to 8 windows at once; in a cutscene the text goes to the bottom bar.
`dialogDone interactIndex=k` is true if the dialog is finished and more than k interacts were played.
Dialog state (position, finished flag) is part of the checkpoint.

Parsing details (18 files, 615 dialogs, 728 interacts, 759 messages, 907 lines in the game):
- The file needs a top-level `<Dialogs>`. Inside `<Dialogs>`, `<Dialog>` and `<interact>` every
  node except comments must be the expected element (`Dialog`, `Interact`, `message`; names
  ignoring case), otherwise loading fails; a text node fails too.
- The children of `<message>` are iterated without skipping comments (a comment or text inside
  a message → error); the `<text>` tag name is not checked (any element = a line).
- Dialog `id` is lowered and hashed; without one the key is 0 (`id=""` is the hash of the empty
  string). The level's file and `Dialog_Fairy.xml` fill one dictionary: two dialogs with the
  same key, in one file or across both, → error. `repeatFrom` and `count` are ints,
  `lock`/`focusOwner` bools (`bool.Parse`, a bad value fails), `iconText` an InteractType.
- Message flags are lenient: `facing` and `forceOnScreen` use `bool.TryParse`, so a bad value
  means **false**; a bad `width` keeps 600. `character` is lowered. `type` must parse as
  MessageType, whose only name is `Say` (as with every enum, a number is accepted too).
- Line: `TTL` float (default −1); `anim` an Animations name ([ENUMS](ENUMS.md)); `sound` is
  lowered and split at `/`: one part = cue in the Characters bank, two = bank (Banks enum) and
  cue, more → error. Other line attributes are ignored. The text is lowered and split at `,`
  (no trimming): the first part is the loc key, the others the argument keys.

## 12. Parsing quirks and pitfalls (for compatibility)

1. Triggers are processed in ascending order of id hash, not file order. Scripts
   that rely on order within one frame are rare but possible.
2. An unknown action/condition tag or a text node inside `<then>`/`<if>` → load failure.
   An unknown **attribute** of an action/condition → silently ignored; on `<trigger>`, `<OnInteract>`,
   `<Dialog>`, `<interact>`, `<message>` and in `.lvl/<Scenes>` — exception. Example: `havindr_s5HBF.xml`
   with the `repeatitions` attribute on OnInteract would not load (the scene is not referenced).
3. Under `<Wave>` any tag is treated as a wave block (see §7.1) — `GiveOrder` directly in a wave
   is not executed.
4. `startDialog position="x,y,z"` — due to a bug the third component is written into Y, and Z stays 0.
5. `activateDispenser` with an area looks up the dispenser by its own `id` instead of iterating the area's entities
   (it effectively works only when an id is given).
6. `playersHaveStatusEffect` ignores the `statusEffect` attribute — it checks any of 9 statuses.
7. The `timer` condition with `equal` compares floats exactly — in practice almost never true.
8. Hashing: attribute strings are lowered, but locator names in LevelModel are not;
   `area` in `.lvl` Start is not lowered.
9. `repeat` accepts both bool and a number; any other value — log/assert and −1.
10. A Survival wave block forcibly overwrites the `Area` property of all its actions
    (via reflection on the property name `Area`).
11. Tutorial/telemetry/profile/achievements (`telemetry*`, `foundMoose`, `killedAPony`,
    `foundSecretArea`, `unlockAchievement`, `printDebug`) do not affect gameplay — they can be
    implemented as stubs.
12. On a network client, spawn actions return immediately; the server is authoritative.
13. At most 65535 actions per level (global handle numbering for network/saves).

## 13. Recommendations for OpenMagicka

- Safety limits beyond the original: XML nesting is limited to 256 levels (the original's
  `InnerText` would overflow the stack), reference expansion in strings is bounded (see
  [Engine architecture](../engine/ARCHITECTURE.md) §10).
- Parser: XML DOM, case-insensitive name comparison, a "tag → action factory" table
  with an attribute schema (from §9/§10); unknown attributes — a warning, not an error.
- Store string keys (lower) instead of hashes; the §2.1 hash is needed only for importing
  original saves.
- Preserve the semantics: OR between `<if>`, repeat (−1/0/N), the delay queue, sender, random+regular,
  log replay on checkpoint with an `ignore` list, shared level counters/timers, scene state
  for the whole level.
- Deterministic trigger order: for compatibility — by hash; for simplicity — by file
  (the risk is minimal, but it is worth checking on the campaign).

## 14. Open questions

1. Which entities go into the checkpoint's "saved entities" (`EntityStateStorage`) and how this
   combines with `spawn` replay (living NPCs are recreated from the log) — are there duplicates with
   NPCs saved via `saveNPCs`.
2. The case of locator names inside `LevelModel` `.xnb` — are they all lower (otherwise some references in the
   original "do not work" and this must be reproduced). Needs checking with an xnb decoder.
3. Exact semantics of `forceCamera`/`forceNavMesh` on the scene and NPCs, `IsInvisible` in the
   `present`/`factionPresent` conditions.
4. Details of `cameraMove`/`cameraLock`/`cameraBias` (interpolation functions, priorities) — in
   `MagickCamera`, outside this document.
5. Logic of specific bosses (`spawnBoss boss=<name>`, `bossMessage`) — hardcoded in
   `Magicka.GameLogic.Entities.Bosses`; a separate document with the list of names and messages is needed.
6. The Survival/TimedObjective score formula and leaderboards — describe separately together with the HUD.
7. `TutorialManager`: semantics of `enableDark`, dialog hints, element/cast restrictions.
8. Binary checkpoint format in the save (the field order is described above, but not byte by byte); whether
   OpenMagicka needs import of original saves.
9. Behavior of a `Trigger` without `id` (id = 0): key collisions are possible with several such triggers.
10. Survival/Versus in network play: which actions run via `LocalUpdate`.
