# XNB v4: бинарные макеты ContentTypeReader'ов Magicka

Документ описывает **точный порядок байт** всех нестандартных reader'ов, которые встречаются в
`Content/` Steam-версии Magicka 1.10.4.2, а также стандартных reader'ов XNA 3.1 в том виде, в котором
они реально записаны в файлах (формат XNB v4 отличается от XNA 4.0 / MonoGame v5).

> Clean-room: ниже только описание формата и поведения (порядок полей, типы, условия). Код
> оригинала не воспроизводится. Имена классов/полей приводятся как идентификаторы.

Статус проверки: **каждый** перечисленный reader проверен исследовательским парсером на всех
файлах соответствующего типа: файл разбирается целиком (главный объект + все shared resources),
и проверяется, что съедено ровно `decompressedSize` байт (остаток = 0). Подробности — в §7.

---

## 0. Общие соглашения

### 0.1 Примитивы (`BinaryReader` .NET 2.0 / `ContentReader` XNA 3.1)

Порядок байт little-endian везде.

| Обозначение | Размер | Описание |
|---|---|---|
| `u8` / `byte` | 1 | беззнаковый байт; enum'ы с базой `byte` |
| `bool` | 1 | 0 или 1 (во всех файлах других значений не встречено) |
| `u16` / `i32` / `u32` | 2 / 4 / 4 | целые |
| `f32` | 4 | IEEE-754 float |
| `7bit` | 1–5 | 7-битное var-int (младшие группы первыми, старший бит = «есть продолжение») |
| `string` | 7bit + N | длина **в байтах** (7bit) + UTF-8 |
| `char` | 1–3 | один символ UTF-8 (`BinaryReader.ReadChar` с UTF-8): длина по первому байту (`<0x80`→1, `<0xE0`→2, иначе 3) |
| `Vector2/3/4` | 8/12/16 | float'ы X,Y(,Z(,W)) |
| `Quaternion` | 16 | X,Y,Z,W |
| `Matrix` | 64 | 16 float в порядке M11,M12,M13,M14,M21,…,M44 (row-major, XNA-конвенция: строка 4 = трансляция) |
| `BoundingSphere` | 16 | Vector3 центр + f32 радиус |

### 0.2 Объектная модель ContentReader

| Операция | Байты |
|---|---|
| `Object<T>` (ReadObject) | `7bit typeId`; 0 = null; иначе `typeId-1` — индекс в таблице reader'ов файла, далее данные этого reader'а. **Тип определяется таблицей, а не ожидаемым T** (например, на месте `Object<Texture2D>` может стоять `ExternalReferenceReader`). |
| `Raw<T>` (значимые типы внутри коллекций) | только данные reader'а, без typeId |
| `ExtRef<T>` (ReadExternalReference) | `string` — путь к ассету **относительно каталога текущего файла**, без расширения, с `\` (например `..\..\..\Models\Items_NPC\acc_ammoBelt_0`); пустая строка = null |
| `SharedRef<T>` (ReadSharedResource) | `7bit`; 0 = null; иначе 1-based индекс в таблице shared resources файла; значение подставляется **после** чтения главного объекта (отложенный fix-up) |

### 0.3 Контейнер XNB v4 (XNA 3.1)

| Смещение | Тип | Значение |
|---|---|---|
| 0 | 3 байта | `XNB` |
| 3 | u8 | платформа: `w` (во всех файлах) |
| 4 | u8 | версия: **4** (XNA 3.1); у XNA 4.0 — 5 |
| 5 | u8 | флаги: `0x80` = LZX-сжатие. Бита `0x01` (HiDef, XNA 4.0) нет. 4299 файлов сжаты, 9 — нет (один из них — `Item`) |
| 6 | u32 | полный размер файла |
| 10 | u32 | (только если сжат) размер распакованных данных |
| 10/14 | … | данные (LZX: окно 64 КБ, фреймы 32 КБ; см. `tools/research/xnb.py`) |

Распакованное тело:

1. `7bit readerCount`, затем `readerCount` × (`string` имя reader'а, `i32` версия reader'а). У всех reader'ов версия = 0. Имена — assembly-qualified строки (например `Magicka.ContentReaders.LevelModelReader, Magicka` или с `Version=1.0.0.0, Culture=neutral`; generic'и — в синтаксисе ``ListReader`1[[Microsoft.Xna.Framework.Vector3, Microsoft.Xna.Framework, Version=3.1.0.0, …]]``). Для сопоставления надёжно брать часть до первой запятой (для generic — с учётом `[[…]]`).
2. `7bit sharedResourceCount`.
3. `Object` — главный объект (с typeId).
4. `sharedResourceCount` × `Object` — shared resources (каждый с typeId).

В таблице reader'ов могут быть записи, которые ни разу не используются (например `TextureReader` в моделях с EffectMaterial — reader зарегистрирован, но объекта такого типа в потоке нет).

### 0.4 Хэш строк `GetHashCodeCustom`

Многие строковые поля сразу при загрузке превращаются в `int`-хэш (имена эффектов, звуков, локаторов,
триггеров, ID шаблонов). Для совместимости ID нужно воспроизвести алгоритм — это фиксированная
реализация `String.GetHashCode` из .NET 2.0/3.5 (x86):

- два 32-битных аккумулятора, оба стартуют с `0x15051505` (352654597);
- строка обрабатывается как массив UTF-16 code unit'ов, читаемых **парами** как 32-битные слова
  (младшее слово — первый символ); после последнего символа в памяти лежит `\0`, поэтому у строки
  нечётной длины последнее слово = (последний символ, 0);
- на каждом шаге (по 4 символа) первый аккумулятор: `h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ word0`,
  затем, если осталось больше 2 символов, второй аналогично со `word1`; арифметический сдвиг вправо,
  переполнение 32-бит;
- результат `h1 + h2 * 1566083941` (mod 2^32, знаковый int).

Перед хэшированием строка часто приводится к нижнему регистру (`ToLowerInvariant`) — это отмечено
в таблицах как «→hash(lower)».

### 0.5 Часто используемые enum'ы (как хранятся)

| Enum | Хранение | Значения |
|---|---|---|
| `Elements` | i32, флаги | Earth=1, Water=2, Cold=4, Fire=8, Lightning=0x10, Arcane=0x20, Life=0x40, Shield=0x80, Ice=0x100, Steam=0x200, Poison=0x400. Индекс 0..10 в массивах сопротивлений — порядковый номер бита |
| `AttackProperties` | i32, флаги | Damage=1, Knockdown=2, Pushed=4, Knockback=6, Piercing=8, ArmourPiercing=0x10, Status=0x20, Entanglement=0x40, Stun=0x80, Bleed=0x100 |
| `Banks` (XACT-банк) | i32, флаги | WaveBank=1, Music=2, Ambience=4, UI=8, Spells=0x10, Characters=0x20, Footsteps=0x40, Weapons=0x80, Misc=0x100, Additional=0x200, AdditionalMusic=0x400 |
| `LightType` | i32 | Point=0, Directional=1, Spot=2 (Custom=10 → ошибка загрузки) |
| `LightVariationType` | i32 (в LevelModel) / u8 (в Character/Item/LightEvent) | None, Sine, Flicker, Candle, Strobe |
| `CollisionMaterials` | u8 | Generic, Gravel, Grass, Wood, Snow, Stone, Mud, Reflect, Water, Lava |
| `MovementProperties` | u8, флаги | Default=0, Water=1, Jump=2, Fly=4, Dynamic=0x80 |
| `WeaponClass` | u8 | 27 значений (Default, Thrust_*, Crush_*, Slash_*, Throw_*, Unarmed, Handgun, Rifle, Machinegun, Heavy, Staff, NewAnimationSet0..7) — индекс набора анимаций |
| `Animations` | строка в файле | имя enum'а (231 значение), парсится без учёта регистра |

---

## 1. Стандартные reader'ы XNA 3.1 (как они лежат в файлах)

### 1.1 `Microsoft.Xna.Framework.Content.Texture2DReader` (v0)

| # | Тип | Поле |
|---|---|---|
| 1 | i32 | `SurfaceFormat` (нумерация **XNA 3.1**) |
| 2 | u32 | width |
| 3 | u32 | height |
| 4 | u32 | mipCount |
| 5 | mipCount × (`u32 size`, `size` байт) | уровни, от крупного к мелкому |

Встреченные форматы (2296 файлов + вложенные): **1 = Color**, **28 = Dxt1**, **32 = Dxt5**.
Размеры подтверждают: Color 4 Б/пиксель, Dxt1 8 Б на блок 4×4, Dxt5 16 Б на блок. В XNA 4.0 те же
форматы имеют номера 0 / 4 / 6 — **это главное отличие от v5**. Порядок байт `Color` — **B,G,R,A**
(D3DFMT_A8R8G8B8; проверено по цветам текстур `lava`, `timer_fill_blue/red`). В XNA 4.0 `Color` —
R,G,B,A, т.е. при переносе на FNA/MonoGame нужен swizzle (или формат Bgra32).

Полная таблица SurfaceFormat 3.1 (по документации XNA 3.1, в файлах встречены только 1/28/32):
Color=1, Bgr32=2, Bgra1010102=3, Rgba32=4, Rgb32=5, Rgba1010102=6, Rg32=7, Rgba64=8, Bgr565=9,
Bgra5551=10, Bgr555=11, Bgra4444=12, Bgr444=13, Bgra2338=14, Alpha8=15, Bgr233=16, Bgr24=17,
NormalizedByte2=18, NormalizedByte4=19, NormalizedShort2=20, NormalizedShort4=21, Single=22,
Vector2=23, Vector4=24, HalfSingle=25, HalfVector2=26, HalfVector4=27, Dxt1=28, Dxt2=29, Dxt3=30,
Dxt4=31, Dxt5=32, Luminance8=33, …

### 1.2 `Texture3DReader` (v0)

`i32 format`, `u32 width`, `u32 height`, `u32 depth`, `u32 mipCount`, затем mipCount × (`u32 size`, данные).
7 файлов (`EffectTextures/Particles*`), формат 1 (Color), 256×256×1, 9 мипов. Совпадает по макету с 4.0 (кроме enum'а формата).

### 1.3 `TextureCubeReader` (v0)

`i32 format`, `u32 size`, `u32 mipCount`, затем **6 граней** × mipCount × (`u32 size`, данные) — грань-major
(+X, −X, +Y, −Y, +Z, −Z — порядок CubeMapFace). 13 файлов, формат 32 (Dxt5).

### 1.4 `VertexDeclarationReader` (v0) — отличается от 4.0

| # | Тип | Поле |
|---|---|---|
| 1 | u32 | elementCount |
| 2 | elementCount × 8 байт | `u16 stream`, `u16 offset`, `u8 VertexElementFormat`, `u8 VertexElementMethod`, `u8 VertexElementUsage`, `u8 usageIndex` |

Stride в декларации **не хранится** (в 3.1 он передаётся отдельно — см. `BiTreeRootNode.vertexStride`,
`Liquid.stride`; в Model он вычисляется). В XNA 4.0 формат другой: `u32 stride`, `u32 count`, по 16 байт
на элемент (`i32 offset`, `i32 format`, `i32 usage`, `i32 usageIndex`) с другими номерами enum'ов.

Нумерация 3.1 (подтверждена смещениями/размерами в файлах):

- `VertexElementFormat`: Single=0, Vector2=1, Vector3=2, Vector4=3, Color=4, Byte4=5, Short2=6, Short4=7, …
- `VertexElementUsage`: Position=0, BlendWeight=1, BlendIndices=2, Normal=3, PointSize=4, TextureCoordinate=5, Tangent=6, Binormal=7, TessellateFactor=8, Color=10, Fog=11, Depth=12, Sample=13
- `VertexElementMethod`: везде 0 (Default); `stream` везде 0.

Встреченные комбинации: Position(Vector3), Normal(Vector3), TexCoord(Vector2, индексы 0/1), Tangent(Vector3),
Binormal(Vector3), Color(Vector4 или Color), BlendIndices(Byte4) + BlendWeight(Vector4) у скинированных
моделей, Color(Single) у воды/лавы (уровень «заморозки»).

### 1.5 `VertexBufferReader` (v0) — отличается от 4.0

`u32 sizeInBytes` + сырые вершины. Декларации и числа вершин **нет** (в 4.0: встроенная декларация + `u32 vertexCount` + данные).

### 1.6 `IndexBufferReader` (v0)

`bool is16Bit`, `u32 sizeInBytes`, данные. Во всех файлах is16Bit = true. Совпадает с 4.0.

### 1.7 `EffectReader` (v0)

`u32 size` + бинарник DX9 Effect (`fx_2_0`, начинается с `01 09 FF FE`). 52 файла (`Content/Shaders/*.xnb`
и `SkinnedModelBasicEffect.xnb`). Макет совпадает с 4.0, но MonoGame ожидает свой формат шейдера; FNA
(MojoShader) может исполнять этот байткод напрямую.

### 1.8 `ModelReader` (v0) — сильно отличается от 4.0

| # | Тип | Поле |
|---|---|---|
| 1 | u32 | boneCount |
| 2 | boneCount × (`Object<string>` name, `Matrix` transform) | кости |
| 3 | boneCount × (`BoneRef` parent, `u32` childCount, childCount × `BoneRef`) | иерархия |
| 4 | u32 | **vertexDeclarationCount** |
| 5 | vertexDeclarationCount × `Object<VertexDeclaration>` | таблица деклараций модели |
| 6 | u32 | meshCount |
| 7 | meshCount × Mesh | см. ниже |
| 8 | `BoneRef` | root bone |
| 9 | `Object` | Tag (во всех файлах null) |

`BoneRef`: `u8`, если boneCount < 255, иначе `u32`; 0 = нет кости, иначе индекс+1.

Mesh:

| # | Тип | Поле |
|---|---|---|
| 1 | `Object<string>` | name |
| 2 | `BoneRef` | parentBone |
| 3 | BoundingSphere | bounds |
| 4 | `Object<VertexBuffer>` | **общий VB меша** |
| 5 | `Object<IndexBuffer>` | **общий IB меша** |
| 6 | `Object` | Tag (null) |
| 7 | u32 | partCount |
| 8 | partCount × MeshPart | |

MeshPart:

| # | Тип | Поле |
|---|---|---|
| 1 | u32 | streamOffset (байт) |
| 2 | u32 | baseVertex |
| 3 | u32 | numVertices |
| 4 | u32 | startIndex |
| 5 | u32 | primitiveCount |
| 6 | u32 | **индекс декларации** в таблице п.5 |
| 7 | `Object` | Tag (null) |
| 8 | `SharedRef<Effect>` | эффект (лежит в shared resources файла) |

Vertex stride части = размер её декларации (сумма размеров элементов). В 4.0 таблицы деклараций нет,
VB/IB — shared resources у каждой части, а у частей поля `vertexOffset/numVertices/startIndex/primitiveCount`.

Какие эффекты встречаются в shared resources Model: `RenderDeferredEffect` (статичные модели, предметы,
части уровней), `AdditiveEffect`, `SkinnedModelBasicEffect` / `SkinnedModelDeferredNormalMappedEffect`
(внутри SkinnedModel), стандартные `BasicEffect` (6 файлов) и `EffectMaterial` (3 файла, ассеты-заглушки
из Maya).

### 1.9 `BasicEffectReader` (v0)

`ExtRef<Texture>` texture, `Vector3` diffuseColor, `Vector3` emissiveColor, `Vector3` specularColor,
`f32` specularPower, `f32` alpha, `bool` vertexColorEnabled. (Порядок подтверждён правдоподобными
значениями: alpha=1, specularPower=16/64.)

### 1.10 `EffectMaterialReader` (v0)

`ExtRef<Effect>` + `Object<Dictionary<string,object>>` параметров. Значения словаря — `SingleReader`,
`BooleanReader`, `Vector3Reader`, `MatrixReader`, `ExternalReferenceReader` (текстуры). Используется
в 3 файлах (`Models/AnimatedProps/Dungeons/*`), ссылки ведут на `..\..\..\Shaders\Maya\SkinnedModelEffect_0`.

### 1.11 Прочие стандартные

| Reader | Байты |
|---|---|
| `StringReader` | `string` |
| `SingleReader` / `BooleanReader` / `Vector3Reader` / `MatrixReader` | соответствующий примитив |
| `ExternalReferenceReader` | `string` (путь, как в ExtRef). Появляется как значение `Object<T>`, когда ассет вынесен в отдельный файл |
| ``ListReader`1[Vector3]`` | `u32 count` + count × Vector3 (**без** typeId — значимый тип) |
| ``ListReader`1[String]`` | `u32 count` + count × `Object<string>` (с typeId). Только `Audio/SoundList.xnb` |
| ``DictionaryReader`2[String,Object]`` | `u32 count` + count × (`Object` key, `Object` value) |
| `TextureReader` | присутствует в таблицах, но объектов этого типа в данных нет |

---

## 2. XNAnimation 0.7 (`XNAnimation.Pipeline.*`)

### 2.1 `SkinnedModelReader` (v0) — главный тип 327 файлов

| # | Тип | Поле |
|---|---|---|
| 1 | `Object<Model>` | обычная XNA 3.1 Model (§1.8); эффекты частей — `SkinnedModelBasicEffect` или `SkinnedModelDeferredNormalMappedEffect` в shared resources |
| 2 | i32 | boneCount |
| 3 | boneCount × `SharedRef<SkinnedModelBone>` | скелет (порядок = индекс кости) |
| 4 | i32 | clipCount |
| 5 | clipCount × `SharedRef<AnimationClip>` | клипы, ключ словаря = AnimationClip.Name |

Файлы `*_animations*.xnb` содержат и меш, и клипы; «мешевые» файлы могут иметь clipCount = 0.

### 2.2 `SkinnedModelBoneReader` (v0) (shared resource)

| # | Тип | Поле |
|---|---|---|
| 1 | u16 | index |
| 2 | string | name |
| 3 | Vector3 | bindPose.Translation |
| 4 | Quaternion | bindPose.Orientation |
| 5 | Vector3 | bindPose.Scale |
| 6 | Matrix | inverseBindPoseTransform |
| 7 | `SharedRef<SkinnedModelBone>` | parent (0 у корня) |
| 8 | i32 | childCount |
| 9 | childCount × `SharedRef<SkinnedModelBone>` | children |

### 2.3 `AnimationClipReader` (v0) (shared resource)

| # | Тип | Поле |
|---|---|---|
| 1 | string | name |
| 2 | f32 | duration (секунды) |
| 3 | i32 | channelCount |
| 4 | channelCount × (`string` boneName, AnimationChannel) | |

**AnimationChannel** (тот же макет используется в `AnimatedLevelPart`): `i32 keyframeCount`, затем
keyframeCount × (`f32 time`, `Vector3 translation`, `Quaternion orientation`, `Vector3 scale`) — 44 байта
на ключ. Поиск ключа по времени — бинарный, ключи отсортированы по времени.

### 2.4 `SkinnedModelBasicEffectReader` (v0) (shared resource)

| # | Тип | Поле |
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

Важно: текстуры здесь — `Object`, а не ExtRef. В файлах на их месте стоит typeId
`ExternalReferenceReader` + путь (или 0 = null), т.е. на практике это тоже внешние ссылки, но с typeId.
Для создания требуется глобальный `EffectPool` (шейдер берётся из `SkinnedModelBasicEffect.xnb`).

---

## 3. PolygonHead (`PolygonHead.Pipeline.*`)

### 3.1 `RenderDeferredEffectReader` (v0) — основной материал статической геометрии

| # | Тип | Поле |
|---|---|---|
| 1 | f32 | Alpha |
| 2 | f32 | Sharpness |
| 3 | bool | VertexColorEnabled |
| 4 | bool | UseMaterialTextureForReflectiveness |
| 5 | `ExtRef<TextureCube>` | ReflectionMap |
| 6 | Layer0 | см. ниже |
| 7 | bool | hasLayer1 |
| 8 | Layer1 | только если hasLayer1 |

Layer (суффикс 0/1): `bool DiffuseTextureNAlphaDisabled`, `bool AlphaMaskNEnabled`, `Vector3 DiffuseColorN`,
`f32 SpecAmountN`, `f32 SpecPowerN`, `f32 EmissiveAmountN`, `f32 NormalPowerN`, `f32 ReflectivenessN`,
`ExtRef<Texture2D> DiffuseTextureN`, `ExtRef<Texture2D> MaterialTextureN`, `ExtRef<Texture2D> NormalTextureN`.
Второй слой — для блендинга двух материалов по вершинному цвету. World = Identity после загрузки.

### 3.2 `RenderDeferredLiquidEffectReader` (v0) — вода

| # | Тип | Поле |
|---|---|---|
| 1 | `ExtRef<Texture>` | ReflectionMap (ожидается TextureCube; иначе null) |
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

Встречается только внутри LevelModel (Liquid), а также как эффект частей моделей AnimatedLevelPart.

### 3.3 `LavaEffectReader` (v0) — лава (4 уровня: `ru_s5`, `ru_s6BF`, `ew_s4`, `ch_volcano_hideout`)

| # | Тип | Поле |
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

`Vector3 ColorTint` (W принудительно = 1), `bool VertexColorEnabled`, `bool TextureEnabled`, `ExtRef<Texture2D> Texture`.
Встречается как эффект корней BiTree и частей Model (аддитивные объекты рисуются отдельным проходом).

### 3.5 `SkinnedModelDeferredNormalMappedEffectReader` (v0)

`Vector3 DiffuseColor`, `f32 SpecularAmount`, `f32 SpecularPower`, `f32 EmissiveAmount`, `f32 NormalPower`,
затем 5 × `ExtRef<Texture2D>`: DiffuseMap, MaterialMap, DamageMap, NormalMap, NormalDamageMap.
OverrideColor обнуляется. Только 4 файла (`Models/Bosses/cthulhu/*`). Требует глобальный EffectPool.

(В PolygonHead есть ещё `SkinnedModelDeferredEffect` со своим методом чтения, но reader'а для него
в контенте нет — не используется.)

### 3.6 `BiTreeModelReader` (v0) — статическая геометрия уровня (BSP-подобное дерево AABB)

| # | Тип | Поле |
|---|---|---|
| 1 | i32 | rootCount |
| 2 | rootCount × BiTreeRootNode | |

BiTreeRootNode (по одному на материал/батч):

| # | Тип | Поле |
|---|---|---|
| 1 | bool | Visible |
| 2 | bool | CastShadows |
| 3 | f32 | Sway (амплитуда покачивания растительности) |
| 4 | f32 | EntityInfluence (реакция покачивания на сущности) |
| 5 | f32 | GroundLevel |
| 6 | i32 | numVertices |
| 7 | i32 | vertexStride (байт; встречены 44, 60, 64, 72, 80, 96) |
| 8 | `Object<VertexDeclaration>` | |
| 9 | `Object<VertexBuffer>` | |
| 10 | `Object<IndexBuffer>` | |
| 11 | `Object<Effect>` | `RenderDeferredEffect` или `AdditiveEffect` (inline, не shared) |
| 12 | i32 | primitiveCount (всего узла) |
| 13 | i32 | startIndex |
| 14 | Vector3 | bbox.Min |
| 15 | Vector3 | bbox.Max |
| 16 | bool | hasChildA → BiTreeNode |
| 17 | bool | hasChildB → BiTreeNode |

BiTreeNode (рекурсивно, в порядке: A полностью, затем B): `i32 primitiveCount`, `i32 startIndex`,
`Vector3 min`, `Vector3 max`, `bool hasChildA` [+узел], `bool hasChildB` [+узел]. numVertices наследуется от корня.

Поведение: при отрисовке, если узел полностью в фрустуме — рисуется весь его диапазон индексов; если
пересекается и есть оба ребёнка — рекурсия; иначе рисуется целиком. Т.е. диапазон родителя покрывает
диапазоны детей (индексы упорядочены по дереву). Рисуется `TriangleList`, baseVertex = 0.

### 3.7 `BitmapFontReader` (v0) — 66 файлов `Languages/<lang>/Font/*.xnb`

| # | Тип | Поле |
|---|---|---|
| 1 | i32 | LineHeight |
| 2 | i32 | BaseLine |
| 3 | char | DefaultCharacter (UTF-8) |
| 4 | `Object<Texture2D>` | атлас (inline Texture2D, формат Color) |
| 5 | i32 | glyphCount (до 263 в файлах) |
| 6 | glyphCount × Glyph | |
| 7 | i32 | kerningCount (в части шрифтов > 0) |
| 8 | kerningCount × (`char` first, `char` second, `i32` amount) | |

Glyph: `char Character`, `i32 Origin.X`, `i32 Origin.Y`, `i32 Size.X`, `i32 Size.Y`, `i32 AdvanceWidth`,
`i32 LeftSideBearing`, `bool ForceWhite`. Origin/Size — пиксели в атласе. Spacing = 0. При отсутствии
символа используется DefaultCharacter, затем любой первый.

---

## 4. Magicka: `LevelModelReader` (132 файла `Levels/**/*.xnb`)

Главный объект уровня-сцены. Поля строго по порядку:

| # | Тип | Поле |
|---|---|---|
| 1 | `Object<BiTreeModel>` | статическая геометрия (§3.6) |
| 2 | i32 + N × AnimatedLevelPart | анимированные части (рекурсивные) |
| 3 | i32 + N × LevelLight | источники света |
| 4 | i32 + N × VisualEffectStorage | эффекты-частицы на уровне |
| 5 | i32 + N × (`Matrix` startTransform, `string` template) | физ-объекты; шаблон грузится как `Data/PhysicsEntities/<template>` |
| 6 | i32 + N × Liquid | вода/лава |
| 7 | i32 + N × ForceField | силовые поля |
| 8 | **10** × CollisionMeshSlot | статическая коллизия, по слоту на материал |
| 9 | bool + CameraMesh | опциональная коллизия камеры |
| 10 | i32 + N × (`string` name, TriggerArea) | зоны триггеров; ключ = hash(lower(name)); дополнительно движок добавляет «any»-зону |
| 11 | i32 + N × (`string` name, Locator) | локаторы; ключ = hash(name) (без lower) |
| 12 | NavMesh | навигационная сетка (всегда) |

Shared resources файла — эффекты (`RenderDeferredEffect`/`AdditiveEffect`) частей Model внутри AnimatedLevelPart.

### 4.1 LevelLight

| # | Тип | Поле |
|---|---|---|
| 1 | string | name (ключ = hash(name)) |
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
| 15 | i32 | ShadowMapSize (корректируется настройкой качества теней) |
| 16 | bool | CastShadows (у directional-света, созданного из уровня, конструктор ставит false, но потом поле перезаписывается этим значением) |

Поля 6–9 читаются всегда, независимо от типа.

### 4.2 VisualEffectStorage (уровень и AnimatedLevelPart)

`string id` (→hash(lower)), `Vector3 position`, `Vector3 forward`, `f32 range`, `string effect`
(→hash; в LevelModel без lower, в AnimatedLevelPart — с lower). Трансформ = `CreateWorld(position, forward, up=+Y)`.
`effect` — имя XML-эффекта из `Content/Effects`.

### 4.3 AnimatedLevelPart (рекурсивно)

| # | Тип | Поле |
|---|---|---|
| 1 | string | name (→lower, ID = hash) |
| 2 | bool | AffectShields |
| 3 | `Object<Model>` | модель части (эффекты частей — Additive/RenderDeferred/RenderDeferredLiquid, иначе ошибка) |
| 4 | i32 + N × (`string` meshName, `bool`, `bool`) | MeshSettings: пара флагов на меш (по использованию — видимость и отбрасывание тени; не проверено) |
| 5 | i32 + N × Liquid | жидкости, привязанные к части |
| 6 | i32 + N × (`string` name, Locator) | локаторы части |
| 7 | f32 | animationDuration |
| 8 | AnimationChannel | анимация части (§2.3, один канал) |
| 9 | i32 + N × VisualEffectStorage | |
| 10 | i32 + N × (`string` lightName, `Matrix` localTransform) | ссылки на источники света уровня (по hash имени) |
| 11 | bool hasCollision → (`u8` CollisionMaterials, `Object<List<Vector3>>` vertices, TriangleList) | коллизия части |
| 12 | bool hasNavMesh → AnimatedNavMesh | (макет = NavMesh) |
| 13 | i32 + N × AnimatedLevelPart | дочерние части (ключ = hash(name)) |

### 4.4 Liquid (вода/лава)

| # | Тип | Поле |
|---|---|---|
| 1 | `Object<Effect>` | `RenderDeferredLiquidEffect` → Water, `LavaEffect` → Lava; другое — ошибка |
| 2 | `Object<VertexBuffer>` | |
| 3 | `Object<IndexBuffer>` | |
| 4 | `Object<VertexDeclaration>` | движок добавляет к ней элемент stream 1, Single, Color index 1 (динамический буфер «заморозки») |
| 5 | i32 | vertexStride |
| 6 | i32 | numVertices |
| 7 | i32 | primitiveCount |
| 8 | bool | hasCollision (создаются IceMesh + WaterMesh из позиций VB и индексов IB) |
| 9 | bool | Freezable |
| 10 | bool | AutoFreeze (все вершины изначально заморожены = 1.0) |

Коллизия строится по самим VB/IB: позиции берутся по смещению элемента Position, а также читается
float по смещению элемента Color (usage 10). Порядок индексов треугольника при построении коллизии
переставлен (0, 2, 1).

### 4.5 ForceField

| # | Тип | Поле |
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

Позиции обязаны быть Vector3 с usageIndex 0. В проверенных файлах ForceField не встречен ни разу
(счётчик везде 0) — макет только по коду.

### 4.6 CollisionMeshSlot (×10) и CameraMesh

Слот: `bool present`; если true — `Object<List<Vector3>> vertices`, `i32 triangleCount`,
triangleCount × (`i32 I0`, `i32 I1`, `i32 I2`). Слоты 0..9 становятся примитивами одного CollisionSkin;
индекс слота — это индекс примитива, по которому физика персонажа определяет материал поверхности,
т.е. слот = `CollisionMaterials` (Generic, Gravel, Grass, Wood, Snow, Stone, Mud, Reflect, Water, Lava).
Пример `bf_s1`: слоты 0, 3, 4, 5 заполнены.

CameraMesh: `bool present` + тот же макет (vertices + треугольники).

### 4.7 TriggerArea

`Vector3 position`, `Vector3 sideLengths`, `Quaternion orientation` → JigLibX `Box(position, R(orientation), sideLengths)`.
**position — угол коробки**, а не центр (центр = position + R·sideLengths/2). Сущность «внутри», если
расстояние от её позиции до коробки ≤ её радиуса.

### 4.8 Locator

`Matrix transform`, `f32 radius` (имя читается перед ним снаружи).

### 4.9 NavMesh / AnimatedNavMesh

| # | Тип | Поле |
|---|---|---|
| 1 | u16 | vertexCount |
| 2 | vertexCount × Vector3 | вершины |
| 3 | u16 | triangleCount |
| 4 | triangleCount × Triangle (25 байт) | |

Triangle: `u16 VertexA, VertexB, VertexC`, `u16 NeighbourA, NeighbourB, NeighbourC` (индексы соседних
треугольников; 0xFFFF — вероятно «нет соседа», не проверено), `f32 CostAB, CostBC, CostCA`, `u8 MovementProperties`.
Треугольники с флагом Dynamic (0x80) получают динамических соседей (стыковка с анимированными навмешами).
Лимит 65535 вершин/треугольников (пример `ru_s5`: 6818 вершин, 12577 треугольников).

---

## 5. Magicka: шаблоны сущностей

### 5.1 `CharacterTemplateReader` (338 файлов `Data/Characters/*.xnb`)

| # | Тип | Поле |
|---|---|---|
| 1 | string | ID (→lower, hash) |
| 2 | string | DisplayID (→lower, hash) — ключ локализации |
| 3 | i32 | Factions (флаги) |
| 4 | i32 | BloodType (regular, green, black, wood, insect, none) |
| 5–11 | 7 × bool | IsEthereal, LooksEthereal, Fearless, Uncharmable, Nonslippery, HasFairy, CanSeeInvisible |
| 12 | i32 + N × (`string` cue →lower hash, `i32` Banks) | AttachedSounds. **Хранятся только первые 4; при N > 4 оригинал перестаёт читать (рассинхронизация) — в данных N ≤ 4** |
| 13 | i32 + N × Gib | Gib: `ExtRef<Model>`, `f32 mass`, `f32 scale` |
| 14 | i32 + N × PointLightHolder (N ≤ 4, иначе ошибка) | `string jointName`, `f32 radius`, `Vector3 diffuse`, `Vector3 ambient`, `f32 specular`, `u8 LightVariationType`, `f32 variationAmount`, `f32 variationSpeed` |
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
| 29 | f32 | Length (капсула; min 0.01) |
| 30 | f32 | Radius |
| 31 | f32 | Mass |
| 32 | f32 | Speed |
| 33 | f32 | TurnSpeed |
| 34 | f32 | BleedRate |
| 35 | f32 | StunTime |
| 36 | i32 | SummonElementBank (Banks) |
| 37 | string | SummonElementCue (hash) |
| 38 | i32 + N × Resistance | `i32 Elements`, `f32 Multiplier`, `f32 Modifier`, `bool StatusResistance`; по умолчанию все 11 = (1, 0, false) |
| 39 | i32 + N × ModelProperties | `ExtRef<SkinnedModel>`, `f32 scale`, `Vector3 tint` (варианты внешности) |
| 40 | `ExtRef<SkinnedModel>` | файл со скелетом и клипами анимации |
| 41 | i32 + N × (`string` boneName →lower, `string` effect →lower hash) | AttachedEffects (кость должна существовать) |
| 42 | **27** × AnimationSet | по одному на WeaponClass |
| 43 | i32 + N × (`i32 slot`, Attachment) | Equipment, слотов 8 |
| 44 | ConditionCollection | события персонажа (§5.4) |
| 45–57 | 13 × f32 | AlertRadius, GroupChase, GroupSeparation, GroupCohesion, GroupAlignment, GroupWander, FriendlyAvoidance, EnemyAvoidance, SightAvoidance, DangerAvoidance, AngerWeight, DistanceWeight, HealthWeight |
| 58 | bool | Flocking |
| 59 | f32 | BreakFreeStrength |
| 60 | i32 + N × Ability | ИИ-способности (§5.6) |
| 61 | i32 + N × (`u8 MovementProperties`, `i32 count`, count × `string` Animations) | MoveAnimations; MoveAbilities = OR всех флагов |
| 62 | i32 + N × BuffStorage | (§5.7) |
| 63 | i32 + N × AuraStorage | TTL ≤ 0 → бесконечность |

Поведение при загрузке (не формат, но важно): для персонажей без флага FRIENDLY множители/модификаторы
сопротивлений масштабируются от числа игроков (`k = 1 − (players−1)·0.00666`; положительные ×k, отрицательные ÷k).
Кости `Mouth`, `RightAttach`, `LeftAttach`, `RightLeg`, `LeftLeg` и кость с индексом 1 (бедро) ищутся по имени
без учёта регистра; bind-позы joint'ов поворачиваются на 180° вокруг Y.

**AnimationSet**: `i32 count`, count × (`string` Animations-имя, AnimationClipAction).

**AnimationClipAction**: `string clipName` (ключ в клипах SkinnedModel), `f32 animationSpeed`, `f32 blendTime`,
`bool loop`, `i32 actionCount`, actionCount × AnimationAction.

**AnimationAction**: `string typeName` (имя класса, без учёта регистра), `f32 startTime`, `f32 endTime`
(доли длительности клипа 0..1), затем поля типа:

| Тип | Доп. поля |
|---|---|
| Block | `i32 weapon` |
| BreakFree | `f32 magnitude`, `i32 weapon` |
| CameraShake | `string` (игнорируется), `f32 duration`, `f32 magnitude` |
| CastSpell | `bool fromStaff`; если false — `string source` (кость) |
| Crouch | `f32 radius`, `f32 length` (≤0 → 0.01) |
| DamageGrip | `bool damageToOwner`, `i32 n` (≤5), n × Damage(f32 amount) |
| DealDamage | `i32 weapon`, `u8 Targets` (Target=0, Friendly=1, Enemy=2, NonCharacters=4, All=255) |
| DetachItem | `i32 item`, `Vector3 velocity` |
| Ethereal | `bool ethereal`, `f32 alpha`, `f32 speed` |
| Footstep, OverkillGrip, ReleaseGrip, ThrowGrip | — |
| Grip | `u8 GripType` (Pickup, Ride, Hold), `f32 radius`, `f32 breakFreeTolerance` (приводится к int; 0 → 10), `string gripAttachBone`, `string targetAttachBone`, `bool finishOnGrip` |
| Gunfire | `i32 weapon`, `f32 accuracy` |
| Immortal | `bool collide` |
| Invisible | `bool noEffect` |
| Jump | `f32 elevation` (градусы), `bool` [+`f32 minRange`], `bool` [+`f32 maxRange`] |
| Move | `Vector3 velocity` |
| PlayEffect | `string bone`, `bool attach`, `string effect` (→lower hash), `f32` (игнорируется) |
| PlaySound | `string cue` (hash), `i32 Banks` |
| RemoveStatus | `string StatusEffects`-имя |
| SetItemAttach | `i32 item`, `string bone` |
| SpawnMissile | `i32 weapon`, `Vector3 velocity` (длина² ≤ 1e-6 → нет), `bool itemAligned` |
| SpecialAbility | `i32 weapon`; если < 0 — SpecialAbility (§5.5) |
| Suicide | `bool overkill` |
| Tongue | `f32 maxLength` |
| WeaponVisibility | `i32 weapon`, `bool visible` |

**Attachment** (equipment): `string boneName` (должна существовать), `Vector3 rotation` (X — rotX,
Y — rotY + π, Z — снова **rotX**; так в оригинале), `ExtRef<Item>`.

**Damage** (во многих местах): `i32 AttackProperties`, `i32 Elements`, amount, `f32 magnitude`, где amount —
`f32`, кроме мест, помеченных «Damage(i32)»: там amount хранится как `i32`.

### 5.2 `ItemReader` (454 файла `Data/Items/**`)

| # | Тип | Поле |
|---|---|---|
| 1 | string | name (тип = hash(name), регистр сохраняется) |
| 2 | string | displayName (hash; ключ локализации) |
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
| 13 | i32 + N × Resistance (с bool) | |
| 14 | u8 | PassiveAbility (None, ShieldBoost, AreaLifeDrain, ZombieDeterrent, ReduceAggro, EnhanceAllyMelee, AreaRegeneration, InverseArcaneLife, Zap, BirchSteam, WetLightning, MoveSpeed, Glow, Mjolnr, Gungner, MasterSword, DragonSlayer) |
| 15 | f32 | PassiveAbility variable |
| 16 | i32 + N × string | эффекты (→lower hash) |
| 17 | i32 + N × PointLight (N ≤ 1) | `f32 radius`, `Vector3 diffuse`, `Vector3 ambient`, `f32 specular`, `u8 variation`, `f32 varAmount`, `f32 varSpeed` |
| 18 | bool hasSpecial → (`f32 rechargeTime`, SpecialAbility) | |
| 19 | f32 | MeleeRange |
| 20 | bool | MeleeMultiHit |
| 21 | ConditionCollection | MeleeConditions |
| 22 | f32 | RangedRange |
| 23 | bool | Facing |
| 24 | f32 | Homing |
| 25 | f32 | RangedElevation (градусы) |
| 26 | f32 | RangedDanger |
| 27 | f32 | GunRange |
| 28 | i32 | GunClip |
| 29 | i32 | GunRate |
| 30 | f32 | GunAccuracy |
| 31 | string | GunSound: `""`, `cue` (банк Weapons) или `Bank/cue` |
| 32 | string | GunMuzzleEffect (hash, `""` = 0) |
| 33 | string | GunShellsEffect (hash) |
| 34 | f32 | TracerVelocity |
| 35 | string | NonTracerSprite: `""` = −1, `N` или `X/N` где X∈{B,C,D,E} добавляет 64/128/192/256 |
| 36 | string | TracerSprite (тот же синтаксис) |
| 37 | ConditionCollection | GunConditions |
| 38 | ExtRef<Model> | ProjectileModel |
| 39 | ConditionCollection | RangedConditions |
| 40 | f32 | Scale |
| 41 | ExtRef<Model> | Model (из её костей `attach0/attach1/effect0/effect1` берутся точки крепления; bbox — по позициям первой части) |
| 42 | i32 + N × AuraStorage | ауры |

### 5.3 `PhysicsEntityTemplateReader` (69 файлов `Data/PhysicsEntities/**`, `Data/AnimatedProps/**`)

| # | Тип | Поле |
|---|---|---|
| 1 | bool | Movable |
| 2 | bool | Pushable |
| 3 | bool | Solid |
| 4 | f32 | Mass |
| 5 | i32 | MaxHitPoints (≤0 → неразрушаемый) |
| 6 | bool | CanHaveStatus |
| 7 | i32 + N × (`i32 Elements`, `f32 Multiplier`, `f32 Modifier`) | сопротивления **без** bool StatusResistance |
| 8 | i32 + N × Gib | как у персонажа |
| 9 | string | HitEffect (→lower hash) |
| 10 | string | HitSound: `""`, `cue` или `Bank/cue` |
| 11 | string | GibTrailEffect (→lower hash) |
| 12 | `Object<Model>` | inline Model (эффекты частей в shared resources) |
| 13 | bool hasMesh → (`Object<List<Vector3>>`, `i32 n`, n × 3×i32) | коллизионный trimesh |
| 14 | i32 boxCount + boxCount × (`string` name, `Vector3 position`, `Vector3 sides`, `Quaternion orientation`) | используется только первый box; остальные читаются и отбрасываются |
| 15 | i32 + N × (`string` effect →hash, `Matrix` transform) | эффекты |
| 16 | i32 | lightCount — **должен быть 0** (иначе NotImplemented) |
| 17 | ConditionCollection | |
| 18 | string | ID (→lower, hash) |
| 19 | bool | hasAnimation — читается в try/catch: если поток кончился, считается false (во всех 69 файлах присутствует и = true) |
| 20 | (если 19) f32 | Radius |
| 21 | i32 + N × ModelProperties | `ExtRef<SkinnedModel>`, `f32 scale`, `Vector3 tint` |
| 22 | bool hasSkeleton → `ExtRef<SkinnedModel>` | скелет/клипы |
| 23 | i32 + N × (`string` bone, `string` effect) | AttachedEffects |
| 24 | 27 × AnimationSet | как у персонажа |

Путь шаблона берётся из AssetName (часть после `content\`, в нижнем регистре). Для событий с условием
Hit/Collision оригинал хотел уменьшить magnitude Damage-событий в 4 раза, но индексирует не тот элемент
(использует индекс условия вместо индекса события) — побочный эффект, не формат.

### 5.4 ConditionCollection / EventCollection / EventStorage

ConditionCollection: `i32 n` (**≤ 5**, массив фиксированный), n × EventCollection.

EventCollection:

| # | Тип | Поле |
|---|---|---|
| 1 | u8 | EventConditionType (флаги: Default=1, Hit=2, Collision=4, Damaged=8, Timer=0x10, Death=0x20, OverKill=0x40) |
| 2 | i32 | Hitpoints |
| 3 | i32 | Elements |
| 4 | f32 | Threshold |
| 5 | f32 | Time |
| 6 | bool | Repeat |
| 7 | i32 + N × EventStorage | |

EventStorage: `u8 EventType`, затем:

| EventType | Поля |
|---|---|
| 0 Damage | Damage(f32), `bool velocityBased` |
| 1 Splash | Damage(**i32** amount), `f32 radius` (> 0 обязательно) |
| 2 Sound | `i32 Banks`, `string cue` (hash), `f32 magnitude`, `bool stopOnRemove` |
| 3 Effect | `bool follow`, `bool worldAlign`, `string effect` (→lower hash) |
| 4 Remove | `i32 bounce` |
| 5 CameraShake | `f32 ttl`, `f32 magnitude`, `bool fromPosition` |
| 6 Decal | `i32 a`, `i32 b` (Decal = a + 8·b), `i32 scale` (TTL = 60 c) |
| 7 Blast | не реализовано (ошибка загрузки) |
| 8 Spawn | `string characterType` (hash; шаблон `Data/Characters/<type>` подгружается), `string idleAnim`, `string spawnAnim`, `f32 health`, `u8 Order`, `u8 ReactTo`, `u8 Reaction` (Order), `f32 rotation` (градусы), `Vector3 offset` |
| 9 Overkill | — |
| 10 SpawnGibs | `i32 startIndex`, `i32 endIndex` |
| 11 SpawnItem | `string item` (→lower hash; подгружается `Data/Items/Wizard/<item>`, кроме «random») |
| 12 SpawnMagick | `string MagickType`-имя (`random` → Revive) |
| 13 SpawnMissile | `string type` (→lower hash), `Vector3 velocity`, `bool facing` |
| 14 Light | `f32 radius`, `Vector3 diffuse`, `Vector3 ambient`, `f32 specular`, `u8 variation`, `f32 varAmount`, `f32 varSpeed` |
| 15 CastMagick | `string MagickType`-имя, `i32 n`, n × `i32 Elements` |
| 16 DamageOwner | Damage(f32), `bool velocityBased` |
| 17 Callback | ошибка загрузки (только из кода) |

### 5.5 SpecialAbility (у предметов и в AnimationAction SpecialAbility)

`string typeName` (класс из `Abilities.SpecialAbilities`), `string animation` (`""` → cast_self), `string effect`
(→lower hash, опционально), `i32 n`, n × `i32 Elements`. Конструктор выбирается по наличию элементов/эффекта.

### 5.6 Ability (ИИ-способности персонажа)

Общая часть: `string typeName` (подкласс Ability, без учёта регистра), `f32 cooldown`, `u8 Target`
(Self=1, Enemy=2, Friendly=3), `bool hasFuzzy` [+`string` fuzzy-выражение], `i32 n` + n × `string` Animations.
Затем:

| Тип | Доп. поля |
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
| Ranged | `f32 minRange`, `f32 maxRange`, `f32 elevation`, `f32 arc` (перезаписывается π), `f32 accuracy`, `i32 n`, n × `i32 weapon` |
| SpecialAbilityAbility | `f32 maxRange`, `f32 minRange`, `f32 angle`, `i32 weapon` |
| ThrowGrip | `f32 maxRange`, `f32 minRange`, `f32 elevation`, `i32 n`, n × Damage(**i32** amount). Читается не больше 4 Damage (при n > 4 — рассинхронизация в оригинале) |

### 5.7 BuffStorage / AuraStorage

BuffStorage: `u8 BuffType`, `u8 VisualCategory` (None, Offensive, Defensive, Special), `Vector3 color`, `f32 TTL`,
`string effect` (→lower hash, `""` = 0), затем:

| BuffType | Поля |
|---|---|
| 0 BoostDamage | Damage(f32) |
| 1 DealDamage | Damage(f32) (интервал 1 с) |
| 2 Resistance | `i32 Elements`, `f32 Modifier`, `f32 Multiplier`, `bool status` (**порядок Modifier/Multiplier обратный** относительно Resistance в шаблонах) |
| 3 Undying | — |
| 4 Boost | `f32 amount` |
| 5 ReduceAgro | `f32 amount` |
| 6 ModifyHitPoints | `f32 multiplier`, `f32 modifier` |
| 7 ModifySpellTTL | `f32 multiplier`, `f32 modifier` |
| 8 ModifySpellRange | `f32 multiplier`, `f32 modifier` |

AuraStorage: `u8 AuraTarget` (Friendly, FriendlyButSelf, Enemy, All, AllButSelf, Self, Type, TypeButSelf, Faction,
FactionButSelf), `u8 AuraType`, `u8 VisualCategory`, `Vector3 color`, `string effect` (→lower hash), `f32 TTL`,
`f32 radius`, `string targetTypes` (→lower, через запятую, каждый → hash), `i32 targetFaction`, затем:

| AuraType | Поля |
|---|---|
| 0 Buff | BuffStorage |
| 1 Deflect | `f32 strength` |
| 2 Boost | `f32 magnitude` |
| 3 LifeSteal | `f32 amount` |
| 4 Love | `f32 radius`, `f32 ttl` |

---

## 6. `Magicka.Physics.CollisionSkinReader`

Reader существует в коде, но **ни один файл контента его не использует** (в таблицах reader'ов нет).
Макет по коду: `i32 vertexCount`, vertexCount × Vector3, `i32 indexCount` (число **индексов**, треугольников =
indexCount/3), затем на треугольник 3 × i32, которые присваиваются в обратном порядке (I2, I1, I0) —
обратный winding по сравнению с треугольниками LevelModel/PhysicsEntityTemplate (I0, I1, I2).
Не проверен на данных (нет файлов).

---

## 7. Статус проверки

Парсер: Python, на базе `tools/research/xnb.py` (LZX), вспомогательные скрипты вне репозитория. Для каждого
файла читается главный объект и все shared resources и проверяется, что прочитано ровно столько байт,
сколько в распакованном теле.

| Reader (главный тип файла) | Файлов | Разобрано без остатка | Пример |
|---|---|---|---|
| Texture2DReader | 2296 | 2296 | `EffectTextures/ArcaneBlade.xnb` (Dxt1, 128², 8 мипов) |
| TextureCubeReader | 13 | 13 | `EffectTextures/burnCube.xnb` |
| Texture3DReader | 7 | 7 | `EffectTextures/ParticlesA.xnb` |
| EffectReader | 52 | 52 | `Shaders/AdditiveEffect.xnb` |
| ModelReader | 553 | 553 | `Models/AnimatedProps/Dungeons/dungeons_radio.xnb` (8 костей, 1 меш, EffectMaterial) |
| SkinnedModelReader | 327 | 327 | `Models/AnimatedProps/toxic_spore_animations_0.xnb` (15 костей, 5 клипов) |
| LevelModelReader | 132 | 132 | `Levels/Ruins/ru_s5.xnb` (13.7 МБ: 67 BiTree-корней, 2 части, 36 источников, лава, 12 триггеров, 42 локатора, навмеш 6818/12577), `Levels/BattleField/bf_s1.xnb` (вложенные анимированные части) |
| CharacterTemplateReader | 338 | 338 | `Data/Characters/Beastman_brute.xnb` (24 анимации) |
| ItemReader | 454 | 454 | `Data/items/NPC/acc_ammoBelt_0.xnb` (в т.ч. 1 несжатый файл) |
| PhysicsEntityTemplateReader | 69 | 69 | `Data/AnimatedProps/dungeons_radio.xnb` |
| BitmapFontReader | 66 | 66 | `Languages/eng/Font/Maiandra14.xnb` (198 глифов) |
| ListReader<String> | 1 | 1 | `Audio/SoundList.xnb` |

Вложенные reader'ы проверены транзитивно в составе этих файлов: VertexDeclaration/VertexBuffer/IndexBuffer,
String, ExternalReference, List<Vector3>, Dictionary<String,Object>, Single/Boolean/Vector3/Matrix, BasicEffect (6),
EffectMaterial (3), SkinnedModelBone, AnimationClip, SkinnedModelBasicEffect, SkinnedModelDeferredNormalMappedEffect (4 файла),
RenderDeferredEffect, RenderDeferredLiquidEffect, AdditiveEffect, LavaEffect (4 уровня), BiTreeModel (132).
Встречены: все подтипы AnimationAction, кроме SetItemAttach; все подтипы Ability, кроме ElementSteal и ZombieGrip;
BuffType 0–8; AuraType 0–1; EventType 0–5, 8, 10–16.

Итого: **4308 из 4308** файлов `.xnb` разобраны целиком без остатка.

Не покрыто данными (макет только по коду): ForceField (во всех уровнях 0 штук), AuraType 2–4
(Boost/LifeSteal/Love), EventType 6 (Decal) и 9 (Overkill, без полей), AnimationAction SetItemAttach,
Ability ElementSteal/ZombieGrip, CollisionSkinReader, ветка PhysicsEntityTemplate без анимационной части.

---

## 8. Открытые вопросы

1. **Семантика пар bool в MeshSettings** AnimatedLevelPart (предположительно visible / castShadows) — проверить по
   использованию в рендере части.
2. **Neighbour = 0xFFFF** в треугольниках навмеша — подтвердить, что это «нет соседа», и смысл Cost* (длина ребра
   или стоимость перехода через ребро к соседу).
3. **Соответствие слотов коллизии CollisionMaterials**: вывод сделан по индексу примитива в физике персонажа;
   стоит подтвердить на уровнях со снегом/водой.
4. **Порядок граней TextureCube** и мипов — принят стандартный (+X,−X,+Y,−Y,+Z,−Z); визуально не проверен.
5. **Цветовые пространства и диапазоны** параметров материалов (EmissiveAmount, NormalPower, Sharpness и т.д.)
   — нужны для шейдеров; это вопрос к рендеру, а не к формату.
6. **Technique у SkinnedModelBasicEffect**: значения enum'а и их соответствие техникам в
   `SkinnedModelBasicEffect.xnb`.
7. **Поведение при отсутствии shared-эффекта** у частей Model, а также порядок fix-up ссылок
   (кости ссылаются друг на друга через shared resources — нужен двухфазный резолв).
8. **Полные enum'ы, которые хранятся как строки** (Animations — 231 значение с алиасами, StatusEffects, MagickType,
   имена классов AnimationAction/Ability/SpecialAbility) — нужно выписать в отдельный справочник, чтобы ID
   совпадали с оригиналом.
9. **Factions, Order, ReactTo, CastType, Decal** — числовые значения enum'ов не выписаны здесь (хранятся как i32/u8).
10. **Нужна ли бит-в-бит совместимость `GetHashCodeCustom`** для строк, содержащих не-ASCII символы (алгоритм
    работает с UTF-16 code unit'ами — для ASCII результат совпадает с описанным).
11. Совместимость байткода `Content/Shaders/*.xnb` (fx_2_0) с MojoShader/FNA — не проверялась.
