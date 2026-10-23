# OpenMagicka — analysis of the original Magicka engine

Source: the Steam installation of Magicka (AppID 42910),
`Magicka.exe` version **1.10.4.2** (SHA-256 `a896e05a…12ee7`, MVID `11bf844c-2882-4ec8-aed8-39169d72b637`).

> Project rule: **clean-room**. The original code is studied only to understand formats
> and behaviour; decompiled code is never copied into the repository. Game assets are never
> put into the repository: the user points OpenMagicka at their own purchased copy.

## 1. The original stack

| Layer | Technology |
|---|---|
| Runtime | .NET Framework 3.5 (CLR 2.0), x86 only (`32-bit-required`) |
| Framework | **XNA Framework 3.1** (`Microsoft.Xna.Framework`, `.Game`) |
| Render engine | `PolygonHead.dll` — Arrowhead's own engine: deferred shading, lights, particles, post effects, BiTree level models, bitmap fonts |
| Animation | `XNAnimation.dll` 0.7 (open source, skinning + AnimationClip) |
| Physics | `JigLibX.dll` 0.0.2 (open-source port of JigLib), plus custom collision detectors in `Magicka.Physics` |
| Audio | XACT (`.xgs` / `.xsb` / `.xwb`) |
| Input | XNA GamePad + DirectInput (`Microsoft.DirectX.DirectInput`) + `XInputHelper.dll` |
| Online/services | Steam (`SteamWrapper.dll`, `steam_api.dll`), Paradox (`PdxConnect`, `pops_api`), GameSparks — **not needed for the open implementation** |

P/Invoke use is minimal: `QueryPerformanceCounter/Frequency`, `GetCurrentThreadId`, `LoadCursorFromFile`, `TranslateMessage`, `MapVirtualKey`.

## 2. Code structure of `Magicka.exe` (≈2054 types, not obfuscated)

Key namespaces:

- `Magicka` — `Game : Microsoft.Xna.Framework.Game`, `Program`, global enums (`Elements`, `StatusEffects`, `Factions`, `Animations`, `CollisionMaterials`…), `SharedContentManager`.
- `Magicka.GameLogic.GameStates` — `GameStateManager`, `CompanyState` → `MenuState` → `PlayState`, `LoadingScreen`.
- `Magicka.GameLogic.Spells` — the gameplay core: `Spell`, `SpellManager`, `SpellTree`/`SpellNode`, `MagickTree`, `CastType`, `SpellType`, `StatusEffect`; effects: `ProjectileSpell`, `SpraySpell`, `LightningSpell`, `ShieldSpell`, `PushSpell`, `RailGunSpell`.
- `Magicka.GameLogic.Entities` (+ `Abilities`, `SpecialAbilities` ×114, `Bosses` ×35, `Buffs`, `CharacterStates` ×27, `Items`, `AnimationActions` ×29) — characters, items, bosses, magicks.
- `Magicka.Levels` — `Level`, `GameScene`, `LevelModel`, `Liquid`/`Water`/`Lava`, `ForceField`, waves, rulesets (`SurvivalRuleset`, `TimedObjectiveRuleset`).
- `Magicka.Levels.Triggers` — `Actions` (113 types) and `Conditions` (24 types): the level scripting system.
- `Magicka.AI` — `Agent`, `AIManager`, `AgentStates`, `Arithmetics` (fuzzy logic), `Messaging`.
- `Magicka.PathFinding` — `NavMesh`, `AnimatedNavMesh`, `NavMeshOctree`.
- `Magicka.Physics` — `PhysicsManager`, `CharacterBody`, custom detectors (capsule/hollow sphere/static mesh), `IceMesh`, `WaterMesh`.
- `Magicka.Graphics` — `MagickCamera`, `EffectManager`, `DecalManager`, `Octree`, `TracerMan`, fonts.
- `Magicka.Audio` — `AudioManager`, banks, `RoomType` reverb.
- `Magicka.Network` (84 types) — network co-op/versus on top of Steam.
- `Magicka.GameLogic.UI*` — menus, HUD (Gold/Iron/Moba styles), widgets, UI animations.
- `Magicka.Localization`, `Magicka.Storage` (saves + `Encryption`), `Magicka.Achievements`.
- Not needed for the open implementation: `Magicka.WebTools.*` (GameSparks, Paradox telemetry, monetization, GDPR), `Magicka.DRM`.

`PolygonHead.dll`: `RenderManager`, `Scene`, `Camera`, `Effects` (deferred, liquid, lava, additive, skinned, point/spot/directional lights, bloom, fog, skymap, transitions), `Lights`, `ParticleEffects` (Continuous/Pulse emitters, half vectors), `Models.BiTreeModel`, `PackedContent` (unused in the Steam build — files are stored loose).

Shaders are embedded as compiled DX9 Effect bytecode (`*EffectCode` classes + FieldRVA), plus 51 `.xnb` files in `Content/Shaders`.

## 3. Content (`Content/`, ~1.7 GB)

| Folder | Size | Contents |
|---|---|---|
| `Audio` | 652 MB | XACT: `magicka.xgs`, 11 × `.xsb`, 13 × `.xwb`, `SoundList.xnb` |
| `UI` | 317 MB | textures, JSON animations/sprite sheets |
| `Levels` | 263 MB | `.lvl` (campaign/level XML) → scenes `*.xml` (script) + `*.xnb` (`LevelModel` geometry) |
| `Models` | 85 MB | `Model` / `SkinnedModel` + textures |
| `Languages` | 75 MB | 8 languages, `*.loctable.xml` (Excel SpreadsheetML) + `BitmapFont` |
| `Effects` | 7 MB | ~900 XML particle effect descriptions |
| `Data` | 2.8 MB | character/item/physics object templates (`.xnb`), store/event JSON |
| `Shaders`, `EffectTextures`, `FBX` | — | effects, effect textures |

### 3.1 XNB format

- All 4308 files: `XNB` header + platform `w` + **version 4** (XNA 3.1). 4299 are compressed (flag `0x80`), 9 are not.
- Compression: **LZX** (64 KB window, 2^16), 32 KB frames; block header: 2 size bytes, or `0xFF` + 2 bytes of frame size + 2 bytes of block size.
- Research decoder: [`tools/research/xnb.py`](../tools/research/xnb.py). Verified: all 4308 files are recognised without errors, and full decompression matches the declared size (including a 6 MB level file).
- After decompression: 7-bit type reader count → list (name + int32 version) → shared resources → object.

### 3.2 ContentTypeReaders in use (file counts)

Primary type of the file:

| Reader | Files | Origin |
|---|---|---|
| `Texture2DReader` | 2296 | XNA |
| `ModelReader` | 553 | XNA |
| `ItemReader` | 454 | Magicka |
| `CharacterTemplateReader` | 338 | Magicka |
| `SkinnedModelReader` | 327 | XNAnimation |
| `LevelModelReader` | 132 | Magicka |
| `PhysicsEntityTemplateReader` | 69 | Magicka |
| `BitmapFontReader` | 66 | PolygonHead |
| `EffectReader` | 52 | XNA |
| `TextureCubeReader` / `Texture3DReader` | 13 / 7 | XNA |

Nested: `RenderDeferredEffectReader` (710), `SkinnedModelBone/BasicEffect/AnimationClipReader` (XNAnimation), `BiTreeModelReader` (132), `RenderDeferredLiquidEffectReader` (53), `AdditiveEffectReader` (33), `LavaEffectReader`, `SkinnedModelDeferredNormalMappedEffectReader`, `CollisionSkinReader`, standard XNA ones (`VertexBuffer`, `IndexBuffer`, `VertexDeclaration`, `ExternalReference`, `List`, `Dictionary`, primitives).

In total the open implementation needs **~12 non-standard readers** + the standard XNA 3.1 ones (format v4 differs from XNA 4.0/MonoGame v5).

### 3.3 Data in plain text (XML/JSON)

Most of the game design is text, which makes the job much easier:

- `Levels/*.lvl` — campaign: start scene, dialogs, list of scenes.
- Scenes `Levels/<Chapter>/*.xml` — model, lighting/bloom/fog/reverb and **triggers**: `<trigger><if>…</if><then>…</then></trigger>`. The most common actions: `spawn`, `counter`, `waveActions`, `Move`, `executeTrigger`, `giveOrder`, `playAnimation`, `playSound`, `startDialog`, `spawnPhys`, `cameraMove`, `cutscene`… Tags are **case-insensitive** (both `playmusic` and `playMusic` occur).
- `Levels/Dialog_*.xml` — dialogs (`Dialog` → `interact` → `message` → `text sound= anim= TTL=`).
- `Effects/**/*.xml` — particle effects (`ContinuousEmitter`, spread/lifetime/size parameters, key frames).
- `Languages/<lang>/*.loctable.xml` — strings (`#Key` → text), Excel 2003 XML format.
- `SaveData/*.sav` — **not encrypted**: `Settings/campaign/Profile/ch_*.sav` are `BinaryWriter` files with a version string prefix, `DLCData.sav` is XML. `Encryption` is only used for `pops.sav` (the Paradox account, which we do not need). Details: [engine/ARCHITECTURE.md](engine/ARCHITECTURE.md).
- `col.dat` — text; the first line is the number of unlocked player colours.

## 4. Recommended OpenMagicka architecture

**C# / .NET 10 LTS + FNA** (open-source reimplementation of XNA 4.0). Decision and rationale: [ADR-0001](adr/0001-stack.md), network: [ADR-0002](adr/0002-network.md).

- The API is close to XNA 3.1 — a familiar model for carrying the behaviour over.
- Through MojoShader FNA runs **DX9 effect bytecode** — there is a chance to use the original shaders from the user's assets without rewriting them.
- FAudio/FACT supports **XACT** banks (compatibility with the XNA 3.1 bank version had to be checked).
- Cross-platform (Windows/Linux/macOS, Vulkan/D3D11/Metal through SDL_GPU/FNA3D).
- XNB v4 is read by our own readers (LZX is ported from the research decoder).
- JigLibX and XNAnimation are open source and can be ported to FNA instead of written from scratch (licenses to be checked).

Layers: `OpenMagicka.Content` (XNB v4 + readers + XML parsers) → `OpenMagicka.Engine` (deferred rendering, lights, particles, camera, audio, input) → `OpenMagicka.Game` (spells, entities, AI, triggers, levels, UI) → `OpenMagicka.Launcher` (finds the Steam installation, checks assets). Network: `OpenMagicka.Net` (protocol and transports) and `OpenMagicka.Server` (lobby and relay).

## 5. Detailed specifications

| Document | Topic |
|---|---|
| [formats/XNB_READERS.md](formats/XNB_READERS.md) | binary layouts of all non-standard readers |
| [formats/LEVELS_TRIGGERS.md](formats/LEVELS_TRIGGERS.md) | .lvl, scenes, 24 conditions and 113 actions, dialogs, cutscenes |
| [gameplay/SPELLS.md](gameplay/SPELLS.md) | elements, queue, spell types, damage, statuses, 34 magicks |
| [engine/ARCHITECTURE.md](engine/ARCHITECTURE.md) | threads, render pipeline, shaders, physics, audio, input, saves |
| [engine/ENTITIES_AI.md](engine/ENTITIES_AI.md) | entities, character states, AI, nav mesh, network |
| [engine/RISK_CHECKS.md](engine/RISK_CHECKS.md) | risk checks: shaders on FNA, XACT on FAudio, Linux |
| [adr/0001-stack.md](adr/0001-stack.md) | decision: .NET 10 + FNA stack |
| [adr/0002-network.md](adr/0002-network.md) | decision: network model and transport |

## 6. Key risks

- XACT v45 works on FAudio except xWMA, which has to be transcoded on install ([RISK_CHECKS](engine/RISK_CHECKS.md) §2).
- Original shaders: all 51 work with our FNA3D after patching the bytecode, including the main level material `RenderDeferredEffect`; with the stock FNA3D `ArcaneEffect` crashes inside MojoShader ([RISK_CHECKS](engine/RISK_CHECKS.md) §1).
- FNA does not support `EffectPool`: shared parameters (View/Projection/Time) must be set on every effect.
- The JigLibX license is not confirmed (XNAnimation is Ms-PL).
- Localization/sound/effect keys are the .NET 2.0–3.5 x86 `String.GetHashCode` hash; we need our own implementation.
- Linux/macOS: file lookup must ignore case (code and files disagree on letter case).

Open questions for each subsystem are listed at the end of its document.
