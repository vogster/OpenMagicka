# OpenMagicka — анализ оригинального движка Magicka

Источник: Steam-установка `D:\SteamLibrary\steamapps\common\Magicka` (AppID 42910),
`Magicka.exe` версии **1.10.4.2** (SHA-256 `a896e05a…12ee7`, MVID `11bf844c-2882-4ec8-aed8-39169d72b637`).

> Правило проекта: **clean-room**. Код оригинала изучается только чтобы понять форматы
> и поведение; декомпилированный код в репозиторий не копируется. Ассеты игры в репозиторий
> не кладутся: пользователь указывает путь к своей купленной копии.

## 1. Стек оригинала

| Слой | Технология |
|---|---|
| Рантайм | .NET Framework 3.5 (CLR 2.0), x86-only (`32-bit-required`) |
| Фреймворк | **XNA Framework 3.1** (`Microsoft.Xna.Framework`, `.Game`) |
| Рендер-движок | `PolygonHead.dll` — собственный движок Arrowhead: deferred shading, свет, частицы, пост-эффекты, BiTree-модели уровней, bitmap-шрифты |
| Анимация | `XNAnimation.dll` 0.7 (open-source, скиннинг + AnimationClip) |
| Физика | `JigLibX.dll` 0.0.2 (open-source порт JigLib), плюс свои детекторы коллизий в `Magicka.Physics` |
| Звук | XACT (`.xgs` / `.xsb` / `.xwb`) |
| Ввод | XNA GamePad + DirectInput (`Microsoft.DirectX.DirectInput`) + `XInputHelper.dll` |
| Онлайн/сервисы | Steam (`SteamWrapper.dll`, `steam_api.dll`), Paradox (`PdxConnect`, `pops_api`), GameSparks — **для open-реализации не нужны** |

P/Invoke минимален: `QueryPerformanceCounter/Frequency`, `GetCurrentThreadId`, `LoadCursorFromFile`, `TranslateMessage`, `MapVirtualKey`.

## 2. Структура кода `Magicka.exe` (≈2054 типа, без обфускации)

Ключевые пространства имён:

- `Magicka` — `Game : Microsoft.Xna.Framework.Game`, `Program`, глобальные enum'ы (`Elements`, `StatusEffects`, `Factions`, `Animations`, `CollisionMaterials`…), `SharedContentManager`.
- `Magicka.GameLogic.GameStates` — `GameStateManager`, `CompanyState` → `MenuState` → `PlayState`, `LoadingScreen`.
- `Magicka.GameLogic.Spells` — ядро геймплея: `Spell`, `SpellManager`, `SpellTree`/`SpellNode`, `MagickTree`, `CastType`, `SpellType`, `StatusEffect`; эффекты: `ProjectileSpell`, `SpraySpell`, `LightningSpell`, `ShieldSpell`, `PushSpell`, `RailGunSpell`.
- `Magicka.GameLogic.Entities` (+ `Abilities`, `SpecialAbilities` ×114, `Bosses` ×35, `Buffs`, `CharacterStates` ×27, `Items`, `AnimationActions` ×29) — персонажи, предметы, боссы, магики.
- `Magicka.Levels` — `Level`, `GameScene`, `LevelModel`, `Liquid`/`Water`/`Lava`, `ForceField`, волны, рулсеты (`SurvivalRuleset`, `TimedObjectiveRuleset`).
- `Magicka.Levels.Triggers` — `Actions` (113 типов) и `Conditions` (24 типа): скриптовая система уровней.
- `Magicka.AI` — `Agent`, `AIManager`, `AgentStates`, `Arithmetics` (fuzzy-логика), `Messaging`.
- `Magicka.PathFinding` — `NavMesh`, `AnimatedNavMesh`, `NavMeshOctree`.
- `Magicka.Physics` — `PhysicsManager`, `CharacterBody`, кастомные детекторы (capsule/hollow sphere/static mesh), `IceMesh`, `WaterMesh`.
- `Magicka.Graphics` — `MagickCamera`, `EffectManager`, `DecalManager`, `Octree`, `TracerMan`, шрифты.
- `Magicka.Audio` — `AudioManager`, банки, reverb `RoomType`.
- `Magicka.Network` (84 типа) — сетевой кооп/versus поверх Steam.
- `Magicka.GameLogic.UI*` — меню, HUD (Gold/Iron/Moba стили), виджеты, UI-анимации.
- `Magicka.Localization`, `Magicka.Storage` (сейвы + `Encryption`), `Magicka.Achievements`.
- Не нужно для open-реализации: `Magicka.WebTools.*` (GameSparks, Paradox-телеметрия, монетизация, GDPR), `Magicka.DRM`.

`PolygonHead.dll`: `RenderManager`, `Scene`, `Camera`, `Effects` (deferred, liquid, lava, additive, skinned, свет point/spot/directional, bloom, fog, skymap, переходы), `Lights`, `ParticleEffects` (Continuous/Pulse эмиттеры, Half-векторы), `Models.BiTreeModel`, `PackedContent` (не используется в Steam-сборке — файлы лежат россыпью).

Шейдеры встроены как скомпилированный байткод DX9 Effect (`*EffectCode` классы + FieldRVA), плюс 51 `.xnb` в `Content/Shaders`.

## 3. Контент (`Content/`, ~1.7 ГБ)

| Папка | Размер | Что внутри |
|---|---|---|
| `Audio` | 652 МБ | XACT: `magicka.xgs`, 11 × `.xsb`, 13 × `.xwb`, `SoundList.xnb` |
| `UI` | 317 МБ | текстуры, json-анимации/спрайтшиты |
| `Levels` | 263 МБ | `.lvl` (XML кампании/уровня) → сцены `*.xml` (скрипт) + `*.xnb` (геометрия `LevelModel`) |
| `Models` | 85 МБ | `Model` / `SkinnedModel` + текстуры |
| `Languages` | 75 МБ | 8 языков, `*.loctable.xml` (Excel SpreadsheetML) + `BitmapFont` |
| `Effects` | 7 МБ | ~900 XML-описаний частиц |
| `Data` | 2.8 МБ | шаблоны персонажей/предметов/физобъектов (`.xnb`), JSON магазина/событий |
| `Shaders`, `EffectTextures`, `FBX` | — | эффекты, текстуры эффектов |

### 3.1 Формат XNB

- Все 4308 файлов: заголовок `XNB` + платформа `w` + **версия 4** (XNA 3.1). 4299 сжаты (флаг `0x80`), 9 без сжатия.
- Сжатие: **LZX** (окно 64 КБ, 2^16), фреймы по 32 КБ; заголовок блока: 2 байта размера, либо `0xFF` + 2 байта размера фрейма + 2 байта размера блока.
- Исследовательский декодер: [`tools/research/xnb.py`](../tools/research/xnb.py). Проверен: все 4308 файлов распознаны без ошибок, полная распаковка совпадает с заявленным размером (в т.ч. файл уровня 6 МБ).
- После распаковки: 7-битный счётчик type reader'ов → список (имя + версия int32) → shared resources → объект.

### 3.2 Используемые ContentTypeReader'ы (число файлов)

Основной тип файла:

| Reader | Файлов | Источник |
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

Вложенные: `RenderDeferredEffectReader` (710), `SkinnedModelBone/BasicEffect/AnimationClipReader` (XNAnimation), `BiTreeModelReader` (132), `RenderDeferredLiquidEffectReader` (53), `AdditiveEffectReader` (33), `LavaEffectReader`, `SkinnedModelDeferredNormalMappedEffectReader`, `CollisionSkinReader`, стандартные XNA (`VertexBuffer`, `IndexBuffer`, `VertexDeclaration`, `ExternalReference`, `List`, `Dictionary`, примитивы).

Итого для open-реализации нужно написать **~12 нестандартных reader'ов** + стандартные XNA 3.1 (формат v4 отличается от XNA 4.0/MonoGame v5).

### 3.3 Данные в открытом виде (XML/JSON)

Большая часть геймдизайна — текстовая, что сильно упрощает задачу:

- `Levels/*.lvl` — кампания: стартовая сцена, диалоги, список сцен.
- Сцены `Levels/<Chapter>/*.xml` — модель, освещение/bloom/fog/reverb и **триггеры**: `<trigger><if>…</if><then>…</then></trigger>`. Самые частые действия: `spawn`, `counter`, `waveActions`, `Move`, `executeTrigger`, `giveOrder`, `playAnimation`, `playSound`, `startDialog`, `spawnPhys`, `cameraMove`, `cutscene`… Теги **регистронезависимы** (встречаются `playmusic`/`playMusic`).
- `Levels/Dialog_*.xml` — диалоги (`Dialog` → `interact` → `message` → `text sound= anim= TTL=`).
- `Effects/**/*.xml` — эффекты частиц (`ContinuousEmitter`, параметры спреда/жизни/размера, ключевые кадры).
- `Languages/<lang>/*.loctable.xml` — строки (`#Key` → текст), формат Excel 2003 XML.
- `SaveData/*.sav` — **не зашифрованы**: `Settings/campaign/Profile/ch_*.sav` — `BinaryWriter` с префиксом-строкой версии, `DLCData.sav` — XML. `Encryption` применяется только к `pops.sav` (аккаунт Paradox, нам не нужен). Подробно: [engine/ARCHITECTURE.md](engine/ARCHITECTURE.md).
- `col.dat` — текст, первая строка = число открытых цветов игрока.

## 4. Рекомендуемая архитектура OpenMagicka

**C# / .NET 10 LTS + FNA** (open-source реимплементация XNA 4.0). Решение и обоснование: [ADR-0001](adr/0001-stack.md), сеть: [ADR-0002](adr/0002-network.md).

- API близок к XNA 3.1 — понятная модель для переноса поведения.
- FNA через MojoShader исполняет **DX9 effect bytecode** — есть шанс использовать оригинальные шейдеры из ассетов пользователя без переписывания.
- FAudio/FACT поддерживает **XACT**-банки (нужно проверить совместимость версии банков XNA 3.1).
- Кроссплатформенность (Windows/Linux/macOS, Vulkan/D3D11/Metal через SDL_GPU/FNA3D).
- XNB v4 читаем своими reader'ами (LZX — портировать из исследовательского декодера).
- JigLibX и XNAnimation — open-source, можно портировать на FNA, а не писать с нуля (проверить лицензии).

Слои: `OpenMagicka.Content` (XNB v4 + reader'ы + XML-парсеры) → `OpenMagicka.Engine` (рендер deferred, свет, частицы, камера, аудио, ввод) → `OpenMagicka.Game` (заклинания, сущности, AI, триггеры, уровни, UI) → `OpenMagicka.Launcher` (поиск установки Steam, проверка ассетов). Сеть: `OpenMagicka.Net` (протокол и транспорты) и `OpenMagicka.Server` (лобби и relay).

## 5. Детальные спецификации

| Документ | Тема |
|---|---|
| [formats/XNB_READERS.md](formats/XNB_READERS.md) | бинарные макеты всех нестандартных reader'ов |
| [formats/LEVELS_TRIGGERS.md](formats/LEVELS_TRIGGERS.md) | .lvl, сцены, 24 условия и 113 действий, диалоги, катсцены |
| [gameplay/SPELLS.md](gameplay/SPELLS.md) | элементы, очередь, типы заклинаний, урон, статусы, 34 магики |
| [engine/ARCHITECTURE.md](engine/ARCHITECTURE.md) | потоки, рендер-пайплайн, шейдеры, физика, звук, ввод, сейвы |
| [engine/ENTITIES_AI.md](engine/ENTITIES_AI.md) | сущности, состояния персонажа, AI, навмеш, сеть |
| [engine/RISK_CHECKS.md](engine/RISK_CHECKS.md) | проверка рисков: шейдеры в FNA, XACT на FAudio, Linux |
| [adr/0001-stack.md](adr/0001-stack.md) | решение: стек .NET 10 + FNA |
| [adr/0002-network.md](adr/0002-network.md) | решение: сетевая модель и транспорт |

## 6. Ключевые риски

- XACT v45 на FAudio работает, кроме xWMA: его нужно перекодировать при установке ([RISK_CHECKS](engine/RISK_CHECKS.md) §2).
- Оригинальные шейдеры: 46/51 работают после патча байткода; 5, включая основной материал уровней `RenderDeferredEffect`, падают в MojoShader ([RISK_CHECKS](engine/RISK_CHECKS.md) §1).
- FNA не поддерживает `EffectPool`: общие параметры (View/Projection/Time) выставлять в каждый эффект.
- Лицензия JigLibX не подтверждена (XNAnimation — Ms-PL).
- Ключи локализации/звуков/эффектов — хэш `String.GetHashCode` .NET 2.0–3.5 x86; нужна своя реализация.
- Linux/macOS: поиск файлов без учёта регистра (код и файлы расходятся в регистре).

Открытые вопросы по каждой подсистеме — в конце соответствующего документа.
