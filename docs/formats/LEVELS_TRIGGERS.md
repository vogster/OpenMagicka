# Уровни, сцены и триггеры Magicka (формат и семантика)

> Clean-room документ для OpenMagicka. Описывает **форматы данных** (`.lvl`, XML сцен, диалогов,
> катсцен) и **наблюдаемое поведение** скриптовой системы оригинала (Magicka 1.10.4.2, XNA 3.1).
> Декомпилированный код сюда не переносится — только схемы, таблицы и прозаическое описание.
> Числа использования посчитаны скриптом по всем XML в `Content/Levels` Steam-версии
> (142 сцены, 48 `.lvl`, 18 файлов диалогов).

Связанные документы: [`../ENGINE_ANALYSIS.md`](../ENGINE_ANALYSIS.md) (общий обзор движка).

## 1. Общая картина

```
Level (.lvl)                      — глава / челлендж / versus-карта
 ├─ DialogCollection              — Dialog_*.xml (+ всегда Dialog_Fairy.xml)
 ├─ счётчики (int) и таймеры (float) — ОБЩИЕ для всех сцен уровня
 └─ GameScene ×N (scene XML)      — одна «комната», id = имя файла
     ├─ LevelModel (.xnb)         — геометрия + локаторы, триггер-зоны, анимированные части,
     │                              свет, эффекты, физобъекты, навмеш
     ├─ визуальные настройки      — fog/bloom/clouds/skymap/reverb…
     ├─ Ruleset (опционально)     — Survival / TimedObjective / Versus (+ Wave для Survival)
     ├─ Trigger ×M                — <trigger><if>…</if><then>…</then></trigger>
     └─ Interactable ×K           — <OnInteract locator=…>действия</OnInteract>
```

Скрипт — **декларативный набор триггеров**: каждый кадр каждый триггер с `autorun=true`
проверяет свои условия (`<if>`) и при выполнении ставит в очередь свои действия (`<then>`).
Триггеры без autorun вызываются действием `executeTrigger`, из AI-событий (`trigger=` у move и
т.п.), из `onDeath`/`onDamage`/`onPickup`/`onSkip` и т.д.

## 2. Идентификаторы, хэши и регистр

### 2.1 Хэш строк

Почти все ссылки по имени (сцены, локаторы, зоны, сущности, триггеры, счётчики, таймеры,
диалоги, эффекты, звуки, шаблоны) внутри движка превращаются в `int` функцией
«custom hash». Это **классический 32-битный хэш строки .NET Framework 2.0–3.5 (x86)**:
два аккумулятора с начальным значением 352654597 (= 5381·2¹⁶+5381); строка UTF-16
читается блоками по 2 символа как little-endian int32; на каждом шаге
`acc = ((acc << 5) + acc + (acc >> 27)) ^ block` (сдвиг вправо арифметический), блоки
попеременно идут в первый и второй аккумулятор; результат `acc1 + acc2 · 1566083941`
(всё по модулю 2³²). Для нечётной длины последний блок содержит завершающий NUL-символ
строки как старшую половину; цикл обрывается, когда осталось ≤ 2 символов.

Для OpenMagicka точное воспроизведение хэша нужно **только** для совместимости с бинарными
сохранениями чекпоинтов и сетевым протоколом оригинала. Для собственной реализации проще
использовать нормализованные (lower-case) строки как ключи.

### 2.2 Правила регистра

| Где | Правило |
|---|---|
| Имена элементов (`Scene`, `trigger`, `If`, `Then`, `Random`, `Looped`, `OnInteract`, все визуальные теги) | регистронезависимо (Ordinal IgnoreCase) |
| Тег действия/условия | регистронезависимое сравнение с **именем класса** (`spawn`, `Spawn`, `SPAWN`, `executetrigger`…). В игре встречаются все варианты: `spawnEffect` в 4 написаниях, `IDPresent` в 4 |
| Имена атрибутов действий/условий | регистронезависимо, сопоставляются с публичными записываемыми свойствами класса |
| Значения-строки у действий/условий | **приводятся к нижнему регистру** при чтении, затем хэшируются |
| Enum-значения | регистронезависимо; допускается числовая форма (`roomType="10"`) и список флагов через запятую (`factions="evil,friendly"`, `reactto="proximity,attack"`) |
| bool | `true`/`false` в любом регистре (.NET `bool.Parse`), иначе исключение |
| float | инвариантная культура (точка), допускаются `.1`, `-12` |
| Имена сцен | id сцены = имя файла сцены без расширения в нижнем регистре |
| Триггер-зоны в LevelModel | имена приводятся к нижнему регистру перед хэшем |
| Локаторы и свет в LevelModel | имена хэшируются **как есть** (без lower) → ссылки из XML (всегда lower) находят только локаторы с именами в нижнем регистре |
| `area=` у `<Start>` в `.lvl` и `spawnPoint=` | к значению дописывается индекс игрока `0..3`, затем хэш **без** lower |
| Исключения | атрибут `color` у `<SkyMap>` сравнивается чувствительно к регистру |

### 2.3 Пространства имён

- **Локатор** (`Locator`: матрица + радиус) и **триггер-зона** (`TriggerArea`: ориентированный бокс)
  приходят из `LevelModel` сцены. Локаторы ищутся сначала в модели, затем в иерархии
  AnimatedLevelPart (с учётом их текущей трансформации). Когда действию нужна «точка» по имени
  (`area=` у spawn/spawnPhys/setPosition…), сначала ищется локатор, затем триггер-зона —
  тогда берётся **случайная точка внутри бокса** со случайным поворотом по Y.
- Зона `any` — встроенная: «весь уровень» (все сущности EntityManager). В правилах
  Survival/Versus `any` в роли точки спавна означает случайную зону из списка ruleset-а.
- **Сущности** с уникальным `id` (атрибут `id` у spawn/spawnPhys/spawnItem…) ищутся глобально
  (`Entity.GetByID`). Аватары адресуются через `player1`..`player4` там, где это явно
  поддержано (setPosition, setFaction), либо через свой id.
- **Тип персонажа** (`type=`) — имя шаблона из `Data/Characters/<type>`; хэш имени = Type ID.
- **Триггеры** и **OnInteract** лежат в одном словаре сцены (`OnInteract` — по хэшу `locator`).
- **Счётчики и таймеры** — словари уровня (`Level`), переживают смену сцен.

## 3. Файл уровня `.lvl`

XML, корень `<Level>`. Список глав кампании **захардкожен** (порядок Wizard_Castle,
Mountaindale, Highlands, Havindr, Battlefield, EndOfWorld, Wizard_Castle2, Mines, Swamp,
Niflheim, Ruins, EndOfWorld2; Mythos: Tsar_WizardCastle, Tsar_Mountaindale, Tsar_Rlyeh;
Dungeons: `Challenges/ch_dungeons_ch1/ch2`). Челленджи и versus — все `*.lvl` в
`Levels/Challenges/` и `Levels/Versus/` (`ch_vietnam`, `ch_osotc`, `ch_dungeons_ch1/2` —
«сюжетные челленджи»). Локализованные имена уровней тоже частично захардкожены
(таблица имя файла → loc-ключ).

| Элемент | Исп. (из 48) | Содержимое / атрибуты | Семантика |
|---|---|---|---|
| `Name` | 48 | текст, loc-ключ (`#Chapter_04`) | обязателен; имя уровня |
| `Description` | 48 | loc-ключ | обязателен |
| `ShortDescription` | 2 | loc-ключ | по умолчанию = Description |
| `Loading` | 48 | путь к картинке загрузки | обязателен |
| `Start` | 48 | `scene` (имя сцены), `area` (префикс локаторов `area0..3`), `spawnPlayers` (bool, по умолч. true), `noItems` (bool — выдать всем `weapon_unarmed`), `forceCamera` (bool; по умолч. true в Campaign/Mythos), `spawnFairy` (bool, по умолч. true) | стартовая точка; игнорируется при загрузке из сохранения |
| `Dialogs` | 26 | путь к Dialog XML относительно `.lvl` (`../Dialog_Tutorial.xml` допустим) | без него — только Dialog_Fairy |
| `Scenes` / `Scene` | 48 / 141 | путь к XML сцены относительно `.lvl` | обязателен ≥1; иные дети `Scenes` → ошибка |
| `PreferredAvatar` | 4 | имя аватара (по умолч. `wizard`) | меню выбора персонажа |
| `AllowedAvatars` / `Avatar` | 2 / 8 | список имён | ограничение выбора |
| `AdditionalItems` / `Item` | 3 / 3 | `item` (ассет предмета), `bone` | доп. предметы, вешаемые на аватары |

Особенности:
- Меню читает `.lvl` потоковым парсером (только Name/Description/ShortDescription/Loading/
  Scenes/PreferredAvatar/AllowedAvatars, плюс у каждой сцены `<model>` и `<ruleset type>`);
  при запуске уровень читается целиком (DOM) — другой набор элементов (Start, Dialogs,
  AdditionalItems). Неизвестные элементы обоими парсерами молча игнорируются.
- Целостность контента: SHA-256 `.lvl`, всех XML сцен и их `.xnb` моделей XOR-ятся в «combined
  hash» (лицензирование DLC/античит + сопоставление уровней в сети). Для OpenMagicka не нужно.
- Ruleset главы определяется первой сценой с ненулевым `<ruleset type>`; `versus` в меню
  трактуется как «нет».
- Сцена `Havindr/havindr_s5HBF.xml` не подключена ни в один `.lvl` (и содержит недопустимый
  атрибут — см. §12).

## 4. XML сцены

Корень `<Scene>` (атрибуты `forceNavMesh` ×29, `forceCamera` ×12 — bool). Дети читаются по
порядку; **неизвестный элемент верхнего уровня → исключение** (комментарии пропускаются).

| Элемент | Исп. | Атрибуты / содержимое | Семантика |
|---|---|---|---|
| `Model` | 142 | текст: имя модели | `LevelModel` = `<dir сцены>/<имя в lower>.xnb`; обязателен |
| `Trigger` | 2519 | см. §5 | триггер |
| `OnInteract` | 286 | см. §5.5 | интерактивная точка |
| `Wave` | 316 | дети — блоки действий (§7.1); атрибут `value` (×36) игнорируется | волна Survival; допустима только **после** `<Ruleset type="survival">`, иначе — исключение «неизвестный элемент» |
| `Reverb` | 116 | `roomType` (enum RoomType, допускается число), `mix` (float) | реверберация |
| `Indoor` | 103 | текст bool | интерьер (влияет на погоду/свет) |
| `Bloom` | 90 | `threshold`, `multiplier` (или опечатка `multiplyer`), `blur` | пост-эффект |
| `Fog` | 49 | `start`, `end`, `color` = `r,g,b[,a]` (a по умолч. 1) | туман; наличие элемента включает его |
| `Clouds` | 33 | `intensity`, `scale` (`s` или `sx,sy`), `movement` (`x,y`), `texture` (путь от сцены), `worldRelative` (bool) | проекция облаков на направленный свет |
| `Ruleset` | 32 | `type` = survival / timedobjective / versus (+ свои атрибуты, §7) | правила режима |
| `KillPlanePosition` | 21 | текст float (по умолч. −50) | горизонтальная плоскость: всё, что ниже, уничтожается |
| `SkyMap` | 19 | текст: текстура (путь от сцены); `color` = `r,g,b` (регистрозависимо!) | небо |
| `Filter` | 1 | `brightness`, `contrast`, `saturation` | цветокоррекция |
| `Magnify` | 0 | текст float | начальный зум камеры |

## 5. Модель триггеров

### 5.1 Элемент `<trigger>`

| Атрибут | Исп. | Тип | По умолч. | Семантика |
|---|---|---|---|---|
| `id` | 2518 | строка → lower → хэш | 0 | ключ триггера; один триггер без id в игре есть |
| `autorun` | 1556 | bool | **true** | проверять условия каждый кадр |
| `repeat` | 2444 | float **или** bool | `false` | `false` = −1 (однократно); `true` = 0 (каждый кадр, пока условия истинны); число N ≥ 0 = кулдаун N секунд |

Любой другой атрибут у `<trigger>` → исключение. Значения в игре (у `<trigger>`): `repeat="false"` ×1643,
`"true"` ×616, число (0.5–32 с) ×185; `autorun="false"` ×1208, `"true"` ×348.

Дети: `<if>` (0..n), `<then>` (1). Прочие дети молча игнорируются. Если `<then>` указан
несколько раз, побеждает последний.

### 5.2 Условия: `<if>`

- Каждый `<if>` — **конъюнкция** (AND) своих детей-условий.
- Несколько `<if>` — **дизъюнкция** (OR): триггер срабатывает, если выполнен хотя бы один блок.
- Нет `<if>` или пустой `<if/>` → условие считается выполненным.
- У каждого условия есть базовый атрибут `invert` (bool): результат XOR-ится с ним
  (использован 353 раза). Исключение: `bossDead`/`bossHealth` при отсутствии босса
  возвращают false **до** применения invert.
- Сравнения: `compareMethod` ∈ `less | equal | greater` (= −1/0/1), смысл «значение в мире
  *op* значение в атрибуте». Встречено: equal ×1007, greater ×820, less ×260.

### 5.3 Действия: `<then>`

- Дети `<then>` — действия, тег = имя класса (§9). Неизвестные атрибуты **молча игнорируются**
  (в игре такой один: `spawn delayed=`).
- Базовый атрибут каждого действия — `delay` (float, секунды; задан 8426 раз).
- `<random>` (×245): группа альтернатив. Все `<random>`-группы одного `<then>` образуют
  набор вариантов; при срабатывании выбирается **одна** группа равновероятно, и к ней
  дописываются все обычные (не-random) действия `<then>`. Итоговый порядок: сначала
  действия выбранной группы, затем обычные — независимо от положения в XML.
- `<looped>` (×18): содержимое (действия и вложенные `<random>`) включается **только если
  текущее сохранение кампании «looped»** (повторное прохождение); решается при загрузке сцены.
- Комментарии допускаются внутри `<then>`/`<if>`/`<looped>`, но **не** внутри `<random>`
  (там комментарий будет принят за тег действия → ошибка загрузки).

### 5.4 Когда и как исполняется

Порядок кадра для текущей сцены (`GameScene.Update`):
1. свет/звуки сцены;
2. **все триггеры** сцены в порядке возрастания хэша id (не в порядке файла!), для каждого:
   уменьшить таймер повтора → если `autorun` и мы не сетевой клиент — попытка `Execute`
   → `Update` всех его действий (обработка delay-очередей);
3. ruleset (`Update` или `LocalUpdate` на клиенте);
4. модель уровня (анимированные части, вода и т.д.), эффекты;
5. сброс содержимого всех триггер-зон; зона `any` тут же пересчитывается по всем сущностям.
   Остальные зоны заново заполняются коллбеками физики до следующего кадра.

`Execute(sender, ignoreConditions)`:
- допускается, если `ignoreConditions`, или триггер ещё не срабатывал, или
  (`repeat ≥ 0` и таймер повтора истёк);
- проверяются условия (OR по `<if>`, AND внутри); при неуспехе — выход;
- `hasTriggered = true`, таймер повтора = `repeat`;
- выбирается вариант (`random`), на сервере рассылается сетевое сообщение с индексом варианта;
- каждому действию варианта вызывается `OnTrigger(sender)`.

Очередь действия: `OnTrigger` при пустой очереди ставит отсчёт = `delay`, иначе увеличивает
счётчик ожидающих запусков. На каждом `Update` отсчёт уменьшается на dt; пока он ≤ 0 и есть
ожидающие — выполняется `Execute`, к отсчёту прибавляется `delay` (повторные вызовы
разносятся с шагом delay). Значит действие с `delay=0` выполняется **в том же кадре**, что и
срабатывание. `clearDelayedActions` обнуляет очереди.

`sender` (инициатор): персонаж, вызвавший триггер (взаимодействие, AI-событие, `onDeath`…).
Его используют `startDialog position="interactor"` (по умолчанию!) и условие `interactorIs`;
`executeTrigger`/`startDialog` хранят FIFO отправителей на каждый ожидающий запуск.

`executeTrigger` вызывает цель **с проверкой условий** и правила repeat — однократный уже
сработавший триггер повторно не выполнится. Типичный паттерн «функции» в скриптах:
`autorun="false" repeat="true"` без `<if>`.

Сетевой режим: на клиенте autorun не работает; клиент получает от сервера «trigger execute»
(id сцены, id триггера, индекс варианта, handle отправителя) и выполняет те же действия
локально. Действия, создающие сущности, на клиенте ничего не делают и ждут отдельных
сообщений спавна.

### 5.5 `<OnInteract>` (Interactable)

Атрибуты (строгие — неизвестный атрибут → исключение):

| Атрибут | Исп. | Семантика |
|---|---|---|
| `locator` | 286 | имя локатора-точки (lower); id взаимодействия = хэш |
| `type` | 286 | InteractType: None, Pick_Up, Use, Next, Push, Pull, Boost, Examine, Talk, Interact, Activate, Attack, BreakFree — иконка/подпись |
| `enabled` | 0 | bool, по умолч. true; переключается `enablePoi`/`disablePoi` |
| `animHighlight` | 223 | список (через запятую) путей AnimatedLevelPart (`a/b/c`) для подсветки |
| `physHighlight` | 6 | список id физобъектов для подсветки |

Дети — действия (тот же синтаксис, что внутри `<then>`, включая `random`/`looped`). Условий нет,
autorun выключен. Срабатывает, когда игрок взаимодействует, а луч/отрезок курсора проходит
в пределах `Locator.Radius` от точки локатора (выбирается ближайший). Взаимодействия типа
Talk недоступны, пока играет «боевая» музыка (threat). Повтор не ограничен — блокировку
делают через `disablePoi`.

### 5.6 Счётчики и таймеры

- Счётчик: `int`, по умолчанию 0, создаётся при первом обращении. Действие `counter`
  (INC/DEC/SET), условие `counter`. 1020 изменений и 955 проверок в игре.
- Таймер: `float`, создаётся действием `addTimer` (повторное создание игнорируется),
  меняется действием `timer` (`value`, `paused`), проверяется условием `timer`. Два вида:
  обычные (растут всегда, пока уровень обновляется) и «played» (`playedTimer=true`, не растут
  в катсценах, на паузе и после конца игры). Таймер `TimedObjective` ruleset-а — обычный таймер.
- Оба хранилища принадлежат уровню, сохраняются в чекпоинте.

### 5.7 AI-события (дети `spawn`, `giveOrder`, `spawnGenericBoss`, `spawnCloneWizards`, `activateDispenser`, `avatarMove/PlayerN`)

Тег — тип события (регистронезависимо): `move`, `animation`, `face`, `kill`, `loop`.
Общие атрибуты: `delay`, `trigger` (триггер по завершении события, sender = персонаж).

| Событие | Атрибуты |
|---|---|
| `move` | `position` (`x,y,z` или имя локатора/зоны — зона → случайная точка; локатор задаёт и направление), `facingDirection` (bool или вектор), `speed` (по умолч. 1) |
| `animation` | `animation`, `idleAnimation`, `blendTime` |
| `face` | `target`, `speed` |
| `kill` | `remove` |
| `loop` | `type` = Loop / Reverse (зациклить список событий) |

## 6. Смена сцен и чекпоинты

### 6.1 Переходы

`changeScene`/`enterScene` (и `<Start>` уровня) задают **SpawnPoint**: id сцены + 4 локатора
`<spawnPoint>0..3` + флаг `spawnPlayers`. Процесс:
1. запустить переход рендера (`Fade`/`CrossFade`; `None` или время 0 → сразу);
2. в конце перехода (в фоновой загрузке): сбросить босс-файт и generic-полосу; текущая сцена
   «разрушается» — эффекты, физика, звуки (пауза при `saveNPCs`, иначе стоп); состояние
   AnimatedLevelPart запоминается в сцене; сущности сохраняются в сцене при `saveNPCs`,
   иначе удаляются; при смене на другую сцену — выгрузка её контента;
3. загрузка LevelModel новой сцены и `Initialize`: инициализация триггеров (они **не
   сбрасываются** — состояние сцен живёт до конца уровня), восстановление анимаций,
   ruleset, параметры рендера, первое создание физобъектов модели (только при первом
   входе), возврат сохранённых сущностей, сброс таймеров повтора триггеров, регистрация
   триггер-зон;
4. при `spawnPlayers`: игрок *i* ставится в локатор `<sp><i>`, если его нет — в ближайший
   младший найденный со смещением 2 м по X на каждый пропуск; затем привязка к земле
   лучом вниз; камера — в центр игроков; феи;
5. выполнение «стартовых действий» (реплей, §6.2), колбэк (у enterScene — запуск движения
   аватаров и `instantTrigger`), выход из перехода.

Если стартовых действий > 300 — показывается экран загрузки с прогрессом.

### 6.2 Чекпоинт и реплей

`triggerCheckpoint` делает снимок **состояния уровня**: текущая сцена, таймеры, счётчики,
и для каждой сцены: флаг первого входа, сохранённые сущности, глобальные и 3D-звуки,
состояния анимаций, состояние каждого триггера (таймер повтора, hasTriggered, очереди
действий) и **журнал выполненных действий** сцены. Плюс камера, диалоги (какие завершены,
позиции), музыка, разблокированные магии, оружие игроков. В Campaign/Mythos снимок
пишется в сейв (бинарно, с SHA-256 уровня и диалогов в заголовке).

Журнал: каждое выполненное действие добавляется в список сцены (повторное выполнение того
же `spawn` переносит его в конец; `spawnPhys`/`spawnAnimProp` дополнительно в отдельный
«world sync» список для сети). При загрузке чекпоинта журнал проигрывается как
**QuickExecute** — «мгновенная» версия действия. Колонка «Реплей» в §9:
`да` — QuickExecute = обычное выполнение; `особ.` — своя логика (spawn пересоздаёт
только выживших NPC с их HP, катсцены без анимации полос, свет без перехода, эффекты —
только непрерывные); `—` — при реплее ничего (эффект либо уже в сохранённом состоянии —
счётчики, сущности, — либо временный: звук, камера, диалог, титры).

`<ignore trigger="id"/>` внутри `triggerCheckpoint`: при загрузке эти триггеры сбрасываются в
исходное состояние (как будто не срабатывали), а их действия исключаются из реплея — так
делаются повторяемые после смерти энкаунтеры.

## 7. Rulesets

### 7.1 Survival (`<Ruleset type="survival" waves="N">`, 17 сцен — арены челленджей)

- `waves` — число волн (информативно; реальное = число `<Wave>`).
- Дочерний `<spawnAreas>` со списком `<area>имя</area>` — зоны появления для `any`.
- `<Wave>` (верхний уровень сцены, после `<Ruleset>`): **каждый** не-комментарий-ребёнок
  считается блоком «WaveActions» вне зависимости от имени тега (в игре `waveActions` ×1727,
  `waveAction` ×3, а также `GiveOrder` ×20, `playAnimation`, `playMusic`, `executeTrigger`
  прямо под `<Wave>` — их атрибуты, кроме `area`/`delay`, теряются, а тела пусты, т.е.
  они фактически **ничего не делают**).
- Блок: `area` (имя зоны; `any`/нет — случайная из spawnAreas, выбирается при старте волны и
  **принудительно подставляется** всем действиям блока, у которых есть свойство `Area`),
  `delay` (задержка от начала волны). Дети — действия; `<random>` запрещён (исключение).
- Волна закончена, когда все блоки отработали и в зоне `any` персонажей фракций
  EVIL|WILD|DEMON не больше, чем «чемоданов» (luggage). Затем пауза 5 с и следующая
  волна; после последней — победа. Полоса HP волны = суммарное HP заспавненных.
- Каждые 30 с, если на карте нет «чемоданов», появляется чемодан с магией (первый) или
  предметом (второй); максимум 2 за волну. Содержимое — из включённых Item/Magick-паков
  (паки захардкожены, привязаны к DLC; Magicka.Levels.Packs).
- Очки: множитель времени, урона, всплывающие числа (scrollScore) — детали в UI-коде.

### 7.2 TimedObjective (`type="timedobjective"`, 3 сцены — Vietnam)

Атрибуты: `texture` (рамка, по умолч. `UI/HUD/Dialog_Say`), `bordersize` (32), `timer`
(id таймера уровня с прошедшим временем, по умолч. `timestorage`). Дети:
- `<objective name="#loc" id="counter" max="N" score="S"/>` — строка HUD, значение берётся из
  счётчика `id`;
- `<timeBonus minTime maxTime minTimeBonus maxTimeBonus counter/>` — множитель очков, линейно
  от maxTimeBonus к minTimeBonus по прошедшему времени.
Ruleset только отображает; завершение уровня — триггером (`endGame`).

### 7.3 Versus (`type="versus"`, 12 сцен)

Конкретный режим (DeathMatch, Brawl, Pyrite, Krietor, King) выбирается настройками лобби,
не XML. Дети: `<Areas>` со списком `<area team="A|red|B|blue" tourney="true">имя</area>`
(зоны спавна, командные; Krietor берёт только `tourney="true"`), у Krietor — `<Tournament>`
со списком `<magick unlock="start|<сек>">MagickType</magick>`.

## 8. Катсцены

Есть два разных механизма:

1. **Катсцена в уровне** — действие `cutscene` … `endCutscene`: режим летербокса,
   UI выключен, рядом удаляются магии/барьеры/мины/торнадо, камера управляется
   `cameraMove`/`cameraRelease`, диалоги в нижней полосе. Пропуск доступен, только если
   задан `onSkip`: при пропуске выполняется триггер onSkip (с проверкой условий), обычно
   с `clearDelayedActions` и `endCutscene`.
2. **Катсцены карты кампании** — `Levels/Cutscenes.xml`, `Levels/Tsar/Cutscenes.xml`,
   `Challenges/Dungeons_Cutscenes.xml`, `Dungeons2_Cutscenes.xml`; показываются в меню между
   главами (панорама по карте мира с озвучкой):

```
<Cutscenes>
  <Cutscene id="1,2">                 ← индексы глав (через запятую); у Dungeons id нет
    <camera>
      <zoom time="0.0">1.05</zoom>     ← ключи зума
      <position time="8.0">1090,1400</position>  ← ключи позиции x,y на карте
    </camera>
    <Dialog subtitles="#Cutscene_02">waveBank/CutS_cut02</Dialog>  ← [bank/]cue, банк по умолч. WaveBank
  </Cutscene>
</Cutscenes>
```

Ключи сортируются по времени, интерполяция smoothstep между соседними; длительность =
максимум времён последних ключей. Нужен минимум один ключ каждого вида.

## 9. Каталог действий (113)

> В пространстве имён `Triggers.Actions` 116 файлов: абстрактная база `Action`, два enum-а
> (`OperationAction` = Add/Remove/Set, `WizardAction` = StaffLightOn/Off) и **113** конкретных
> действий. Аналогично `Triggers.Conditions`: база + **24** условия. Тег сопоставляется только
> с прямыми наследниками базового класса.

Тег — имя класса (регистр любой). Колонки: **Исп.** — число вхождений во всех сценах
(включая волны и OnInteract), **Файлов** — в скольких сценах. Атрибуты: `имя`:тип[=умолчание].
Тип `id` — строка, приводимая к lower и хэшируемая (ссылка по имени); `str` — строка (lower)
без хэша; `vec3` — `"x,y,z"`; `rgba` — `"r,g,b,a"` (0..1). Умолчание не указано = нуль типа
(0, false, пусто, член enum со значением 0 — для CompareMethod это `equal`). `⁰` — атрибут поддерживается, но в игре не встречается.
Базовый атрибут `delay` (float) есть у всех. Значения enum: Order = None/Idle/Attack/Defend/
Flee/Wander/Panic; ReactTo — флаги None/Attack/Proximity; Transitions = None/Fade/CrossFade;
Factions — флаги EVIL, WILD, FRIENDLY, DEMON, UNDEAD, HUMAN, WIZARD, NEUTRAL(=все), PLAYER0..3,
TEAM_RED, TEAM_BLUE, PLAYERS; Banks = WaveBank/Music/Ambience/UI/Spells/Characters/Footsteps/
Weapons/Misc/Additional/AdditionalMusic; EndGameCondition = Victory/LevelComplete/Defeat/
VersusPlayer/VersusTeam/EightyeightMilesPerHour/ChallengeExit/EndOffGame/Disconnected/
ToBeContinued; LightTransitionType = None/Linear/Flicker.

Фильтр «по зоне» у kill/remove/overkill/releaseGrip/giveOrder: персонаж в зоне подходит,
если `type="any"`, **или** совпал тип, **или** пересеклась фракция (OR!). У `damage` логика
обратная: пропуск, только если не `any`, тип не совпал **и** фракция не пересеклась (то же
множество). Если задан `id` — работает только по этой сущности, зона игнорируется.

| # | Тег | Исп. | Файлов | Атрибуты | Семантика | Реплей |
|---|---|---|---|---|---|---|
| 1 | `spawn` | 5188 | 119 | `area`:id, `snapToNavMesh`:bool, `targetArea`:id, `speed`:float=1, `type`:id, `nr`:int=1, `health`:float=1, `id`:id, `dialog`:id, `order`:Order=Attack, `reactTo`:ReactTo=Attack,Proximity, `reaction`:Order=Attack, `trigger`:id⁰, `neverRemove`:bool, `animation`:Animations, `idleAnimation`:Animations, `spawnAnimation`:Animations, `priorityTarget`:id, `priorityAbility`:int=-1⁰, `onDeath`:id, `onDamage`:id, `forceDraw`:bool, `meshId`:int=-1, `forceCamera`:bool, `forceNavMesh`:bool, `cannotDieWithoutExplicitKill`:bool⁰ | Создаёт `nr` NPC шаблона `Data/Characters/<type>` в локаторе/зоне `area` (зона → случайная точка), задаёт AI-приказ (`order`/`reactTo`/`reaction`), `id`, диалог, анимации появления/idle, `onDeath`/`onDamage`-триггеры; дочерние элементы — скрипт AIEvent. Реплей: пересоздаёт только выживших с сохранённым HP. | особ. |
| 2 | `executeTrigger` | 1420 | 99 | `trigger`:id | Вызывает триггер `trigger` текущей сцены **с проверкой** его условий и repeat; передаёт инициатора (sender) дальше. | — |
| 3 | `counter` | 1020 | 93 | `name`:id, `action`:CounterAction, `value`:int | Меняет счётчик уровня `name`: `action` = INC/DEC/SET на `value`. | — |
| 4 | `giveOrder` | 978 | 96 | `order`:Order, `reactTo`:ReactTo, `trigger`:id, `reaction`:Order=Attack, `animation`:Animations, `idleAnimation`:Animations, `factions`:Factions, `id`:id, `type`:id, `area`:id, `targetArea`:id, `speed`:float=1, `priorityTarget`:id, `priorityAbility`:int=-1⁰, `explicit`:bool | Меняет AI-приказ NPC `id` либо всем NPC в зоне `area`, отобранным по `type` ИЛИ `factions` (`explicit` — точное равенство фракции); дочерние AIEvent; `animation`/`idleAnimation`. | особ. |
| 5 | `playAnimation` | 889 | 92 | `name`:id, `speed`:float=1, `start`:float=-1, `end`:float=-1, `loop`:bool, `children`:bool=true, `resume`:bool | Проигрывает анимацию AnimatedLevelPart `name` (путь `parent/child`), диапазон `start`..`end` (−1 = весь клип), `speed`, `loop`, `children`, `resume`. | — |
| 6 | `playSound` | 814 | 130 | `id`:id, `cue`:id, `area`:id, `volume`:float, `radius`:float, `apply3D`:bool | Играет XACT-cue `cue` (`bank/cue`, банк по умолчанию Ambience). С `id` — именованный звук сцены (stopSound/setVolume); с `area` — 3D-звук в локаторе (`radius`, `apply3D`). `volume` — переменная cue `Volume`. | — |
| 7 | `startDialog` | 515 | 95 | `dialog`:id, `forceOnScreen`:bool, `position`:id="interactor" | Показывает следующий `<interact>` диалога `dialog`. `position`: `interactor` (по умолч., инициатор триггера), id сущности, имя локатора, `x,y` (экран) или `x,y,z` (мир). | — |
| 8 | `spawnPhys` | 388 | 39 | `area`:id, `object`:str, `onDeath`:id, `onDamage`:id, `id`:id | Создаёт физобъект `Data/PhysicsEntities/<object>` в локаторе `area`; `onDeath`/`onDamage` — триггеры (для разрушаемых). Реплея нет — состояние хранится как сохранённые сущности. | — |
| 9 | `avatarMove` | 361 | 106 | — | Задаёт аватарам AIEvent-скрипт. Дети: `<Player1>`..`<Player4>` (слот по последней цифре; занятый слот → следующий свободный) или `<PlayerID name="<id аватара>">` (резолвится при Initialize сцены); внутри — `move/animation/face/kill/loop`. Иные дети → исключение. | — |
| 10 | `spawnEffect` | 310 | 28 | `effect`:id, `name`:id, `area`:id, `range`:float | Запускает эффект частиц `effect` в локаторе `area` под именем `name`; `range` > 0 — луч, укорачиваемый до препятствия/щита. | особ. |
| 11 | `stopEffect` | 309 | 56 | `name`:id | Останавливает именованный эффект сцены `name` (из LevelModel или spawnEffect). | особ. |
| 12 | `cutscene` | 285 | 83 | `skipBarMove`:bool, `killDialogs`:bool=true, `onSkip`:id | Режим катсцены: летербокс, UI выкл, магии/снаряды рядом очищаются. `onSkip` — триггер при пропуске (без него пропуск недоступен), `killDialogs`, `skipBarMove`. | особ. |
| 13 | `disableInput` | 254 | 87 | — | Блокирует управление игроков (ControlManager.LimitInput). | да |
| 14 | `setPosition` | 252 | 40 | `id`:id, `facingDirection`:bool, `area`:id | Телепортирует сущность `id` (или `player1`..`player4`) в локатор `area` с привязкой к земле; `facingDirection` — развернуть по локатору. | да |
| 15 | `cameraMove` | 250 | 85 | `lockInput`:bool, `magnification`:float=-1, `time`:float=1, `target`:id, `offset`:vec3, `interpolation`:CameraInterpolation=Interpolated | Плавно ведёт камеру к локатору `target` (+`offset`) за `time`; `magnification` > 0 — зум; `lockInput`; `interpolation`. | — |
| 16 | `enableInput` | 239 | 81 | — | Снимает блокировку управления. | да |
| 17 | `endCutscene` | 230 | 80 | `skipBarMove`:bool | Выход из режима катсцены. | особ. |
| 18 | `cameraRelease` | 222 | 79 | `time`:float=1 | Возвращает камеру к слежению за игроками за `time`. | — |
| 19 | `startEffect` | 197 | 46 | `name`:id | Запускает именованный эффект сцены `name` (пустое имя → сгенерированный id). | да |
| 20 | `fade` | 194 | 56 | `color`:rgba, `fadeIn`:bool, `override`:bool, `time`:float | Затемнение экрана: `fadeIn=true` — уход в `color` за `time`, `false` — выход; `override` отменяет fade смены сцены. | — |
| 21 | `remove` | 191 | 60 | `factions`:Factions, `id`:id, `type`:id, `area`:id, `force`:bool | Убирает (kill + terminate) сущность `id` либо персонажей в зоне `area` по `type`/`factions`; `force` снимает «неубиваемость». | да |
| 22 | `playMusic` | 136 | 103 | `cue`:id, `focusValue`:float | Включает музыку `cue` (`bank/cue`, банк по умолч. Music), `focusValue`. | — |
| 23 | `triggerCheckpoint` | 123 | 81 | `spawnPoint`:id, `spawnFairy`:bool=true, `saveToDisk`:bool=true⁰, дети: `ignore`/`trigger` | Чекпоинт: снимок состояния уровня, точки респауна `spawnPoint0..3`, воскрешение/лечение игроков, фея (`spawnFairy`), запись на диск (`saveToDisk`). Дочерние `<ignore trigger="…"/>` — триггеры, сбрасываемые при загрузке. | — |
| 24 | `disablePoi` | 111 | 46 | `id`:id | Выключает `<OnInteract locator="id">`. | особ. |
| 25 | `clearDelayedActions` | 101 | 59 | `trigger`:id | Отменяет отложенные (ещё не выполненные по delay) действия триггера `trigger`, либо `all`/пусто — всех, `allbutthis` — всех, кроме текущего. | — |
| 26 | `changeScene` | 94 | 77 | `scene`:id, `transition`:Transitions=Fade, `transitionTime`:float=1, `spawnPlayers`:bool, `spawnPoint`:id, `saveNPCs`:bool | Переход в сцену `scene` (имя файла сцены) к локаторам `spawnPoint0..3`; `transition`/`transitionTime`; `spawnPlayers`; `saveNPCs` — сохранить NPC уходящей сцены. | — |
| 27 | `cameraShake` | 90 | 20 | `area`:id, `magnitude`:float, `time`:float | Тряска камеры `magnitude`/`time`, глобально или с затуханием от локатора `area`. | — |
| 28 | `cameraMagnify` | 84 | 58 | `magnification`:float=1, `time`:float=1 | Зум камеры к `magnification` за `time`. | — |
| 29 | `spawnItem` | 78 | 46 | `area`:id, `item`:id, `ignoreTractorPull`:bool, `attachTo`:id, `nr`:int=1, `physicsEnabled`:bool, `onPickup`:id, `id`:id | Создаёт предмет `Data/Items/Wizard/<item>` в `area`; `physicsEnabled`, `ignoreTractorPull`, `attachTo` (AnimatedLevelPart), `onPickup`-триггер, `id`. | — |
| 30 | `stopSound` | 77 | 30 | `id`:id, `instant`:bool | Останавливает именованный звук `id` (`instant` — без хвоста). | — |
| 31 | `kill` | 71 | 22 | `factions`:Factions, `id`:id, `type`:id, `area`:id | Убивает сущность `id` либо персонажей в зоне `area` по `type`/`factions` (снимает CannotDieWithoutExplicitKill). | да |
| 32 | `disableLight` | 61 | 10 | `id`:id, `effect`:LightTransitionType, `time`:float | Выключает свет(а) LevelModel `id` (список через запятую), `effect` None/Linear/Flicker за `time`. | особ. |
| 33 | `stopMusic` | 56 | 39 | — | Останавливает музыку. | — |
| 34 | `spawnDispenser` | 52 | 1 | `area`:id, `model`:Dispensers, `types`:id, `amount`:str, `time`:float, `id`:id⁰, `active`:bool=true | Создаёт «раздатчик» `model` (SpiderHole/GoblinTent/SpiderEggs) в `area`, выпускающий `types` (список) в количестве `amount` (список) с периодом `time`; `active`. | да |
| 35 | `setFaction` | 49 | 6 | `id`:id, `faction`:Factions, `area`:id, `action`:OperationAction, `type`:id | Меняет фракцию (`action` Add/Remove/Set, `faction`) у `id`/`player1..4` или у персонажей зоны `area` по `type`. | да |
| 36 | `enableElement` | 48 | 6 | `element`:Elements | Туториал: разрешает элемент `element`. | да |
| 37 | `unlockAchievement` | 42 | 28 | `achievement`:str | Выдаёт достижение `achievement`. | — |
| 38 | `overkill` | 40 | 8 | `factions`:Factions, `id`:id, `type`:id, `area`:id | Overkill (разрыв на куски) для `id` или персонажей зоны по `type`/`factions`. | да |
| 39 | `spawnMagick` | 38 | 28 | `area`:id, `immovable`:bool=true, `magick`:MagickType, `onPickup`:id, `id`:id, `timeOut`:float⁰ | Кладёт книгу магии `magick` в `area`; `immovable`, `timeOut`, `onPickup`, `id`. | — |
| 40 | `setHint` | 35 | 9 | `appendController`:bool, `id`:str, `animation`:HintAnimations, `position`:Position=BottomRight | Туториальная подсказка: loc-ключ `id` (`appendController` → суффикс `_key`/`_pad`), `animation`, `position`. | — |
| 41 | `removeHint` | 34 | 10 | — | Убирает туториальную подсказку. | да |
| 42 | `spawnBoss` | 32 | 22 | `id`:id, `boss`:str, `prop`:bool, `generic`:bool, `template`:str⁰, `delayed`:bool, `area`:id, `healthAppearDelay`:float⁰, `freezeTime`:float⁰, `healthBarWidth`:float=0.8, `meshId`:int=-1, `onDeath`:id, `onDamage`:id | Создаёт босса `boss` (жёстко закодированный класс по имени; `prop` — PropBoss из PhysicsEntities; `generic` — GenericBoss из Characters) в `area`; `delayed` — не начинать бой; полоса HP (`healthBarWidth`, `healthAppearDelay`, `freezeTime`). | да |
| 43 | `cameraLock` | 30 | 18 | `time`:float=1, `area`:id | Фиксирует камеру на боксе триггер-зоны `area` за `time`. | — |
| 44 | `enablePoi` | 29 | 13 | `id`:id | Включает `<OnInteract locator="id">`. | особ. |
| 45 | `printDebug` | 28 | 8 | `entityPos`:str, `break`:bool⁰, `counterName`:str=""⁰, `message`:str="" | Отладочный вывод; в релизе пустышка. | — |
| 46 | `unlockMagick` | 26 | 15 | `magickType`:MagickType | Разблокирует магию `magickType` (в Versus — всем игрокам) + всплывающий текст. | да |
| 47 | `enterScene` | 25 | 16 | `scene`:id, `transition`:Transitions=Fade, `transitionTime`:float=1, `spawnPoint`:id, `moveTarget`:id, `facingDirection`:bool, `instantTrigger`:id, `trigger`:id, `ignoreTriggers`:bool, `saveNPCs`:bool | Как changeScene, плюс после входа аватары идут к локаторам `moveTarget0..3` (`facingDirection`, `ignoreTriggers` на время пути), по прибытии — триггер `trigger`; `instantTrigger` выполняется сразу после входа. | — |
| 48 | `spawnAnimProp` | 25 | 2 | `area`:id, `object`:str, `onDeath`:id⁰, `onDamage`:id⁰, `id`:id, `animation`:str="", `damageable`:str, `gravity`:str | Создаёт анимированный проп `Data/AnimatedProps/<object>` в `area`, стартовая `animation`, `damageable`/`gravity` (строки-bool), `id`. | — |
| 49 | `timer` | 25 | 5 | `name`:id, `value`:float, `paused`:bool | Устанавливает значение (`value`) и/или паузу (`paused`) таймера `name`. | да |
| 50 | `activateBoss` | 24 | 16 | — | Стартует бой с боссом, заспавненным с `delayed=true`. | да |
| 51 | `enableLight` | 22 | 5 | `id`:id, `effect`:LightTransitionType, `time`:float | Включает свет(а) `id` с `effect`/`time`. | особ. |
| 52 | `endGame` | 22 | 19 | `type`:EndGameCondition, `freezeGame`:bool=true, `phony`:bool | Завершает игру с исходом `type` (Victory/LevelComplete/Defeat/…); `freezeGame`, `phony`. | — |
| 53 | `disableElement` | 21 | 9 | `element`:Elements | Туториал: запрещает элемент `element`. | да |
| 54 | `resumeAnimation` | 21 | 13 | `name`:id, `speed`:float=1, `length`:float, `children`:bool=true, `loop`:bool | Продолжает анимацию AnimatedLevelPart `name` (`length`, `speed`, `loop`, `children`). | — |
| 55 | `stopAnimation` | 21 | 9 | `name`:id, `children`:bool=true | Останавливает анимацию AnimatedLevelPart `name` (`children`). | — |
| 56 | `damage` | 20 | 7 | `factions`:Factions, `id`:id, `type`:id, `area`:id, `source`:id, `ignoreShields`:bool=true, дети: `amount`/`attackProperty`/`damage`/`element`/`magnitude` | Наносит урон (дочерние `<damage attackProperty element amount magnitude/>`) сущности `id` или персонажам зоны по `type`/`factions`; `source` — локатор-источник (направление, проверка щитов), `ignoreShields`. | да |
| 57 | `bossTitle` | 19 | 18 | `title`:id, `subTitle`:id, `displayTime`:float=-1, `fadeIn`:float=1, `fadeOut`:float=1 | Показывает титр босса: loc-ключи `title`/`subTitle`, `displayTime` (−1 → 2 с), `fadeIn`/`fadeOut`. | — |
| 58 | `setDialog` | 19 | 7 | `dialog`:id, `id`:id | Назначает персонажу `id` диалог `dialog`. | да |
| 59 | `disableCastType` | 18 | 4 | `type`:CastType | Туториал: запрещает тип каста `type`. | да |
| 60 | `cameraBias` | 17 | 8 | `bias`:vec3, `time`:float=20 | Смещает точку камеры на `bias` за `time`. | — |
| 61 | `foundSecretArea` | 17 | 15 | `id`:str | Профиль: найден секрет `id` (статистика/ачивки). | — |
| 62 | `bossMessage` | 15 | 6 | `message`:str | Передаёт строку `message` в скриптовую логику текущего босса. | да |
| 63 | `enableCastType` | 15 | 2 | `type`:CastType | Туториал: разрешает тип каста `type`. | да |
| 64 | `killDialogs` | 14 | 9 | — | Закрывает все активные диалоги. | да |
| 65 | `addTimer` | 13 | 8 | `name`:id, `value`:float, `playedTimer`:bool | Создаёт таймер `name` со значением `value` (если уже есть — игнор); `playedTimer` — «игровой» таймер (стоит на паузе в катсценах). | да |
| 66 | `assignItem` | 13 | 3 | `id`:id, `player`:int, `area`:id, `animation`:Animations | Выдаёт предмет `Data/Items/Wizard/<id>` игроку `player` (1–4), аватару в зоне `area` или всем; `staff_default`/`weapon_default` — вернуть стандартные; `animation`. | особ. |
| 67 | `endBossFight` | 13 | 8 | — | Сбрасывает бой с боссом (полосу HP). | да |
| 68 | `foundMoose` | 13 | 13 | `id`:str | Профиль: найден лось `id`. | — |
| 69 | `setDialogHint` | 12 | 3 | `trigger`:id, `scale`:float⁰, `size`:vec2⁰, `element`:Elements, `magick`:MagickType, `custom`:int⁰, `id`:id | Туториальная подсказка элемента/магии (`element`/`magick`/`custom`), текст — loc-ключ `id`, `trigger`, `scale`, `size`. | — |
| 70 | `killedAPony` | 11 | 3 | `id`:str | Профиль: убит пони `id`. | — |
| 71 | `telemetryTutorialStep` | 11 | 1 | `tutorialName`:str="", `stepName`:str="" | Телеметрия туториала (для OpenMagicka — no-op). | да |
| 72 | `scrollScore` | 10 | 3 | `value`:str, `ttl`:float, `area`:id, `offset`:str | Всплывающее число `value` в зоне/локаторе `area` + `offset` (`x,y,z`), время `TTL`. | да |
| 73 | `magick` | 8 | 5 | `magickType`:MagickType, `id`:id, `position`:vec3⁰ | Исполняет магию `magickType` от лица `id` (или в `position`). | да |
| 74 | `activateDispenser` | 7 | 1 | `order`:Order⁰, `reactTo`:ReactTo⁰, `id`:id⁰, `type`:Dispensers, `area`:id | Активирует раздатчик `id` или раздатчики типа `type` в зоне `area`. | да |
| 75 | `addCameraEffect` | 7 | 7 | `effect`:id | Включает визуальный эффект камеры `effect`. | да |
| 76 | `displayTitleInLevel` | 7 | 5 | `noTranslation`:bool⁰, `title`:id, `subTitle`:id, `displayTime`:float=-1, `fadeIn`:float=1, `fadeOut`:float=1, `textAlignment`:str⁰ | Титр в уровне: `title`/`subTitle` (loc-ключи; `noTranslation` — сырой текст), `displayTime`, `fadeIn`/`fadeOut`, `textAlignment` L/C/R. | — |
| 77 | `highlightAnim` | 6 | 1 | `name`:id, `time`:float=1 | Подсвечивает AnimatedLevelPart `name` на `time`. | — |
| 78 | `setVolume` | 6 | 1 | `id`:id, `volume`:float | Меняет громкость именованного звука `id`. | — |
| 79 | `conditionalUnlockAchievement` | 5 | 5 | `achievement`:str, `victim`:id | Достижение с условием, только 4 жёстко заданных: `thisismagicka`/`therecanbeonlyone` (последний атакер `victim` — локальный игрок), `drivenmad`/`breezedthrough` (никто не умирал на уровне). | — |
| 80 | `highlightPhys` | 5 | 1 | `id`:id, `time`:float | Подсвечивает физобъект `id` на `time`. | — |
| 81 | `setMusicFocus` | 5 | 4 | `id`:id, `radius`:float, `clearTo`:float=1 | Музыкальный «фокус» от расстояния до локатора `id` (`radius`, `clearTo`). | да |
| 82 | `wizardControl` | 5 | 5 | `action`:WizardAction | Свет посоха: `action` StaffLightOn/StaffLightOff. | да |
| 83 | `disableUI` | 4 | 4 | — | Скрывает HUD. | да |
| 84 | `rollCredits` | 4 | 4 | `file`:id | Запускает титры из файла `file`. | да |
| 85 | `cameraFollow` | 3 | 3 | `id`:id | Камера следует за сущностью `id`. | — |
| 86 | `disablePush` | 3 | 3 | — | Туториал: запрещает push. | да |
| 87 | `flash` | 3 | 3 | `duration`:float=0.5 | Белая вспышка экрана на `duration`. | да |
| 88 | `spawnElemental` | 3 | 1 | `id`:id⁰, `area`:id, `snapToNavMesh`:bool, `nr`:int, `proximity`:float | Создаёт `nr` «яиц элементалей» в `area` (`proximity` — радиус пробуждения, `snapToNavMesh`). | особ. |
| 89 | `spawnGenericBoss` | 3 | 3 | `area`:id, `targetArea`:id⁰, `speed`:float=1⁰, `type`:id, `health`:float=1⁰, `id`:id, `dialog`:id⁰, `order`:Order=Attack, `reactTo`:ReactTo=Attack,Proximity, `reaction`:Order=Attack, `trigger`:id⁰, `neverRemove`:bool, `animation`:Animations, `idleAnimation`:Animations⁰, `spawnAnimation`:Animations⁰, `priorityTarget`:id⁰, `priorityAbility`:int=-1⁰, `onDeath`:id⁰, `onDamage`:id⁰, `forceDraw`:bool⁰, `meshId`:int=-1⁰, `forceCamera`:bool⁰, `forceNavMesh`:bool⁰, `delayed`:bool⁰, `healthAppearDelay`:float⁰, `freezeTime`:float⁰, `healthBarWidth`:float=0.8, `cannotDieWithoutExplicitKill`:bool | Spawn-подобный (те же атрибуты, что у spawn) + регистрация как босс с полосой HP. | — |
| 90 | `enablePush` | 2 | 1 | — | Туториал: разрешает push. | да |
| 91 | `endUnequipWeaponsSection` | 2 | 1 | `animation`:Animations | Конец секции «без оружия»; `animation` — анимация атаки аватаров. | — |
| 92 | `removeItemFromAvatars` | 2 | 1 | `item`:id | Снимает предмет `item` с аватаров (возвращает предмет шаблона). | да |
| 93 | `spawnCloneWizards` | 2 | 2 | `area`:id, `snapToNavMesh`:bool⁰, `targetArea`:id⁰, `speed`:float=1⁰, `nr`:int⁰, `health`:float=1⁰, `id`:id⁰, `dialog`:id⁰, `order`:Order=Attack, `reactTo`:ReactTo=Attack,Proximity, `reaction`:Order=Attack⁰, `trigger`:id⁰, `neverRemove`:bool⁰, `animation`:Animations⁰, `spawnAnimation`:Animations⁰, `priorityTarget`:id⁰, `priorityAbility`:int=-1⁰, `onDeath`:id⁰, `onDamage`:id⁰ | Создаёт клонов каждого живого игрока (его шаблон/цвет) в локаторах `area0..3`, id `CloneWizard<i>`; AI-атрибуты как у spawn. | да |
| 94 | `telemetryTutorialEnd` | 2 | 1 | — | Телеметрия (no-op). | да |
| 95 | `activateHealthBar` | 1 | 1 | — | Показывает настроенную generic-полосу (HealthBar). | да |
| 96 | `addItemToAvatars` | 1 | 1 | `item`:id, `bone`:str | Прикрепляет предмет `item` к кости `bone` всех аватаров. | да |
| 97 | `beginDefaultAvatarSection` | 1 | 1 | — | Флаг PlayState.DefaultAvatarSection = true (секция со стандартным аватаром). | — |
| 98 | `beginUnequipWeaponsSection` | 1 | 1 | — | Начало секции «без оружия». | — |
| 99 | `cameraPosition` | 1 | 1 | `magnification`:float=-1, `target`:id, `offset`:vec3 | Мгновенно ставит камеру в локатор `target` (+`offset`), `magnification`. | — |
| 100 | `endDefaultAvatarSection` | 1 | 1 | — | DefaultAvatarSection = false. | — |
| 101 | `healthBar` | 1 | 1 | `displayName`:id⁰, `normalizedHealth`:float=1⁰, `displayHealth`:float⁰, `time`:float, `type`:GenericHealthBarTypes, `power`:float=1.1⁰, `alpha`:float⁰, `count`:float⁰, `graphicsType`:GenericHealthBarGraphics, `initialTimerDelay`:float, `isScaled`:bool, `isColoredRed`:bool⁰, `showDisplayName`:bool⁰, `hasAnimatedSprite`:bool, `animationOffsetPositionY`:float=16⁰, `fadeTime`:float, `onEndTrigger`:id | Настраивает generic-полосу: `type` HP/TimerIncreasing/TimerDecreasing/Counter, `time`/`count`, графика, `onEndTrigger` и др. | да |
| 102 | `removeAllStatusEffects` | 1 | 1 | — | Снимает все статусы/ауры/щиты/левитацию/заклинания со всех персонажей. | да |
| 103 | `removeDialogHint` | 1 | 1 | — | Убирает dialog-hint туториала. | да |
| 104 | `removeHealthBar` | 1 | 1 | — | Скрывает generic-полосу. | да |
| 105 | `telemetryTutorialBegin` | 1 | 1 | `tutorialName`:str="", `stepName`:str="" | Телеметрия (no-op). | да |
| 106 | `telemetryTutorialSkip` | 1 | 1 | — | Телеметрия (no-op). | да |
| 107 | `teleport` | 1 | 1 | `effect`:id, `sound`:id, `id`:id, `target`:id | Телепортирует сущность `id` в локатор `target` с эффектом `effect` и звуком `sound`. | да |
| 108 | `disableDark` | 0 | 0 | — | Туториал: выключает затемнение. | да |
| 109 | `enableDark` | 0 | 0 | — | Туториал: включает затемнение экрана. | да |
| 110 | `enableUI` | 0 | 0 | — | Показывает HUD. | да |
| 111 | `physAnimation` | 0 | 0 | `id`:id⁰, `name`:id⁰, `speed`:float=1⁰, `start`:float⁰, `end`:float⁰, `children`:bool=true⁰ | Привязывает анимацию AnimatedLevelPart `name` к разрушаемому физобъекту `id`. | да |
| 112 | `releaseGrip` | 0 | 0 | `factions`:Factions⁰, `id`:id⁰, `area`:id⁰, `type`:id⁰ | Освобождает персонажей, схваченных NPC `id` или NPC зоны. | да |
| 113 | `removeCameraEffect` | 0 | 0 | `effect`:id⁰ | Снимает эффект камеры `effect` (пусто — все). | да |

## 10. Каталог условий (24)

Базовый атрибут `invert` (bool) есть у всех.

| # | Тег | Исп. | Файлов | Атрибуты | Семантика |
|---|---|---|---|---|---|
| 1 | `counter` | 955 | 95 | `name`:id, `compareMethod`:CompareMethod, `value`:int | Сравнивает счётчик уровня `name` с `value` (`compareMethod`; LESS = счётчик < value). |
| 2 | `present` | 887 | 111 | `includeInvisible`:bool=true, `area`:id, `type`:id, `nr`:int, `compareMethod`:CompareMethod | Число живых персонажей шаблона `type` (`any` — всех) в зоне `area` сравнивается с `nr`; `includeInvisible=false` вычитает невидимых. |
| 3 | `dialogDone` | 259 | 72 | `dialog`:id, `interactIndex`:int=-1 | Диалог `dialog` завершён хотя бы раз и сыграно > `interactIndex` interact-ов (−1 = любое). |
| 4 | `idPresent` | 253 | 34 | `area`:id="any", `id`:id | Сущность с уникальным `id` присутствует и жива в зоне `area` (по умолч. `any`). |
| 5 | `triggered` | 203 | 51 | `trigger`:id | Триггер `trigger` уже срабатывал (HasTriggered). |
| 6 | `factionPresent` | 111 | 34 | `includeInvisible`:bool=true, `area`:id, `factions`:Factions, `nr`:int, `compareMethod`:CompareMethod | Число живых персонажей с фракцией из `factions` (флаги) в зоне `area` сравнивается с `nr`. |
| 7 | `characterHealth` | 35 | 8 | `id`:id, `compareMethod`:CompareMethod, `health`:float | Доля HP (0..1; мёртв/нет → 0) персонажа `id` сравнивается с `health`. |
| 8 | `interactorIs` | 24 | 3 | `id`:id | Инициатор триггера (sender) имеет уникальный id `id`. |
| 9 | `robeInUse` | 24 | 3 | `name`:id, `player`:int | Игрок `player` (1–4; 0 = любой) играет робой-шаблоном `name`. |
| 10 | `timer` | 13 | 7 | `name`:id, `compareMethod`:CompareMethod, `value`:float | Таймер уровня `name` сравнивается с `value` (EQUAL — точное float-равенство). |
| 11 | `bossHealth` | 10 | 8 | `compareMethod`:CompareMethod, `value`:float | Нормированное HP босса сравнивается с `value` (нет босса → false, даже с invert). |
| 12 | `noCutsceneActive` | 9 | 4 | — | Нет активной катсцены. |
| 13 | `hasAchievement` | 6 | 1 | `name`:str | У игрока есть достижение `name`. |
| 14 | `anyPlayerHasDied` | 4 | 2 | — | Кто-то из игроков умирал на этом уровне. |
| 15 | `characterCasting` | 4 | 1 | `id`:id="", `castType`:CastType, `element`:Elements | Персонаж `id` сейчас кастует заклинание типа `castType`, содержащее элемент `element`. |
| 16 | `bossDead` | 3 | 3 | — | Босс мёртв (нет босса → false). |
| 17 | `dialogActive` | 3 | 1 | `dialog`:id | Диалог `dialog` сейчас показывается (`any`/пусто — любой message box). |
| 18 | `hasDLC` | 3 | 1 | `type`:DLC | Установлен DLC `type` (WizardHat/Vietnam). |
| 19 | `playerCount` | 2 | 2 | `compareMethod`:CompareMethod, `nr`:int | Число игроков сравнивается с `nr`. |
| 20 | `playersHaveStatusEffect` | 2 | 1 | `statusEffect`:StatusEffects | Хоть один игрок имеет **любой** статус-эффект (атрибут `statusEffect` фактически игнорируется). |
| 21 | `playerHasElement` | 1 | 1 | `element`:Elements | Хоть у одного игрока в очереди заклинания есть элемент `element`. |
| 22 | `dialogHintDone` | 0 | 0 | — | Туториальный dialog-hint закрыт. |
| 23 | `isCampaignLooped` | 0 | 0 | — | Кампания пройдена повторно (NG+); вне Campaign — исключение. |
| 24 | `physicsPresent` | 0 | 0 | `onlyDamagable`:bool⁰, `area`:id="any"⁰, `nr`:int⁰, `compareMethod`:CompareMethod⁰ | Число физобъектов (`onlyDamagable` — только разрушаемых) в зоне `area` сравнивается с `nr`. |

## 11. Диалоги (`Levels/Dialog_*.xml`)

Подключаются через `<Dialogs>` в `.lvl`; `Levels/Dialog_Fairy.xml` догружается **всегда**
(общий словарь — коллизия id приводит к ошибке). Схема строгая: неизвестные элементы и
атрибуты на уровнях Dialog/Interact/Message → исключение.

```
<Dialogs>
  <Dialog id="horse" iconText="EXAMINE" repeatFrom="0">      ← 615 шт.
    <interact lock="false" focusOwner="false">              ← 728; один «показ»
      <message character="owner" type="say" facing="false"> ← 759; одна реплика-окно
        <text sound="wavebank/POI_generic" anim="none" TTL="2">#PoI_Horse1</text>  ← 907
      </message>
    </interact>
  </Dialog>
</Dialogs>
```

| Уровень | Атрибут | По умолч. | Семантика |
|---|---|---|---|
| Dialog | `id` | — | lower → хэш; ссылаются `startDialog`, `spawn dialog=`, `setDialog`, `dialogDone`, `dialogActive` |
| Dialog | `iconText` | Talk | InteractType для иконки при наведении на NPC |
| Dialog | `repeatFrom` | **0** | после последнего interact перейти к этому индексу (и сбросить счётчики повторов). Отрицательное — диалог «кончается» |
| Interact | `lock` | false | заморозить физику и ввод на время показа |
| Interact | `focusOwner` | true | камера/фокус на говорящем |
| Interact | `count` | 1 | сколько раз показывать этот interact перед переходом к следующему; ≤0 — бесконечно (в игре не используется) |
| Message | `character` | — | id говорящего; `owner` — владелец диалога (NPC/инициатор) |
| Message | `type` | Say | единственное значение MessageType |
| Message | `facing` | true | говорящий поворачивается к собеседнику |
| Message | `forceOnScreen` | true | держать окно в пределах экрана |
| Message | `width` | 600 | ширина переноса строк, px |
| text | `sound` | — | `[bank/]cue`, банк по умолч. Characters |
| text | `anim` | talk_casual0 | анимация говорящего (enum Animations) |
| text | `TTL` | −1 | автопродолжение через N с; −1 — ждать ввода |
| text | (текст) | — | `#locKey[,arg1,arg2…]` — loc-ключ; в переводе плейсхолдеры `#N;` подставляют loc-строку аргумента N, `#key;` — loc-строку key (выделяются цветом) |

Семантика показа: каждый `startDialog` (или клик по NPC с диалогом) берёт **следующий**
interact (`Pop`) и проигрывает его сообщения по очереди (ввод игрока или TTL). Диалог
помечается «завершённым», когда доигран любой его interact. Если диалог уже показывается —
повторный старт игнорируется. Одновременно до 8 окон; в катсцене текст идёт в нижнюю полосу.
`dialogDone interactIndex=k` истинно, если диалог завершён и сыграно больше k interact-ов.
Состояние диалогов (позиция, завершённость) входит в чекпоинт.

Причуды: у `<message>` дети перебираются без пропуска комментариев (комментарий внутри
message → ошибка), имя тега `<text>` не проверяется (любой элемент = реплика).

## 12. Причуды и ловушки парсинга (для совместимости)

1. Порядок обработки триггеров — по возрастанию хэша id, а не по порядку в файле. Скрипты,
   полагающиеся на порядок внутри одного кадра, редки, но возможны.
2. Неизвестный тег действия/условия или текстовый узел внутри `<then>`/`<if>` → падение загрузки.
   Неизвестный **атрибут** действия/условия → молча игнорируется; у `<trigger>`, `<OnInteract>`,
   `<Dialog>`, `<interact>`, `<message>` и в `.lvl/<Scenes>` — исключение. Пример: `havindr_s5HBF.xml`
   с атрибутом `repeatitions` у OnInteract не загрузился бы (сцена не подключена).
3. Под `<Wave>` любой тег трактуется как блок волны (см. §7.1) — `GiveOrder` прямо в волне
   не выполняется.
4. `startDialog position="x,y,z"` — из-за ошибки третья компонента записывается в Y, Z остаётся 0.
5. `activateDispenser` с зоной ищет раздатчик по своему `id`, а не перебирает сущности зоны
   (фактически работает только при заданном id).
6. `playersHaveStatusEffect` игнорирует атрибут `statusEffect` — проверяет любой из 9 статусов.
7. Условие `timer` с `equal` сравнивает float точно — на практике почти никогда не истинно.
8. Хэширование: строки атрибутов приводятся к lower, но имена локаторов в LevelModel — нет;
   `area` в `.lvl` Start не приводится к lower.
9. `repeat` принимает и bool, и число; любые другие значения — лог/ассерт и −1.
10. Survival-блок волны насильно перезаписывает свойство `Area` у всех своих действий
    (через рефлексию по имени свойства `Area`).
11. Tutorial/телеметрия/профиль/ачивки (`telemetry*`, `foundMoose`, `killedAPony`,
    `foundSecretArea`, `unlockAchievement`, `printDebug`) не влияют на геймплей — можно
    реализовать заглушками.
12. На сетевом клиенте действия спавна возвращаются сразу; авторитет — сервер.
13. Максимум 65535 действий на уровень (сквозная нумерация handle для сети/сейвов).

## 13. Рекомендации для OpenMagicka

- Парсер: XML DOM, сравнение имён без учёта регистра, таблица «тег → фабрика действия»
  со схемой атрибутов (из §9/§10), неизвестные атрибуты — warning, а не ошибка.
- Хранить строковые ключи (lower) вместо хэшей; хэш §2.1 понадобится только при импорте
  оригинальных сейвов.
- Сохранить семантику: OR между `<if>`, repeat (−1/0/N), очередь delay, sender, random+обычные,
  реплей журнала при чекпоинте с `ignore`-списком, общие счётчики/таймеры уровня, состояние
  сцен на весь уровень.
- Детерминированный порядок триггеров: для совместимости — по хэшу; для простоты — по файлу
  (риск минимален, но стоит проверить на кампании).

## 14. Открытые вопросы

1. Какие сущности попадают в «сохранённые сущности» чекпоинта (`EntityStateStorage`) и как это
   сочетается с реплеем `spawn` (живые NPC пересоздаются из журнала) — нет ли дублей с
   NPC, сохранёнными через `saveNPCs`.
2. Регистр имён локаторов внутри `LevelModel` `.xnb` — все ли в lower (иначе часть ссылок в
   оригинале «не работает» и это надо воспроизвести). Нужна проверка декодером xnb.
3. Точная семантика `forceCamera`/`forceNavMesh` у сцены и NPC, `IsInvisible` в условиях
   `present`/`factionPresent`.
4. Детали `cameraMove`/`cameraLock`/`cameraBias` (функции интерполяции, приоритеты) — в
   `MagickCamera`, вне этого документа.
5. Логика конкретных боссов (`spawnBoss boss=<имя>`, `bossMessage`) — жёстко закодирована в
   `Magicka.GameLogic.Entities.Bosses`; нужен отдельный документ со списком имён и сообщений.
6. Формула очков Survival/TimedObjective и таблицы лидеров — описать отдельно вместе с HUD.
7. `TutorialManager`: семантика `enableDark`, dialog-hints, ограничений элементов/кастов.
8. Бинарный формат чекпоинта в сейве (порядок полей выше описан, но не побайтно); нужен ли
   OpenMagicka импорт оригинальных сохранений.
9. Поведение `Trigger` без `id` (id = 0): возможна коллизия ключей при нескольких таких.
10. Версии Survival/Versus в сетевой игре: какие действия выполняются через `LocalUpdate`.
