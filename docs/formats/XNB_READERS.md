# XNB v4: binary layouts of Magicka's ContentTypeReaders

This document describes the **exact byte order** of all non-standard readers found in
`Content/` of the Steam version of Magicka 1.10.4.2. It also describes the standard XNA 3.1 readers in the form
in which they are actually written in the files (the XNB v4 format differs from XNA 4.0 / MonoGame v5).

> Clean-room: below is only a description of the format and behavior (field order, types, conditions). The
> original code is not reproduced. Class/field names are given as identifiers.

Verification status: **every** listed reader was verified by a research parser on all
files of the corresponding type: the file is parsed in full (main object + all shared resources),
and the parser checks that exactly `decompressedSize` bytes were consumed (remainder = 0). Details are in §7.

---

## 0. General conventions

### 0.1 Primitives (`BinaryReader` .NET 2.0 / `ContentReader` XNA 3.1)

Byte order is little-endian everywhere.

| Notation | Size | Description |
|---|---|---|
| `u8` / `byte` | 1 | unsigned byte; enums with `byte` as the underlying type |
| `bool` | 1 | 0 or 1 (no other values were found in any file) |
| `u16` / `i32` / `u32` | 2 / 4 / 4 | integers |
| `f32` | 4 | IEEE-754 float |
| `7bit` | 1–5 | 7-bit var-int (low groups first, high bit = "continuation follows") |
| `string` | 7bit + N | length **in bytes** (7bit) + UTF-8 |
| `char` | 1–3 | one UTF-8 character (`BinaryReader.ReadChar` with UTF-8): length from the first byte (`<0x80`→1, `<0xE0`→2, otherwise 3) |
| `Vector2/3/4` | 8/12/16 | floats X,Y(,Z(,W)) |
| `Quaternion` | 16 | X,Y,Z,W |
| `Matrix` | 64 | 16 floats in the order M11,M12,M13,M14,M21,…,M44 (row-major, XNA convention: row 4 = translation) |
| `BoundingSphere` | 16 | Vector3 center + f32 radius |

### 0.2 ContentReader object model

| Operation | Bytes |
|---|---|
| `Object<T>` (ReadObject) | `7bit typeId`; 0 = null; otherwise `typeId-1` is an index into the file's reader table, followed by that reader's data. **The type is determined by the table, not by the expected T** (for example, an `ExternalReferenceReader` can appear in place of `Object<Texture2D>`). |
| `Raw<T>` (value types inside collections) | reader data only, no typeId |
| `ExtRef<T>` (ReadExternalReference) | `string` — asset path **relative to the directory of the current file**, without extension, with `\` (for example `..\..\..\Models\Items_NPC\acc_ammoBelt_0`); empty string = null |
| `SharedRef<T>` (ReadSharedResource) | `7bit`; 0 = null; otherwise a 1-based index into the file's shared resource table; the value is filled in **after** the main object is read (deferred fix-up) |

### 0.3 XNB v4 container (XNA 3.1)

| Offset | Type | Value |
|---|---|---|
| 0 | 3 bytes | `XNB` |
| 3 | u8 | platform: `w` (in all files) |
| 4 | u8 | version: **4** (XNA 3.1); XNA 4.0 uses 5 |
| 5 | u8 | flags: `0x80` = LZX compression. There is no `0x01` bit (HiDef, XNA 4.0). 4299 files are compressed, 9 are not (one of them is an `Item`) |
| 6 | u32 | total file size |
| 10 | u32 | (only if compressed) decompressed data size |
| 10/14 | … | data (LZX: 64 KB window, 32 KB frames; see `tools/research/xnb.py`) |

Decompressed body:

1. `7bit readerCount`, then `readerCount` × (`string` reader name, `i32` reader version). All readers have version = 0. Names are assembly-qualified strings (for example `Magicka.ContentReaders.LevelModelReader, Magicka` or with `Version=1.0.0.0, Culture=neutral`; generics use the syntax ``ListReader`1[[Microsoft.Xna.Framework.Vector3, Microsoft.Xna.Framework, Version=3.1.0.0, …]]``). For matching, it is reliable to take the part before the first comma (for generics, taking `[[…]]` into account).
2. `7bit sharedResourceCount`.
3. `Object` — the main object (with typeId).
4. `sharedResourceCount` × `Object` — shared resources (each with typeId).

The reader table can contain entries that are never used (for example `TextureReader` in models with EffectMaterial — the reader is registered, but no object of that type exists in the stream).

### 0.4 String hash `GetHashCodeCustom`

Many string fields are converted to an `int` hash right at load time (names of effects, sounds, locators,
triggers, template IDs). For ID compatibility the algorithm must be reproduced. It is a fixed
implementation of `String.GetHashCode` from .NET 2.0/3.5 (x86):

- two 32-bit accumulators, both start at `0x15051505` (352654597);
- the string is processed as an array of UTF-16 code units, read **in pairs** as 32-bit words
  (low word = first character); after the last character there is a `\0` in memory, so for a string
  of odd length the last word = (last character, 0);
- on each step (4 characters) the first accumulator: `h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ word0`,
  then, if more than 2 characters remain, the second one in the same way with `word1`; arithmetic right shift,
  32-bit overflow;
- result `h1 + h2 * 1566083941` (mod 2^32, signed int).

Before hashing, the string is often converted to lower case (`ToLowerInvariant`). This is marked
in the tables as "→hash(lower)".

### 0.5 Frequently used enums (how they are stored)

| Enum | Storage | Values |
|---|---|---|
| `Elements` | i32, flags | Earth=1, Water=2, Cold=4, Fire=8, Lightning=0x10, Arcane=0x20, Life=0x40, Shield=0x80, Ice=0x100, Steam=0x200, Poison=0x400. Index 0..10 in resistance arrays is the bit number |
| `AttackProperties` | i32, flags | Damage=1, Knockdown=2, Pushed=4, Knockback=6, Piercing=8, ArmourPiercing=0x10, Status=0x20, Entanglement=0x40, Stun=0x80, Bleed=0x100 |
| `Banks` (XACT bank) | i32, flags | WaveBank=1, Music=2, Ambience=4, UI=8, Spells=0x10, Characters=0x20, Footsteps=0x40, Weapons=0x80, Misc=0x100, Additional=0x200, AdditionalMusic=0x400 |
| `LightType` | i32 | Point=0, Directional=1, Spot=2 (Custom=10 → load error) |
| `LightVariationType` | i32 (in LevelModel) / u8 (in Character/Item/LightEvent) | None, Sine, Flicker, Candle, Strobe |
| `CollisionMaterials` | u8 | Generic, Gravel, Grass, Wood, Snow, Stone, Mud, Reflect, Water, Lava |
| `MovementProperties` | u8, flags | Default=0, Water=1, Jump=2, Fly=4, Dynamic=0x80 |
| `WeaponClass` | u8 | 27 values (Default, Thrust_*, Crush_*, Slash_*, Throw_*, Unarmed, Handgun, Rifle, Machinegun, Heavy, Staff, NewAnimationSet0..7) — animation set index |
| `Animations` | string in the file | enum name (231 values), parsed case-insensitively |

---

## 1. Standard XNA 3.1 readers (as they are stored in the files)

### 1.1 `Microsoft.Xna.Framework.Content.Texture2DReader` (v0)

| # | Type | Field |
|---|---|---|
| 1 | i32 | `SurfaceFormat` (**XNA 3.1** numbering) |
| 2 | u32 | width |
| 3 | u32 | height |
| 4 | u32 | mipCount |
| 5 | mipCount × (`u32 size`, `size` bytes) | levels, from largest to smallest |

Formats found (2296 files + nested): **1 = Color**, **28 = Dxt1**, **32 = Dxt5**.
The sizes confirm this: Color is 4 B/pixel, Dxt1 is 8 B per 4×4 block, Dxt5 is 16 B per block. In XNA 4.0 the same
formats have numbers 0 / 4 / 6 — **this is the main difference from v5**. The byte order of `Color` is **B,G,R,A**
(D3DFMT_A8R8G8B8; verified against the colors of the textures `lava`, `timer_fill_blue/red`). In XNA 4.0 `Color` is
R,G,B,A, so porting to FNA/MonoGame needs a swizzle (or the Bgra32 format).

Full SurfaceFormat 3.1 table (from the XNA 3.1 documentation; only 1/28/32 were found in the files):
Color=1, Bgr32=2, Bgra1010102=3, Rgba32=4, Rgb32=5, Rgba1010102=6, Rg32=7, Rgba64=8, Bgr565=9,
Bgra5551=10, Bgr555=11, Bgra4444=12, Bgr444=13, Bgra2338=14, Alpha8=15, Bgr233=16, Bgr24=17,
NormalizedByte2=18, NormalizedByte4=19, NormalizedShort2=20, NormalizedShort4=21, Single=22,
Vector2=23, Vector4=24, HalfSingle=25, HalfVector2=26, HalfVector4=27, Dxt1=28, Dxt2=29, Dxt3=30,
Dxt4=31, Dxt5=32, Luminance8=33, …

### 1.2 `Texture3DReader` (v0)

`i32 format`, `u32 width`, `u32 height`, `u32 depth`, `u32 mipCount`, then mipCount × (`u32 size`, data).
7 files (`EffectTextures/Particles*`), format 1 (Color), 256×256×1, 9 mips. The layout matches 4.0 (except for the format enum).

### 1.3 `TextureCubeReader` (v0)

`i32 format`, `u32 size`, `u32 mipCount`, then **6 faces** × mipCount × (`u32 size`, data) — face-major
(+X, −X, +Y, −Y, +Z, −Z — CubeMapFace order). 13 files, format 32 (Dxt5).

### 1.4 `VertexDeclarationReader` (v0) — differs from 4.0

| # | Type | Field |
|---|---|---|
| 1 | u32 | elementCount |
| 2 | elementCount × 8 bytes | `u16 stream`, `u16 offset`, `u8 VertexElementFormat`, `u8 VertexElementMethod`, `u8 VertexElementUsage`, `u8 usageIndex` |

The stride is **not stored** in the declaration (in 3.1 it is passed separately — see `BiTreeRootNode.vertexStride`,
`Liquid.stride`; in Model it is computed). In XNA 4.0 the format is different: `u32 stride`, `u32 count`, 16 bytes
per element (`i32 offset`, `i32 format`, `i32 usage`, `i32 usageIndex`) with different enum numbers.

3.1 numbering (confirmed by offsets/sizes in the files):

- `VertexElementFormat`: Single=0, Vector2=1, Vector3=2, Vector4=3, Color=4, Byte4=5, Short2=6, Short4=7, …
- `VertexElementUsage`: Position=0, BlendWeight=1, BlendIndices=2, Normal=3, PointSize=4, TextureCoordinate=5, Tangent=6, Binormal=7, TessellateFactor=8, Color=10, Fog=11, Depth=12, Sample=13
- `VertexElementMethod`: 0 everywhere (Default); `stream` is 0 everywhere.

Combinations found: Position(Vector3), Normal(Vector3), TexCoord(Vector2, indices 0/1), Tangent(Vector3),
Binormal(Vector3), Color(Vector4 or Color), BlendIndices(Byte4) + BlendWeight(Vector4) in skinned
models, Color(Single) in water/lava ("freeze" level).

### 1.5 `VertexBufferReader` (v0) — differs from 4.0

`u32 sizeInBytes` + raw vertices. There is **no** declaration and no vertex count (in 4.0: embedded declaration + `u32 vertexCount` + data).

### 1.6 `IndexBufferReader` (v0)

`bool is16Bit`, `u32 sizeInBytes`, data. In all files is16Bit = true. Matches 4.0.

### 1.7 `EffectReader` (v0)

`u32 size` + a DX9 Effect binary (`fx_2_0`, starts with `01 09 FF FE`). 52 files (`Content/Shaders/*.xnb`
and `SkinnedModelBasicEffect.xnb`). The layout matches 4.0, but MonoGame expects its own shader format; FNA
(MojoShader) can execute this bytecode directly.

### 1.8 `ModelReader` (v0) — differs significantly from 4.0

| # | Type | Field |
|---|---|---|
| 1 | u32 | boneCount |
| 2 | boneCount × (`Object<string>` name, `Matrix` transform) | bones |
| 3 | boneCount × (`BoneRef` parent, `u32` childCount, childCount × `BoneRef`) | hierarchy |
| 4 | u32 | **vertexDeclarationCount** |
| 5 | vertexDeclarationCount × `Object<VertexDeclaration>` | model declaration table |
| 6 | u32 | meshCount |
| 7 | meshCount × Mesh | see below |
| 8 | `BoneRef` | root bone |
| 9 | `Object` | Tag (null in all files) |

`BoneRef`: `u8` if boneCount < 255, otherwise `u32`; 0 = no bone, otherwise index+1.

Mesh:

| # | Type | Field |
|---|---|---|
| 1 | `Object<string>` | name |
| 2 | `BoneRef` | parentBone |
| 3 | BoundingSphere | bounds |
| 4 | `Object<VertexBuffer>` | **shared VB of the mesh** |
| 5 | `Object<IndexBuffer>` | **shared IB of the mesh** |
| 6 | `Object` | Tag (null) |
| 7 | u32 | partCount |
| 8 | partCount × MeshPart | |

MeshPart:

| # | Type | Field |
|---|---|---|
| 1 | u32 | streamOffset (bytes) |
| 2 | u32 | baseVertex |
| 3 | u32 | numVertices |
| 4 | u32 | startIndex |
| 5 | u32 | primitiveCount |
| 6 | u32 | **declaration index** into the table from item 5 |
| 7 | `Object` | Tag (null) |
| 8 | `SharedRef<Effect>` | effect (stored in the file's shared resources) |

The vertex stride of a part = the size of its declaration (sum of element sizes). In 4.0 there is no declaration table,
VB/IB are shared resources of each part, and parts have the fields `vertexOffset/numVertices/startIndex/primitiveCount`.

Effects found in Model shared resources: `RenderDeferredEffect` (static models, items,
level parts), `AdditiveEffect`, `SkinnedModelBasicEffect` / `SkinnedModelDeferredNormalMappedEffect`
(inside SkinnedModel), standard `BasicEffect` (6 files) and `EffectMaterial` (3 files, placeholder assets
from Maya).

### 1.9 `BasicEffectReader` (v0)

`ExtRef<Texture>` texture, `Vector3` diffuseColor, `Vector3` emissiveColor, `Vector3` specularColor,
`f32` specularPower, `f32` alpha, `bool` vertexColorEnabled. (The order is confirmed by plausible
values: alpha=1, specularPower=16/64.)

### 1.10 `EffectMaterialReader` (v0)

`ExtRef<Effect>` + `Object<Dictionary<string,object>>` of parameters. Dictionary values are `SingleReader`,
`BooleanReader`, `Vector3Reader`, `MatrixReader`, `ExternalReferenceReader` (textures). Used
in 3 files (`Models/AnimatedProps/Dungeons/*`), the references point to `..\..\..\Shaders\Maya\SkinnedModelEffect_0`.

### 1.11 Other standard readers

| Reader | Bytes |
|---|---|
| `StringReader` | `string` |
| `SingleReader` / `BooleanReader` / `Vector3Reader` / `MatrixReader` | the corresponding primitive |
| `ExternalReferenceReader` | `string` (path, as in ExtRef). Appears as the value of `Object<T>` when the asset is stored in a separate file |
| ``ListReader`1[Vector3]`` | `u32 count` + count × Vector3 (**without** typeId — value type) |
| ``ListReader`1[String]`` | `u32 count` + count × `Object<string>` (with typeId). Only `Audio/SoundList.xnb` |
| ``DictionaryReader`2[String,Object]`` | `u32 count` + count × (`Object` key, `Object` value) |
| `TextureReader` | present in the tables, but there are no objects of this type in the data |

---

## 2. XNAnimation 0.7 (`XNAnimation.Pipeline.*`)

### 2.1 `SkinnedModelReader` (v0) — main type of 327 files

| # | Type | Field |
|---|---|---|
| 1 | `Object<Model>` | a regular XNA 3.1 Model (§1.8); part effects are `SkinnedModelBasicEffect` or `SkinnedModelDeferredNormalMappedEffect` in shared resources |
| 2 | i32 | boneCount |
| 3 | boneCount × `SharedRef<SkinnedModelBone>` | skeleton (order = bone index) |
| 4 | i32 | clipCount |
| 5 | clipCount × `SharedRef<AnimationClip>` | clips, dictionary key = AnimationClip.Name |

The `*_animations*.xnb` files contain both the mesh and the clips; "mesh" files can have clipCount = 0.

### 2.2 `SkinnedModelBoneReader` (v0) (shared resource)

| # | Type | Field |
|---|---|---|
| 1 | u16 | index |
| 2 | string | name |
| 3 | Vector3 | bindPose.Translation |
| 4 | Quaternion | bindPose.Orientation |
| 5 | Vector3 | bindPose.Scale |
| 6 | Matrix | inverseBindPoseTransform |
| 7 | `SharedRef<SkinnedModelBone>` | parent (0 for the root) |
| 8 | i32 | childCount |
| 9 | childCount × `SharedRef<SkinnedModelBone>` | children |

### 2.3 `AnimationClipReader` (v0) (shared resource)

| # | Type | Field |
|---|---|---|
| 1 | string | name |
| 2 | f32 | duration (seconds) |
| 3 | i32 | channelCount |
| 4 | channelCount × (`string` boneName, AnimationChannel) | |

**AnimationChannel** (the same layout is used in `AnimatedLevelPart`): `i32 keyframeCount`, then
keyframeCount × (`f32 time`, `Vector3 translation`, `Quaternion orientation`, `Vector3 scale`) — 44 bytes
per key. Keys are looked up by time with a binary search; keys are sorted by time.

### 2.4 `SkinnedModelBasicEffectReader` (v0) (shared resource)

| # | Type | Field |
|---|---|---|
| 1 | u8 | technique (enum `SkinnedModelBasicEffect.Technique`) |
| 2 | f32 | EmissiveAmount |
| 3 | Vector3 | DiffuseColor |
| 4 | f32 | SpecularAmount |
| 5 | f32 | SpecularPower |
| 6 | f32 | Diffuse1Alpha |
| 7 | bool | UseSoftLightBlend |
| 8–13 | 6 × bool | DiffuseMap0Enabled, DiffuseMap1Enabled, SpecularMapEnabled, DamageMap0Enabled, DamageMap1Enabled, NormalMapEnabled |
| 14–19 | 6 × `Object<Texture2D>` | DiffuseMap0, DiffuseMap1, SpecularMap, DamageMap0, DamageMap1, NormalMap |

Important: the textures here are `Object`, not ExtRef. In the files, their place holds the typeId of
`ExternalReferenceReader` + a path (or 0 = null), so in practice these are also external references, but with a typeId.
Creation requires a global `EffectPool` (the shader is taken from `SkinnedModelBasicEffect.xnb`).

---

## 3. PolygonHead (`PolygonHead.Pipeline.*`)

### 3.1 `RenderDeferredEffectReader` (v0) — main material of static geometry

| # | Type | Field |
|---|---|---|
| 1 | f32 | Alpha |
| 2 | f32 | Sharpness |
| 3 | bool | VertexColorEnabled |
| 4 | bool | UseMaterialTextureForReflectiveness |
| 5 | `ExtRef<TextureCube>` | ReflectionMap |
| 6 | Layer0 | see below |
| 7 | bool | hasLayer1 |
| 8 | Layer1 | only if hasLayer1 |

Layer (suffix 0/1): `bool DiffuseTextureNAlphaDisabled`, `bool AlphaMaskNEnabled`, `Vector3 DiffuseColorN`,
`f32 SpecAmountN`, `f32 SpecPowerN`, `f32 EmissiveAmountN`, `f32 NormalPowerN`, `f32 ReflectivenessN`,
`ExtRef<Texture2D> DiffuseTextureN`, `ExtRef<Texture2D> MaterialTextureN`, `ExtRef<Texture2D> NormalTextureN`.
The second layer is for blending two materials by vertex color. World = Identity after loading.

### 3.2 `RenderDeferredLiquidEffectReader` (v0) — water

| # | Type | Field |
|---|---|---|
| 1 | `ExtRef<Texture>` | ReflectionMap (a TextureCube is expected; otherwise null) |
| 2 | f32 | WaveHeight |
| 3 | Vector2 | WaveSpeed0 |
| 4 | Vector2 | WaveSpeed1 |
| 5 | f32 | WaterReflectiveness |
| 6 | Vector3 | BottomColor |
| 7 | Vector3 | DeepBottomColor |
| 8 | f32 | WaterEmissiveAmount |
| 9 | f32 | WaterSpecAmount |
| 10 | f32 | WaterSpecPower |
| 11 | ExtRef<Texture2D> | BottomTexture |
| 12 | ExtRef<Texture2D> | WaterNormalMap |
| 13 | f32 | IceReflectiveness |
| 14 | Vector3 | IceColor |
| 15 | f32 | IceEmissiveAmount |
| 16 | f32 | IceSpecAmount |
| 17 | f32 | IceSpecPower |
| 18 | ExtRef<Texture2D> | IceDiffuseMap |
| 19 | ExtRef<Texture2D> | IceNormalMap |

Found only inside LevelModel (Liquid), and also as the effect of model parts in AnimatedLevelPart.

### 3.3 `LavaEffectReader` (v0) — lava (4 levels: `ru_s5`, `ru_s6BF`, `ew_s4`, `ch_volcano_hideout`)

| # | Type | Field |
|---|---|---|
| 1 | f32 | MaskDistortion |
| 2 | Vector2 | Speed0 |
| 3 | Vector2 | Speed1 |
| 4 | f32 | LavaHotEmissiveAmount |
| 5 | f32 | LavaColdEmissiveAmount |
| 6 | f32 | LavaSpecAmount |
| 7 | f32 | LavaSpecPower |
| 8 | f32 | TempFrequency |
| 9 | ExtRef<Texture2D> | ToneMap |
| 10 | ExtRef<Texture2D> | TempMap |
| 11 | ExtRef<Texture2D> | MaskMap |
| 12 | Vector3 | RockColor |
| 13 | f32 | RockEmissiveAmount |
| 14 | f32 | RockSpecAmount |
| 15 | f32 | RockSpecPower |
| 16 | f32 | RockNormalPower |
| 17 | ExtRef<Texture2D> | RockTexture |
| 18 | ExtRef<Texture2D> | RockNormalMap |

### 3.4 `AdditiveEffectReader` (v0)

`Vector3 ColorTint` (W forced to 1), `bool VertexColorEnabled`, `bool TextureEnabled`, `ExtRef<Texture2D> Texture`.
Found as the effect of BiTree roots and Model parts (additive objects are drawn in a separate pass).

### 3.5 `SkinnedModelDeferredNormalMappedEffectReader` (v0)

`Vector3 DiffuseColor`, `f32 SpecularAmount`, `f32 SpecularPower`, `f32 EmissiveAmount`, `f32 NormalPower`,
then 5 × `ExtRef<Texture2D>`: DiffuseMap, MaterialMap, DamageMap, NormalMap, NormalDamageMap.
OverrideColor is reset to zero. Only 4 files (`Models/Bosses/cthulhu/*`). Requires a global EffectPool.

(PolygonHead also has `SkinnedModelDeferredEffect` with its own read method, but there is no reader for it
in the content — it is not used.)

### 3.6 `BiTreeModelReader` (v0) — static level geometry (BSP-like AABB tree)

| # | Type | Field |
|---|---|---|
| 1 | i32 | rootCount |
| 2 | rootCount × BiTreeRootNode | |

BiTreeRootNode (one per material/batch):

| # | Type | Field |
|---|---|---|
| 1 | bool | Visible |
| 2 | bool | CastShadows |
| 3 | f32 | Sway (amplitude of vegetation sway) |
| 4 | f32 | EntityInfluence (sway reaction to entities) |
| 5 | f32 | GroundLevel |
| 6 | i32 | numVertices |
| 7 | i32 | vertexStride (bytes; found 44, 60, 64, 72, 80, 96) |
| 8 | `Object<VertexDeclaration>` | |
| 9 | `Object<VertexBuffer>` | |
| 10 | `Object<IndexBuffer>` | |
| 11 | `Object<Effect>` | `RenderDeferredEffect` or `AdditiveEffect` (inline, not shared) |
| 12 | i32 | primitiveCount (of the whole node) |
| 13 | i32 | startIndex |
| 14 | Vector3 | bbox.Min |
| 15 | Vector3 | bbox.Max |
| 16 | bool | hasChildA → BiTreeNode |
| 17 | bool | hasChildB → BiTreeNode |

BiTreeNode (recursive, in order: A fully, then B): `i32 primitiveCount`, `i32 startIndex`,
`Vector3 min`, `Vector3 max`, `bool hasChildA` [+node], `bool hasChildB` [+node]. numVertices is inherited from the root.

Measured on all 132 levels: the deepest tree has depth 8 (OpenMagicka rejects depth > 32).

Behavior: during rendering, if a node is fully inside the frustum, its whole index range is drawn; if it
intersects and has both children, the renderer recurses; otherwise it is drawn in full. So the parent range covers
the children's ranges (indices are ordered by the tree). `TriangleList` is drawn, baseVertex = 0.

### 3.7 `BitmapFontReader` (v0) — 66 files `Languages/<lang>/Font/*.xnb`

| # | Type | Field |
|---|---|---|
| 1 | i32 | LineHeight |
| 2 | i32 | BaseLine |
| 3 | char | DefaultCharacter (UTF-8) |
| 4 | `Object<Texture2D>` | atlas (inline Texture2D, Color format) |
| 5 | i32 | glyphCount (up to 263 in the files) |
| 6 | glyphCount × Glyph | |
| 7 | i32 | kerningCount (> 0 in some fonts) |
| 8 | kerningCount × (`char` first, `char` second, `i32` amount) | |

Glyph: `char Character`, `i32 Origin.X`, `i32 Origin.Y`, `i32 Size.X`, `i32 Size.Y`, `i32 AdvanceWidth`,
`i32 LeftSideBearing`, `bool ForceWhite`. Origin/Size are pixels in the atlas. Spacing = 0. If a
character is missing, DefaultCharacter is used, then any first one.

---

## 4. Magicka: `LevelModelReader` (132 files `Levels/**/*.xnb`)

Main object of a level scene. Fields strictly in order:

| # | Type | Field |
|---|---|---|
| 1 | `Object<BiTreeModel>` | static geometry (§3.6) |
| 2 | i32 + N × AnimatedLevelPart | animated parts (recursive) |
| 3 | i32 + N × LevelLight | light sources |
| 4 | i32 + N × VisualEffectStorage | particle effects on the level |
| 5 | i32 + N × (`Matrix` startTransform, `string` template) | physics objects; the template is loaded as `Data/PhysicsEntities/<template>` |
| 6 | i32 + N × Liquid | water/lava |
| 7 | i32 + N × ForceField | force fields |
| 8 | **10** × CollisionMeshSlot | static collision, one slot per material |
| 9 | bool + CameraMesh | optional camera collision |
| 10 | i32 + N × (`string` name, TriggerArea) | trigger zones; key = hash(lower(name)); the engine additionally adds an "any" zone |
| 11 | i32 + N × (`string` name, Locator) | locators; key = hash(name) (without lower) |
| 12 | NavMesh | navigation mesh (always) |

The file's shared resources are the effects (`RenderDeferredEffect`/`AdditiveEffect`) of Model parts inside AnimatedLevelPart.

### 4.1 LevelLight

| # | Type | Field |
|---|---|---|
| 1 | string | name (key = hash(name)) |
| 2 | Vector3 | position |
| 3 | Vector3 | direction |
| 4 | i32 | LightType (0 point, 1 directional, 2 spot) |
| 5 | i32 | LightVariationType |
| 6 | f32 | radius (point) / range (spot) |
| 7 | bool | useAttenuation (spot) |
| 8 | f32 | cutoffAngle (spot) |
| 9 | f32 | sharpness (spot) |
| 10 | Vector3 | DiffuseColor |
| 11 | Vector3 | AmbientColor |
| 12 | f32 | SpecularAmount |
| 13 | f32 | VariationSpeed |
| 14 | f32 | VariationAmount |
| 15 | i32 | ShadowMapSize (adjusted by the shadow quality setting) |
| 16 | bool | CastShadows (for a directional light created from the level, the constructor sets false, but the field is then overwritten with this value) |

Fields 6–9 are always read, regardless of the type.

### 4.2 VisualEffectStorage (level and AnimatedLevelPart)

`string id` (→hash(lower)), `Vector3 position`, `Vector3 forward`, `f32 range`, `string effect`
(→hash; in LevelModel without lower, in AnimatedLevelPart with lower). Transform = `CreateWorld(position, forward, up=+Y)`.
`effect` is the name of an XML effect from `Content/Effects`.

### 4.3 AnimatedLevelPart (recursive)

| # | Type | Field |
|---|---|---|
| 1 | string | name (→lower, ID = hash) |
| 2 | bool | AffectShields |
| 3 | `Object<Model>` | part model (part effects are Additive/RenderDeferred/RenderDeferredLiquid, otherwise an error) |
| 4 | i32 + N × (`string` meshName, `bool`, `bool`) | MeshSettings: a pair of flags per mesh (by usage — visibility and shadow casting; not verified). Combinations in the levels: (true, true) 1222, (false, true) 9, (true, false) 1 |
| 5 | i32 + N × Liquid | liquids attached to the part |
| 6 | i32 + N × (`string` name, Locator) | part locators |
| 7 | f32 | animationDuration |
| 8 | AnimationChannel | part animation (§2.3, one channel) |
| 9 | i32 + N × VisualEffectStorage | |
| 10 | i32 + N × (`string` lightName, `Matrix` localTransform) | references to level light sources (by name hash) |
| 11 | bool hasCollision → (`u8` CollisionMaterials, `Object<List<Vector3>>` vertices, TriangleList) | part collision |
| 12 | bool hasNavMesh → AnimatedNavMesh | (layout = NavMesh) |
| 13 | i32 + N × AnimatedLevelPart | child parts (key = hash(name)) |

### 4.4 Liquid (water/lava)

| # | Type | Field |
|---|---|---|
| 1 | `Object<Effect>` | `RenderDeferredLiquidEffect` → Water, `LavaEffect` → Lava; anything else is an error |
| 2 | `Object<VertexBuffer>` | |
| 3 | `Object<IndexBuffer>` | |
| 4 | `Object<VertexDeclaration>` | the engine adds an element to it: stream 1, Single, Color index 1 (dynamic "freeze" buffer) |
| 5 | i32 | vertexStride |
| 6 | i32 | numVertices |
| 7 | i32 | primitiveCount |
| 8 | bool | hasCollision (IceMesh + WaterMesh are created from VB positions and IB indices) |
| 9 | bool | Freezable |
| 10 | bool | AutoFreeze (all vertices are initially frozen = 1.0) |

Collision is built from the VB/IB themselves: positions are taken at the offset of the Position element, and a
float is also read at the offset of the Color element (usage 10). The triangle index order is
permuted when building the collision (0, 2, 1).

### 4.5 ForceField

| # | Type | Field |
|---|---|---|
| 1 | Vector3 | Color |
| 2 | f32 | Width |
| 3 | f32 | AlphaPower |
| 4 | f32 | AlphaFalloffPower |
| 5 | f32 | MaxRadius |
| 6 | f32 | RippleDistortion |
| 7 | f32 | MapDistortion |
| 8 | bool | VertexColorEnabled |
| 9 | ExtRef<Texture2D> | DisplacementMap |
| 10 | f32 | TTL |
| 11–13 | Object VB, Object IB, Object VertexDeclaration | |
| 14–16 | i32 ×3 | vertexStride, numVertices, primitiveCount |

Positions must be Vector3 with usageIndex 0. The levels of the Steam release contain 5 force fields,
and the OpenMagicka reader consumes every level with no bytes left, so the layout is confirmed by data.

### 4.6 CollisionMeshSlot (×10) and CameraMesh

Slot: `bool present`; if true — `Object<List<Vector3>> vertices`, `i32 triangleCount`,
triangleCount × (`i32 I0`, `i32 I1`, `i32 I2`). Slots 0..9 become primitives of one CollisionSkin;
the slot index is the primitive index that character physics uses to determine the surface material,
so slot = `CollisionMaterials` (Generic, Gravel, Grass, Wood, Snow, Stone, Mud, Reflect, Water, Lava).
Example `bf_s1`: slots 0, 3, 4, 5 are filled. Levels using each slot: Generic 110, Gravel 28, Grass 49,
Wood 35, Snow 9, Stone 69, Mud 8, Reflect 4, Water 0, Lava 0 (water and lava collision comes from the
Liquid entries instead). Every level has exactly one directional light.

CameraMesh: `bool present` + the same layout (vertices + triangles).

### 4.7 TriggerArea

`Vector3 position`, `Vector3 sideLengths`, `Quaternion orientation` → JigLibX `Box(position, R(orientation), sideLengths)`.
**position is a corner of the box**, not the center (center = position + R·sideLengths/2). An entity is "inside" if
the distance from its position to the box is ≤ its radius.

### 4.8 Locator

`Matrix transform`, `f32 radius` (the name is read before it, outside).

### 4.9 NavMesh / AnimatedNavMesh

| # | Type | Field |
|---|---|---|
| 1 | u16 | vertexCount |
| 2 | vertexCount × Vector3 | vertices |
| 3 | u16 | triangleCount |
| 4 | triangleCount × Triangle (25 bytes) | |

Triangle: `u16 VertexA, VertexB, VertexC`, `u16 NeighbourA, NeighbourB, NeighbourC` (indices of neighboring
triangles; 0xFFFF = no neighbor — confirmed: 16174 such values in the 41912 triangles of all levels and nested
parts, and every other value is a valid triangle index), `f32 CostAB, CostBC, CostCA`, `u8 MovementProperties`.
Triangles with the Dynamic flag (0x80) get dynamic neighbors (joining with animated navmeshes).
Limit of 65535 vertices/triangles (example `ru_s5`: 6818 vertices, 12577 triangles).

---

## 5. Magicka: entity templates

### 5.1 `CharacterTemplateReader` (338 files `Data/Characters/*.xnb`)

| # | Type | Field |
|---|---|---|
| 1 | string | ID (→lower, hash) |
| 2 | string | DisplayID (→lower, hash) — localization key |
| 3 | i32 | Factions (flags) |
| 4 | i32 | BloodType (regular, green, black, wood, insect, none) |
| 5–11 | 7 × bool | IsEthereal, LooksEthereal, Fearless, Uncharmable, Nonslippery, HasFairy, CanSeeInvisible |
| 12 | i32 + N × (`string` cue →lower hash, `i32` Banks) | AttachedSounds. **Only the first 4 are stored; with N > 4 the original stops reading (desynchronization) — in the data N ≤ 4** |
| 13 | i32 + N × Gib | Gib: `ExtRef<Model>`, `f32 mass`, `f32 scale` |
| 14 | i32 + N × PointLightHolder (N ≤ 4, otherwise an error) | `string jointName`, `f32 radius`, `Vector3 diffuse`, `Vector3 ambient`, `f32 specular`, `u8 LightVariationType`, `f32 variationAmount`, `f32 variationSpeed` |
| 15 | f32 | MaxHitpoints |
| 16 | i32 | NumberOfHealthBars (≤0 → 1) |
| 17 | bool | Undying |
| 18 | f32 | UndieTime |
| 19 | f32 | UndieHitPoints |
| 20 | i32 | HitTolerance |
| 21 | f32 | KnockdownTolerance |
| 22 | i32 | ScoreValue |
| 23 | i32 | ExperienceValue |
| 24 | bool | RewardOnKill |
| 25 | bool | RewardOnOverkill |
| 26 | i32 | Regeneration |
| 27 | f32 | MaxPanic |
| 28 | f32 | ZapModifier |
| 29 | f32 | Length (capsule; min 0.01) |
| 30 | f32 | Radius |
| 31 | f32 | Mass |
| 32 | f32 | Speed |
| 33 | f32 | TurnSpeed |
| 34 | f32 | BleedRate |
| 35 | f32 | StunTime |
| 36 | i32 | SummonElementBank (Banks) |
| 37 | string | SummonElementCue (hash) |
| 38 | i32 + N × Resistance | `i32 Elements`, `f32 Multiplier`, `f32 Modifier`, `bool StatusResistance`; by default all 11 = (1, 0, false) |
| 39 | i32 + N × ModelProperties | `ExtRef<SkinnedModel>`, `f32 scale`, `Vector3 tint` (appearance variants) |
| 40 | `ExtRef<SkinnedModel>` | file with the skeleton and animation clips |
| 41 | i32 + N × (`string` boneName →lower, `string` effect →lower hash) | AttachedEffects (the bone must exist) |
| 42 | **27** × AnimationSet | one per WeaponClass |
| 43 | i32 + N × (`i32 slot`, Attachment) | Equipment, 8 slots |
| 44 | ConditionCollection | character events (§5.4) |
| 45–57 | 13 × f32 | AlertRadius, GroupChase, GroupSeparation, GroupCohesion, GroupAlignment, GroupWander, FriendlyAvoidance, EnemyAvoidance, SightAvoidance, DangerAvoidance, AngerWeight, DistanceWeight, HealthWeight |
| 58 | bool | Flocking |
| 59 | f32 | BreakFreeStrength |
| 60 | i32 + N × Ability | AI abilities (§5.6) |
| 61 | i32 + N × (`u8 MovementProperties`, `i32 count`, count × `string` Animations) | MoveAnimations; MoveAbilities = OR of all flags |
| 62 | i32 + N × BuffStorage | (§5.7) |
| 63 | i32 + N × AuraStorage | they go when their TTL runs out (ENTITIES_AI §8.3.2) |

Behavior at load time (not format, but important): for characters without the FRIENDLY flag, resistance
multipliers/modifiers are scaled by the number of players (`k = 1 − (players−1)·0.00666`; positive ×k, negative ÷k).
The bones `Mouth`, `RightAttach`, `LeftAttach`, `RightLeg`, `LeftLeg` and the bone with index 1 (hip) are looked up by name
case-insensitively; the bind poses of the joints are rotated by 180° around Y.

**AnimationSet**: `i32 count`, count × (`string` Animations name, AnimationClipAction).

**AnimationClipAction**: `string clipName` (key in the SkinnedModel clips), `f32 animationSpeed`, `f32 blendTime`,
`bool loop`, `i32 actionCount`, actionCount × AnimationAction.

**AnimationAction**: `string typeName` (class name, case-insensitive), `f32 startTime`, `f32 endTime`
(fractions of the clip duration 0..1), then the type's fields:

| Type | Extra fields |
|---|---|
| Block | `i32 weapon` |
| BreakFree | `f32 magnitude`, `i32 weapon` |
| CameraShake | `string` (ignored), `f32 duration`, `f32 magnitude` |
| CastSpell | `bool fromStaff`; if false — `string source` (bone) |
| Crouch | `f32 radius`, `f32 length` (≤0 → 0.01) |
| DamageGrip | `bool damageToOwner`, `i32 n` (≤5), n × Damage(f32 amount) |
| DealDamage | `i32 weapon`, `u8 Targets` (Target=0, Friendly=1, Enemy=2, NonCharacters=4, All=255) |
| DetachItem | `i32 item`, `Vector3 velocity` |
| Ethereal | `bool ethereal`, `f32 alpha`, `f32 speed` |
| Footstep, OverkillGrip, ReleaseGrip, ThrowGrip | — |
| Grip | `u8 GripType` (Pickup, Ride, Hold), `f32 radius`, `f32 breakFreeTolerance` (cast to int; 0 → 10), `string gripAttachBone`, `string targetAttachBone`, `bool finishOnGrip` |
| Gunfire | `i32 weapon`, `f32 accuracy` |
| Immortal | `bool collide` |
| Invisible | `bool noEffect` |
| Jump | `f32 elevation` (degrees), `bool` [+`f32 minRange`], `bool` [+`f32 maxRange`] |
| Move | `Vector3 velocity` |
| PlayEffect | `string bone`, `bool attach`, `string effect` (→lower hash), `f32` (ignored) |
| PlaySound | `string cue` (hash), `i32 Banks` |
| RemoveStatus | `string` StatusEffects name |
| SetItemAttach | `i32 item`, `string bone` |
| SpawnMissile | `i32 weapon`, `Vector3 velocity` (length² ≤ 1e-6 → none), `bool itemAligned` |
| SpecialAbility | `i32 weapon`; if < 0 — SpecialAbility (§5.5) |
| Suicide | `bool overkill` |
| Tongue | `f32 maxLength` |
| WeaponVisibility | `i32 weapon`, `bool visible` |

**Attachment** (equipment): `string boneName` (must exist), `Vector3 rotation` (X — rotX,
Y — rotY + π, Z — again **rotX**; this is how the original does it), `ExtRef<Item>`.

**Damage** (in many places): `i32 AttackProperties`, `i32 Elements`, amount, `f32 magnitude`, where amount is
`f32`, except in places marked "Damage(i32)": there amount is stored as `i32`.

### 5.2 `ItemReader` (454 files `Data/Items/**`)

| # | Type | Field |
|---|---|---|
| 1 | string | name (type = hash(name), case is preserved) |
| 2 | string | displayName (hash; localization key) |
| 3 | string | description (hash) |
| 4 | i32 + N × (`string` cue →lower hash, `i32` Banks) | sounds |
| 5 | bool | Pickable |
| 6 | bool | Bound |
| 7 | i32 | BlockValue |
| 8 | u8 | WeaponClass |
| 9 | f32 | CooldownTime |
| 10 | bool | HideModel |
| 11 | bool | HideEffect |
| 12 | bool | PauseSounds |
| 13 | i32 + N × Resistance (with bool) | |
| 14 | u8 | PassiveAbility (None, ShieldBoost, AreaLifeDrain, ZombieDeterrent, ReduceAggro, EnhanceAllyMelee, AreaRegeneration, InverseArcaneLife, Zap, BirchSteam, WetLightning, MoveSpeed, Glow, Mjolnr, Gungner, MasterSword, DragonSlayer) |
| 15 | f32 | PassiveAbility variable |
| 16 | i32 + N × string | effects (→lower hash) |
| 17 | i32 + N × PointLight (N ≤ 1) | `f32 radius`, `Vector3 diffuse`, `Vector3 ambient`, `f32 specular`, `u8 variation`, `f32 varAmount`, `f32 varSpeed` |
| 18 | bool hasSpecial → (`f32 rechargeTime`, SpecialAbility) | |
| 19 | f32 | MeleeRange |
| 20 | bool | MeleeMultiHit |
| 21 | ConditionCollection | MeleeConditions |
| 22 | f32 | RangedRange |
| 23 | bool | Facing |
| 24 | f32 | Homing |
| 25 | f32 | RangedElevation (degrees) |
| 26 | f32 | RangedDanger |
| 27 | f32 | GunRange |
| 28 | i32 | GunClip |
| 29 | i32 | GunRate |
| 30 | f32 | GunAccuracy |
| 31 | string | GunSound: `""`, `cue` (Weapons bank) or `Bank/cue` |
| 32 | string | GunMuzzleEffect (hash, `""` = 0) |
| 33 | string | GunShellsEffect (hash) |
| 34 | f32 | TracerVelocity |
| 35 | string | NonTracerSprite: `""` = −1, `N` or `X/N` where X∈{B,C,D,E} adds 64/128/192/256 |
| 36 | string | TracerSprite (same syntax) |
| 37 | ConditionCollection | GunConditions |
| 38 | ExtRef<Model> | ProjectileModel |
| 39 | ConditionCollection | RangedConditions |
| 40 | f32 | Scale |
| 41 | ExtRef<Model> | Model (attachment points are taken from its bones `attach0/attach1/effect0/effect1`; bbox is from the positions of the first part) |
| 42 | i32 + N × AuraStorage | auras |

### 5.3 `PhysicsEntityTemplateReader` (69 files `Data/PhysicsEntities/**`, `Data/AnimatedProps/**`)

| # | Type | Field |
|---|---|---|
| 1 | bool | Movable |
| 2 | bool | Pushable |
| 3 | bool | Solid |
| 4 | f32 | Mass |
| 5 | i32 | MaxHitPoints (≤0 → indestructible) |
| 6 | bool | CanHaveStatus |
| 7 | i32 + N × (`i32 Elements`, `f32 Multiplier`, `f32 Modifier`) | resistances **without** bool StatusResistance |
| 8 | i32 + N × Gib | as for a character |
| 9 | string | HitEffect (→lower hash) |
| 10 | string | HitSound: `""`, `cue` or `Bank/cue` |
| 11 | string | GibTrailEffect (→lower hash) |
| 12 | `Object<Model>` | inline Model (part effects in shared resources) |
| 13 | bool hasMesh → (`Object<List<Vector3>>`, `i32 n`, n × 3×i32) | collision trimesh |
| 14 | i32 boxCount + boxCount × (`string` name, `Vector3 position`, `Vector3 sides`, `Quaternion orientation`) | only the first box is used; the rest are read and discarded |
| 15 | i32 + N × (`string` effect →hash, `Matrix` transform) | effects |
| 16 | i32 | lightCount — **must be 0** (otherwise NotImplemented) |
| 17 | ConditionCollection | |
| 18 | string | ID (→lower, hash) |
| 19 | bool | hasAnimation — read inside try/catch: if the stream has ended, it is treated as false (present and = true in all 69 files) |
| 20 | (if 19) f32 | Radius |
| 21 | i32 + N × ModelProperties | `ExtRef<SkinnedModel>`, `f32 scale`, `Vector3 tint` |
| 22 | bool hasSkeleton → `ExtRef<SkinnedModel>` | skeleton/clips |
| 23 | i32 + N × (`string` bone, `string` effect) | AttachedEffects |
| 24 | 27 × AnimationSet | as for a character |

The template path is taken from AssetName (the part after `content\`, in lower case). For events with the condition
Hit/Collision the original intended to reduce the magnitude of Damage events by a factor of 4, but it indexes the wrong element
(it uses the condition index instead of the event index) — a side effect, not format.

### 5.4 ConditionCollection / EventCollection / EventStorage

ConditionCollection: `i32 n` (**≤ 5**, fixed-size array), n × EventCollection.

EventCollection:

| # | Type | Field |
|---|---|---|
| 1 | u8 | EventConditionType (flags: Default=1, Hit=2, Collision=4, Damaged=8, Timer=0x10, Death=0x20, OverKill=0x40) |
| 2 | i32 | Hitpoints |
| 3 | i32 | Elements |
| 4 | f32 | Threshold |
| 5 | f32 | Time |
| 6 | bool | Repeat |
| 7 | i32 + N × EventStorage | |

EventStorage: `u8 EventType`, then:

| EventType | Fields |
|---|---|
| 0 Damage | Damage(f32), `bool velocityBased` |
| 1 Splash | Damage(**i32** amount), `f32 radius` (must be > 0) |
| 2 Sound | `i32 Banks`, `string cue` (hash), `f32 magnitude`, `bool stopOnRemove` |
| 3 Effect | `bool follow`, `bool worldAlign`, `string effect` (→lower hash) |
| 4 Remove | `i32 bounce` |
| 5 CameraShake | `f32 ttl`, `f32 magnitude`, `bool fromPosition` |
| 6 Decal | `i32 a`, `i32 b` (Decal = a + 8·b), `i32 scale` (TTL = 60 s) |
| 7 Blast | not implemented (load error) |
| 8 Spawn | `string characterType` (hash; the template `Data/Characters/<type>` is loaded), `string idleAnim`, `string spawnAnim`, `f32 health`, `u8 Order`, `u8 ReactTo`, `u8 Reaction` (Order), `f32 rotation` (degrees), `Vector3 offset` |
| 9 Overkill | — |
| 10 SpawnGibs | `i32 startIndex`, `i32 endIndex` |
| 11 SpawnItem | `string item` (→lower hash; `Data/Items/Wizard/<item>` is loaded, except for "random") |
| 12 SpawnMagick | `string` MagickType name (`random` → Revive) |
| 13 SpawnMissile | `string type` (→lower hash), `Vector3 velocity`, `bool facing` |
| 14 Light | `f32 radius`, `Vector3 diffuse`, `Vector3 ambient`, `f32 specular`, `u8 variation`, `f32 varAmount`, `f32 varSpeed` |
| 15 CastMagick | `string` MagickType name, `i32 n`, n × `i32 Elements` |
| 16 DamageOwner | Damage(f32), `bool velocityBased` |
| 17 Callback | load error (code only) |

### 5.5 SpecialAbility (on items and in the SpecialAbility AnimationAction)

`string typeName` (class from `Abilities.SpecialAbilities`), `string animation` (`""` → cast_self), `string effect`
(→lower hash, optional), `i32 n`, n × `i32 Elements`. The constructor is chosen based on the presence of elements/effect.

### 5.6 Ability (character AI abilities)

Common part: `string typeName` (Ability subclass, case-insensitive), `f32 cooldown`, `u8 Target`
(Self=1, Enemy=2, Friendly=3), `bool hasFuzzy` [+`string` fuzzy expression], `i32 n` + n × `string` Animations.
Then:

| Type | Extra fields |
|---|---|
| Block | `f32 arc`, `i32 shield` |
| CastSpell | `f32 minRange`, `f32 maxRange`, `f32 arc`, `f32 chantSpeed`, `f32 power`, `i32 CastType`, `i32 n`, n × `i32 Elements` |
| ConfuseGrip, DamageGrip, RemoveStatus | — |
| Dash | `f32 minRange`, `f32 maxRange`, `f32 arc`, `Vector3 vector` |
| ElementSteal | `f32 range`, `f32 angle` |
| GripCharacterFromBehind | `f32 maxRange`, `f32 minRange`, `f32 angle`, `f32 maxWeight` |
| Jump | `f32 maxRange`, `f32 minRange`, `f32 angle`, `f32 elevation` |
| Melee | `f32 minRange`, `f32 maxRange`, `f32 arc`, `i32 n`, n × `i32 weapon`, `bool rotate` |
| PickUpCharacter, ZombieGrip | `f32 maxRange`, `f32 minRange`, `f32 angle`, `f32 maxWeight`, `string dropAnimation` |
| Ranged | `f32 minRange`, `f32 maxRange`, `f32 elevation`, `f32 arc` (overwritten with π), `f32 accuracy`, `i32 n`, n × `i32 weapon` |
| SpecialAbilityAbility | `f32 maxRange`, `f32 minRange`, `f32 angle`, `i32 weapon` |
| ThrowGrip | `f32 maxRange`, `f32 minRange`, `f32 elevation`, `i32 n`, n × Damage(**i32** amount). At most 4 Damage are read (with n > 4 the original desynchronizes) |

### 5.7 BuffStorage / AuraStorage

BuffStorage: `u8 BuffType`, `u8 VisualCategory` (None, Offensive, Defensive, Special), `Vector3 color`, `f32 TTL`,
`string effect` (→lower hash, `""` = 0), then:

| BuffType | Fields |
|---|---|
| 0 BoostDamage | Damage(f32) |
| 1 DealDamage | Damage(f32) (1 s interval) |
| 2 Resistance | `i32 Elements`, `f32 Modifier`, `f32 Multiplier`, `bool status` (**the Modifier/Multiplier order is reversed** compared to Resistance in templates) |
| 3 Undying | — |
| 4 Boost | `f32 amount` |
| 5 ReduceAgro | `f32 amount` |
| 6 ModifyHitPoints | `f32 multiplier`, `f32 modifier` |
| 7 ModifySpellTTL | `f32 multiplier`, `f32 modifier` |
| 8 ModifySpellRange | `f32 multiplier`, `f32 modifier` |

AuraStorage: `u8 AuraTarget` (Friendly, FriendlyButSelf, Enemy, All, AllButSelf, Self, Type, TypeButSelf, Faction,
FactionButSelf), `u8 AuraType`, `u8 VisualCategory`, `Vector3 color`, `string effect` (→lower hash), `f32 TTL`,
`f32 radius`, `string targetTypes` (→lower, comma-separated, each → hash), `i32 targetFaction`, then:

| AuraType | Fields |
|---|---|
| 0 Buff | BuffStorage |
| 1 Deflect | `f32 strength` |
| 2 Boost | `f32 magnitude` |
| 3 LifeSteal | `f32 amount` |
| 4 Love | `f32 radius`, `f32 ttl` |

---

## 6. `Magicka.Physics.CollisionSkinReader`

The reader exists in the code, but **no content file uses it** (it is not in any reader table).
Layout from the code: `i32 vertexCount`, vertexCount × Vector3, `i32 indexCount` (the number of **indices**; triangles =
indexCount/3), then 3 × i32 per triangle, which are assigned in reverse order (I2, I1, I0) —
reversed winding compared to the triangles of LevelModel/PhysicsEntityTemplate (I0, I1, I2).
Not verified on data (no files).

---

## 7. Verification status

Parser: Python, based on `tools/research/xnb.py` (LZX), helper scripts outside the repository. For each
file, the main object and all shared resources are read, and the parser checks that exactly as many bytes were read
as the decompressed body contains.

| Reader (main type of the file) | Files | Parsed with no remainder | Example |
|---|---|---|---|
| Texture2DReader | 2296 | 2296 | `EffectTextures/ArcaneBlade.xnb` (Dxt1, 128², 8 mips) |
| TextureCubeReader | 13 | 13 | `EffectTextures/burnCube.xnb` |
| Texture3DReader | 7 | 7 | `EffectTextures/ParticlesA.xnb` |
| EffectReader | 52 | 52 | `Shaders/AdditiveEffect.xnb` |
| ModelReader | 553 | 553 | `Models/AnimatedProps/Dungeons/dungeons_radio.xnb` (8 bones, 1 mesh, EffectMaterial) |
| SkinnedModelReader | 327 | 327 | `Models/AnimatedProps/toxic_spore_animations_0.xnb` (15 bones, 5 clips) |
| LevelModelReader | 132 | 132 | `Levels/Ruins/ru_s5.xnb` (13.7 MB: 67 BiTree roots, 2 parts, 36 lights, lava, 12 triggers, 42 locators, navmesh 6818/12577), `Levels/BattleField/bf_s1.xnb` (nested animated parts) |
| CharacterTemplateReader | 338 | 338 | `Data/Characters/Beastman_brute.xnb` (24 animations) |
| ItemReader | 454 | 454 | `Data/items/NPC/acc_ammoBelt_0.xnb` (including 1 uncompressed file) |
| PhysicsEntityTemplateReader | 69 | 69 | `Data/AnimatedProps/dungeons_radio.xnb` |
| BitmapFontReader | 66 | 66 | `Languages/eng/Font/Maiandra14.xnb` (198 glyphs) |
| ListReader<String> | 1 | 1 | `Audio/SoundList.xnb` |

Nested readers were verified transitively as part of these files: VertexDeclaration/VertexBuffer/IndexBuffer,
String, ExternalReference, List<Vector3>, Dictionary<String,Object>, Single/Boolean/Vector3/Matrix, BasicEffect (6),
EffectMaterial (3), SkinnedModelBone, AnimationClip, SkinnedModelBasicEffect, SkinnedModelDeferredNormalMappedEffect (4 files),
RenderDeferredEffect, RenderDeferredLiquidEffect, AdditiveEffect, LavaEffect (4 levels), BiTreeModel (132).
Found: all AnimationAction subtypes except SetItemAttach; all Ability subtypes except ElementSteal and ZombieGrip;
BuffType 0–8; AuraType 0–1; EventType 0–5, 8, 10–16.

Total: **4308 of 4308** `.xnb` files parsed in full with no remainder.

The C# readers of OpenMagicka (`OpenMagicka.Content`, registry `XnbReaderRegistry.Magicka`) reproduce
this: with `MAGICKA_DIR` set, the test `MagickaContent_LoadsCompletely` loads all 4308 files with no
bytes left, and the LZX output matches the research decoder byte for byte (SHA-256 per file).

Not covered by data (layout from the code only): AuraType 2–4
(Boost/LifeSteal/Love), EventType 6 (Decal) and 9 (Overkill, no fields), AnimationAction SetItemAttach,
Ability ElementSteal/ZombieGrip, CollisionSkinReader, the PhysicsEntityTemplate branch without the animation part.

---

## 8. Open questions

1. **Semantics of the bool pairs in MeshSettings** of AnimatedLevelPart (presumably visible / castShadows) — verify against
   their usage in part rendering.
2. **Meaning of Cost*** in navmesh triangles (edge length or the cost of crossing the edge to the neighbor).
   (0xFFFF = "no neighbor" is confirmed, see §4.9.)
3. **Mapping of collision slots to CollisionMaterials**: the conclusion is based on the primitive index in character physics;
   it should be confirmed on levels with snow/water.
4. **TextureCube face order** and mip order — the standard order is assumed (+X,−X,+Y,−Y,+Z,−Z); not verified visually.
5. **Color spaces and ranges** of material parameters (EmissiveAmount, NormalPower, Sharpness, etc.)
   — needed for shaders; this is a rendering question, not a format question.
6. **Technique of SkinnedModelBasicEffect**: the enum values and their mapping to the techniques in
   `SkinnedModelBasicEffect.xnb`.
7. **Behavior when the shared effect is missing** for Model parts, and also the fix-up order of references
   (bones reference each other through shared resources — a two-phase resolve is needed).
8. **Full enums that are stored as strings** (Animations — 231 values with aliases, StatusEffects, MagickType,
   class names of AnimationAction/Ability/SpecialAbility) — they need to be written out in a separate reference so that IDs
   match the original.
9. **Factions, Order, ReactTo, CastType, Decal** — the numeric enum values are not listed here (stored as i32/u8).
10. **Is bit-exact compatibility of `GetHashCodeCustom` needed** for strings containing non-ASCII characters (the algorithm
    works with UTF-16 code units — for ASCII the result matches the description).
11. Compatibility of the `Content/Shaders/*.xnb` (fx_2_0) bytecode with MojoShader/FNA — checked: all 51 effects
    work after a bytecode patch with our FNA3D (50 with the stock one), see [Stack risk checks](../engine/RISK_CHECKS.md).
