# Система заклинаний и боя (спецификация для реимплементации)

Источник поведения: `Magicka.exe` 1.10.4.2 (см. [`ENGINE_ANALYSIS.md`](../ENGINE_ANALYSIS.md)).
Область: `Magicka.GameLogic.Spells` (+ `SpellEffects`), `Elements`, `StatusEffects`, `Damage` /
`DamageCollection5` / `AttackProperties` / `DamageResult`, `Defines`, `SpellManager` / `SpellTree` /
`MagickTree`, а также связанная логика урона и статусов в `Character`, `Barrier`, `Shield`,
`SpellMine`, `Railgun`, `LightningBolt`, `Blast`.

> **Clean-room.** Ниже только правила, формулы, константы и идентификаторы. Код оригинала не
> переносится. Формулы записаны в собственной нотации.
>
> Нотация: `E, W, C, F, Z, A, L, S, I, T, P` — *магнитуды* (количество) элементов Earth, Water,
> Cold, Fire, Lightning (Z), Arcane, Life, Shield, Ice, sTeam, Poison в итоговом заклинании.
> `√x` — квадратный корень, `min1(x) = min(1, x)`, `trunc` — отбрасывание дробной части.
> «Тик» = одно применение `DamageCollection5` к цели.

---

## 1. Элементы

### 1.1 Флаги `Elements` (int, `[Flags]`)

| Индекс | Элемент | Флаг | Клавиша (по умолч.) | Геймпад (2 шага) | Получение |
|---:|---|---|---|---|---|
| 0 | Earth (=Physical) | `0x001` | D | Down → Left | базовый |
| 1 | Water | `0x002` | Q | Left → Up | базовый |
| 2 | Cold | `0x004` | R | Right → Up | базовый |
| 3 | Fire | `0x008` | F | Right → Down | базовый |
| 4 | Lightning | `0x010` | A | Left → Down | базовый |
| 5 | Arcane | `0x020` | S | Up → Right | базовый |
| 6 | Life | `0x040` | W | Up → Left | базовый |
| 7 | Shield | `0x080` | E | Down → Right | базовый |
| 8 | Ice | `0x100` | — | — | Water + Cold |
| 9 | Steam | `0x200` | — | — | Water + Fire |
| 10 | Poison | `0x400` | — | — | **игроку недоступен** (только NPC/предметы/магики) |

Индекс = log2(флаг). Особые значения: `None = 0`, `All = Magick = 0xFFFF` (маркер «это магик»,
индекс 11 в `Defines.ElementIndex`, 15 в `Spell.ElementIndex`).

Составные маски (используются в правилах ниже):

| Имя | Значение | Состав |
|---|---|---|
| `Basic` | `0x0FF` | 8 базовых |
| `PhysicalElements` | `0x101` | Earth, Ice |
| `Beams` | `0x060` | Arcane, Life |
| `ShieldElements` | `0x0E0` | Arcane, Life, Shield |
| `Defensive` | `0x0B0` | Lightning, Arcane, Shield |
| `StatusEffects` | `0x64E` | Water, Cold, Fire, Life, Steam, Poison |
| `Instant` | `0x371` | Earth, Lightning, Arcane, Life, Ice, Steam |
| `InstantPhysical` | `0x171` | Earth, Lightning, Arcane, Life, Ice |
| `InstantNonPhysical` | `0x270` | Lightning, Arcane, Life, Steam |
| `Offensive` | `0xFF3F` | всё, кроме Life и Shield |

`Defines.NR_OF_ELEMENTS = 11`. Локализация: `#element_earth` … `#element_poison` (порядок индексов).

### 1.2 Ввод элементов

- Клавиатура: каждая клавиша элемента имеет собственный кулдаун **0.125 с** (`MagickaElement`);
  набор включённых элементов управляется `ElementManager`/`TutorialManager` (обучение может
  отключать элементы). В `ElementManager` нет Poison.
- Геймпад: `SpellTree` — дерево из 5 направлений (Center/Up/Down/Left/Right), глубина 2; каждому
  листу назначен базовый элемент (таблица выше). Буфер направлений игрока (`InputQueue`) проходит
  по дереву; при отсутствии потомка первый элемент буфера отбрасывается; найденный элемент
  возвращается, буфер очищается. Клавиатура просто эмулирует нужную пару направлений.
- Бинды: `KeyboardBindings` (Water, Life, Shield, Cold, Lightning, Arcane, Earth, Fire, Cast, CastSelf,
  Boost, Move, Block, Shift, Inventory, PrevMagick, NextMagick). Каст: ПКМ = Cast, Shift = модификатор
  Area/Weapon, СКМ = CastSelf, Пробел = Boost (каст магика), ЛКМ = Move/атака.

### 1.3 Побочные эффекты при призыве элемента (Avatar)

Если игрок призывает **Lightning, будучи Wet** (и у него нет пассивки предмета `WetLightning`),
или **Fire, будучи Greased**: элемент в очередь не добавляется; вместо этого считается урон
`Spell(элемент).CalculateDamage(Lightning, Self)`, магнитуды всех записей ×0.5, и он применяется
к самому заклинателю. Буфер ввода очищается.

---

## 2. Очередь элементов (spell queue)

- Ёмкость: `Defines.SPELL_QUECAP = 5` у игрока (у NPC — ёмкость их списка; у предмета-оружия тоже 5).
- Элемент очереди — это `Spell` с магнитудой 1 одного элемента (или результат слияния, см. ниже).

### 2.1 Отношения между элементами

| Отношение | Пары | Результат |
|---|---|---|
| **Opposites** (взаимоуничтожение) | Arcane–Life, Fire–Cold, Earth–Lightning, Water–Lightning, **Shield–Shield** | обе записи удаляются |
| **Revertable** (обратимые) | Cold–Steam, Ice–Fire | слияние → Water |
| **Combinable** (комбинирование) | Water–Cold, Water–Fire | слияние → Ice / Steam |

Замечания:
- Проверка «противоположностей» идёт по флагам записей: Steam не является Water, поэтому
  Steam + Lightning **не** гасятся; Ice + Lightning тоже не гасятся.
- `InclusiveOpposites` = Opposites + пара Fire–Water. Используется при встрече лучей и при уроне
  элементальным барьерам (не в очереди).
- Poison–Life гасятся только на уровне магнитуд при сложении заклинаний (раздел 3), не в очереди.

### 2.2 Алгоритм добавления элемента `X`

**Если в очереди < 5 записей:**
1. `X` добавляется в конец.
2. Проход «Opposites»: от последней записи к первой; для каждой записи `i` ищется ближайшая
   *более ранняя* запись `j < i`, противоположная ей (поиск от `i−1` к 0). Если найдена — удаляются
   обе, обход продолжается с позиции до них.
3. Проход «Revertable»: аналогично от конца; найденная пара сливается (сумма заклинаний,
   раздел 3) **на место более ранней** записи, поздняя удаляется.
4. Проход «Combinable»: то же для пар Water+Cold / Water+Fire.

Каждый проход выполняется ровно один раз, в этом порядке. Следствие: продукт слияния из шага 3
(Water) уже не проверяется на противоположность в этом же добавлении (так достижим, например,
магик Amalgameddon: `S Q R A F` → `[Arcane, Water, Lightning]`).

**Если очередь полна (5):** ищется (от конца к началу) первая запись, противоположная `X` → она
удаляется; иначе первая обратимая → сливается с `X` на её месте; иначе первая комбинируемая →
сливается на её месте; иначе `X` отбрасывается. (Для оружия с флагом *lightning precedence*,
если ничего не нашлось, первая с конца запись, отличная от Lightning, заменяется на Lightning.)

Функция возвращает «элемент добавлен» только если длина очереди выросла на 1.

### 2.3 Примеры

- `Q F` → `[Steam]`; `Q R` → `[Ice]`; `R Q` → `[Ice]` (вода ищет более ранний холод).
- `Q A` → `[]` (Water–Lightning); `E E` → `[]` (второй щит гасит первый).
- `Q F R` → `[Water]` (Steam+Cold обратимы); `Q R F` → `[Water]` (Ice+Fire).
- `F D Q` → `[Steam, Earth]` (вода сливается с *самым поздним* подходящим — здесь Fire в позиции 0).

---

## 3. Итоговое заклинание (`Spell`)

`Spell` хранит маску `Element` и 11 магнитуд (float). При касте `SpellManager.Combine` сворачивает
очередь слева направо оператором сложения. Если первая запись — магик (`Element == All`),
возвращается она.

### 3.1 Оператор сложения A + B (по шагам)

Пусть `ΣX = A.X + B.X` для каждого элемента.

1. `E = max(0, ΣE − ΣZ)`, `C = max(0, ΣC − ΣF)`, `F = max(0, ΣF − ΣC)`, `Z = max(0, ΣZ − ΣE)`,
   `P = max(0, ΣP − ΣL)`.
2. `I = ΣI`; если `I > 0` и `ΣF > 0`: `k = min(I, ΣF)`; `W += k`, `F −= k`, `I −= k`.
3. `T = ΣT`; если `T > 0` и `ΣC > 0`: `k = min(T, ΣC)`; `W += k`, `C −= k`, `T −= k`.
4. `L = ΣL`, `A = ΣA`, `S = ΣS`, `W += ΣW`.
5. `k = min(ΣW, ΣF)`: `T += k`, `W −= k`, `F −= k`.
6. `k = min(ΣW, ΣC)`: `I += k`, `W −= k`, `C −= k`.
7. `Element` = объединение флагов всех элементов с магнитудой > 0.

Поскольку очередь уже нормализована, на практике магнитуда элемента = число его записей в очереди
(например, `Q F Q F D` → Steam 2, Earth 1). `Spell.DefaultSpell(mask)` даёт магнитуду 1 на каждый
флаг. `Normalize()` делит все магнитуды на сумму (используется при зарядке оружия).

Вспомогательные величины:
- `TotalMagnitude` = сумма всех 11 магнитуд.
- `TotalSprayMagnitude` = F + C + T + W + P.
- `BlastSize` = `√(m / 5)`, где `m` зависит от типа: Spray → max(T, F, C, W, P); Projectile →
  max(E, I); Beam → max(A, L); иначе 0. Радиус «взрыва» обычно `BlastSize × 10`
  (5 элементов → радиус 10).
- `ArcaneMultiplier(m) = 3·√(m/5)` (вспомогательная, используется вне ядра).

### 3.2 Определение типа заклинания (`GetSpellType`) — по приоритету

1. `Element == All` → **Magick**
2. `Element == None` → **Push** (пустая очередь: «толчок»)
3. содержит Shield → **Shield**
4. содержит Earth или Ice → **Projectile**
5. содержит Arcane или Life → **Beam** (railgun)
6. содержит Lightning и **не** содержит Steam → **Lightning**
7. иначе → **Spray** (Water, Cold, Fire, Steam, Poison, Steam+Lightning)

---

## 4. Типы каста и жизненный цикл

`CastType`: `None/Blast = 0`, `Force = 1` (ПКМ), `Area = 2` (Shift+ПКМ), `Self = 3` (СКМ),
`Weapon = 4` (Shift+ЛКМ, «зачарование» оружия), `Magick = 5` (Пробел).

Порядок: при нажатии каста очередь сворачивается в `Spell` (очередь игрока очищается), персонаж
переходит в `CastState` (или `ChargeState`), по событию анимации вызывается `Spell.Cast(castType)`,
который берёт из пула эффект по `SpellType` и вызывает `CastForce/CastArea/CastSelf/CastWeapon`.

| SpellType | Класс эффекта |
|---|---|
| Push | `PushSpell` |
| Spray | `SpraySpell` |
| Projectile | `ProjectileSpell` |
| Shield | `ShieldSpell` |
| Beam | `RailGunSpell` (+ сущность `Railgun`) |
| Lightning | `LightningSpell` (+ `LightningBolt`) |
| Magick | `SpecialAbility` по `MagickType` (раздел 10) |

Базовые правила эффекта (`SpellEffect`):
- minTTL/maxTTL: Force/Area/Weapon = 0.5 / 1.5 с, Self = 0.2 / 0.2 с.
- Непрерывный эффект (всё, кроме Projectile) прерывается, если игрок отпустил кнопку после
  истечения minTTL, если заклинатель умер, или если его `CastType` стал `None`.
- Скорость поворота во время каста (множитель): база 0.75; Spray 0.6; Lightning 0.1; Shield 0.1;
  Beam 0.0325; Push 0; Projectile 0.
- Пулы эффектов по 16 экземпляров каждого типа.

**Заряд (SpellPower).** Если тип Projectile и каст Force, либо тип Push и каст Force/Area, персонаж
входит в `ChargeState`: проигрывается `charge_force`/`charge_area`, `SpellPower` линейно растёт
от 0 до 1 по длительности клипа, после окончания клипа = 1 (loop-анимация). Отпускание кнопки →
каст. Поворот при зарядке ×0.125. Для не-аватаров заряд снарядов фиксирован 0.5.

**Weapon (зачарование).** У аватара каст Weapon не создаёт эффект сразу: все записи очереди
переносятся в очередь удерживаемого оружия (`Item.TryAddToQueue`, с `Normalize` и теми же
правилами слияния, ёмкость 5), очередь игрока очищается. Когда оружие «заряжено» и очередь игрока
пуста, атака вызывает `Cast(Weapon)` сохранённого заклинания.

---

## 5. Поведение по типам

### 5.1 Push (пустая очередь)

| | Force | Area |
|---|---|---|
| Сила | `50 + 500·SpellPower` | то же |
| Конус / радиус | угол π/6, дальность 10 | 360°, радиус `3.5 + AdditionalPushRadius` |
| Время действия | 0.25 с; радиус охвата растёт от 25 % до 100 % | то же |

Удар: `Damage(Pushed, Earth, amount = сила, magnitude = 1.5)` — один раз на цель (hit list),
только при прямой видимости (луч до цели не пересекает геометрию уровня). Сущности без
`IDamageable` получают импульс напрямую (подъём `10° + SpellPower·0.1·π/8`). Self/Weapon — пусто.

### 5.2 Spray

- **Force**: луч-конус, угол π/10 (18°), длительность до **4 с** (пока зажата кнопка).
  Базовая дальность `R = 5 + TotalSprayMagnitude` (при 5 элементах — 10). Дальность нарастает
  за 0.25 с (множитель +4/с до 1). Фактическая дальность поражения на кадре = `R · (0.1·R·f)`,
  где `f ≤ 1` — доля луча до первого препятствия (геометрия уровня или щит). Т.е. без
  препятствий ≈ `R²/10`.
  Каждая цель в дуге получает коллекцию урона **с магнитудами ×0.25**, не чаще раза в 0.25 с
  (hit list с таймером 0.25 с) ⇒ ≈ 1 «полная» коллекция в секунду.
  Холод/вода в струе замораживают поверхности жидкостей (`Liquid.Freeze`). На полу остаются
  декали (Fire/Cold/Water), при F ≥ 1 — эффект `fire_scourge`.
- **Area**: мгновенный `FullBlast` радиуса `BlastSize·10` вокруг заклинателя (сам заклинатель
  исключён), с затуханием (5.8).
- **Self**: коллекция `(Spray, Self)` применяется к себе один раз (типично: вода тушит огонь, огонь
  сушит).
- **Weapon**: дуговой удар радиуса 6, угол π/2 из точки оружия.
- Если заклинатель **Greased** и в коллекции есть огонь — первая огненная запись применяется к
  самому заклинателю (Force/Area/Weapon).
- Вода без пара формально вызывает «электризацию» (`Electrocute`), но у `Character` эта функция
  пустая (реально работает только у электрических барьеров).

### 5.3 Projectile (Earth/Ice)

**Force** (заряжаемый):
- Скорость `v0 = 25 + 85·SpellPower`; с Earth: `v0 ×= 0.5 + 0.5/E`. Масса снаряда `1 + 50·E`,
  радиус `0.075 + 0.075·E`, время жизни 10 с.
- С Earth — **один** валун. Без Earth (только Ice) — `N = trunc(3·I)` осколков.
- Интервал между выстрелами: `(0.0875 + Total/5·0.2625) / N`.
- Все снаряды, кроме последнего, получают случайный поворот по Y в пределах `±(π/4)·(1−заряд)`
  и скорость `v0·(1.25 + 0.75·rand)`; последний летит точно по направлению со скоростью `v0`.
- Если отрезок «центр персонажа → точка вылета» пересекает щит, снаряд не создаётся.
- Модели: `Models/Missiles/Earth0..3` (индекс `clamp(E−1, 0, 4)`, 2 и 3 совпадают),
  `Ice3`, `IceEarth0..3` (индекс `clamp(I+E−1,0,4)`).
- Попадание: урон `(Projectile, Force)` (раздел 6.3); у каменных снарядов первая запись
  масштабируется скоростью: `amount × |v| / 50`. Ледяные осколки — Piercing, магнитуды ×`1/N`.
- Валун с прочими элементами (кроме Ice): при попадании/столкновении — взрыв радиуса
  `BlastSize(заклинание без Earth)·10` с уроном `CalculateDamage(тип_остатка, Area)` от полного
  заклинания. Без «остатка» валун пробивает до 25 целей.
- Столкновение со средой: декаль `Crater`.

**Area**: `GroundBlast` радиуса `BlastSize·10` (Earth/Ice), без затухания, игнорирует
левитирующих; с Ice появляются ледяные шипы (`IceSpikes`). Урон `(Projectile, Area)`.

**Self**: снаряды падают на самого заклинателя: осколки — из случайных точек на окружности
радиуса 6 на высоте +10, последний (или единственный валун) — с высоты +8 строго вниз.

**Weapon**: с Ice — ледяной клинок (`IceBlade`) длиной `3.75 + 1.25·I`; с Earth — по окончании
анимации удара запускается подземная волна (`UnderGroundAttack`) скоростью 40, дальностью 15,
каждый получает урон один раз.

### 5.4 Beam (Arcane/Life — «railgun»)

- **Force**: луч длительностью `TTL = 1 + 2·(A + L)` с (с учётом баффов `ModifySpellTTL`).
  Длина растёт со скоростью 90 ед/с до максимума **75**. Урон цели (первой пересечённой) —
  каждые 0.25 с коллекция с магнитудами ×0.25. Луч отражается от материала `Reflect`, щитов и
  персонажей под щитом-пузырём, проходит через порталы, игнорирует нетвёрдые барьеры и поля
  смазки. Максимальная глубина цепочки отражений/слияний — 6.
- **Пересечение лучей** (расстояние между отрезками ≤ 1):
  - «Взрыв» через 0.25 с, если: у лучей есть общий владелец, ИЛИ их дополнительные элементы
    (`W, C, F, Z, T, P`) — `InclusiveOpposites`, ИЛИ один Arcane, а другой Life, ИЛИ направления
    почти противоположны (|среднее направлений| < 0.2). Взрыв: `FullBlast` каждой коллекции
    урона владельцев радиусом `3 + число_владельцев`, плюс Knockback 500 / магнитуда 2 радиусом 5.
    Ачивка `nevercrossthebeams`.
  - Иначе лучи **сливаются**: из точки встречи выходит дочерний луч по среднему направлению,
    несущий урон всех родителей.
- Arcane/Life урон лучом накапливается на персонаже (`AccumulateArcaneDamage`); при смерти от
  луча персонаж «раздувается» и взрывается (`BloatKill`, `BLOATTIME = 0.333`).
- **Area**: `FullBlast` радиуса `BlastSize·10` (визуально `ArcaneBlast`, без затухания по
  дистанции для Arcane/Life — см. 5.8).
- **Self**: коллекция `(Beam, Self)` применяется к себе (Life — лечение).
- **Weapon**: `ArcaneBlade`; мгновенный дуговой удар радиуса `1.5 + Total/2`, угол 81° (9π/20).

### 5.5 Lightning

| | Force / Weapon | Area | Self |
|---|---|---|---|
| Число разрядов | 8 за 1 с | 32 за 1 с | 1 (по себе) |
| Дальность | `4 + 0.8·Z` | `2 + 0.4·Z` | — |
| Направление | по взгляду | по кругу, шаг π/4 | — |
| Магнитуда на разряд | ×1/8 | ×1/16 | ×1 |

(Дальность модифицируется баффами `ModifySpellRange`.) Если заклинатель **Wet** (без пассивки
`WetLightning`) — дальность 2 и режим «по кругу».

Разряд (`LightningBolt`): первая цель — среди `IDamageable` в радиусе, в секторе ±90° от
направления, не за щитом; выбирается кандидат с меньшим углом и меньшей дистанцией. Затем цепь:
от поражённой цели ищется ближайшая новая цель в радиусе `оставшаяся_дальность − пройденное`,
в секторе ±45° от направления разряда. Цепь не продолжается от барьеров, щитов и зон боссов.
Блокирующий персонаж поглощает молнию в своё оружие (добавляет Lightning в очередь предмета).
Если цели нет — разряд рисуется в случайном отклонении ±π/16 и гаснет.

### 5.6 Shield (содержит Shield)

Обозначим `X = Element без Shield`.

**X пусто (чистый щит):**

| Каст | Результат |
|---|---|
| Force | Щит-«диск» радиус 5.5, HP 800; центр смещён на 3.5 назад от заклинателя (дуга ≈2 м впереди) |
| Area | Сфера радиус 5.5, HP 1000 вокруг заклинателя |
| Weapon | Стена радиус 5.5, HP 800 впереди на `радиус_персонажа + 7.5` |
| Self | Пузырь (`SelfShield`) — см. ниже |

Сущность `Shield`: теряет 10 HP каждые 0.1 с (**100 HP/с**), макс. HP 5000; урон получает только
от атак с флагами Damage/Status (`amount × magnitude`), прочие отражаются. Блокирует спреи, лучи
(отражение), молнии, снаряды.

**X без Earth/Ice, но с Arcane или Life → мины (`SpellMine`):**
- Force: дуга окружности радиуса 4 (центр на 1 м позади), две первые мины по сторонам, далее
  цепочкой каждые 0.1 с до длины дуги `2π·4·0.125 ≈ 3.14` в каждую сторону.
- Area: полная окружность радиуса 4 вокруг заклинателя.
- Weapon: линия вперёд: `2 + trunc(max(Total/2, 1))` мин с шагом 2.5.
- Мина: радиус 1; активируется по окончании анимации появления; срабатывает через **0.5 с**
  после касания персонажем/зоной босса/снарядом (не эфирным и не левитирующим; владелец должен
  сначала сойти с мины); `FullBlast` радиус **3** с уроном `(Shield, Force|Area|Weapon)`.
  Взрывается, если владелец дальше 30 ед. Новые барьеры, появляющиеся поверх мины, взрывают её.

**Иначе → барьеры/стены (`Barrier`):**
- Force: дуга радиуса 4, шаг сегментов `2·GetRadius` (1.25 для твёрдых, 0.75 для
  элементальных, с подгонкой масштаба), цепочка каждые 0.075 с на `≈π` в каждую сторону. Если в
  заклинании нет статусных элементов, Steam и Lightning (т.е. чисто каменная/ледяная стена,
  возможно с Arcane/Life), центр дуги сдвинут на 2 м назад.
- Area: кольцо радиуса `S + 3` вокруг заклинателя.
- Weapon: линия вперёд как у мин.
- **Твёрдый барьер** (есть Earth или Ice):
  - HP `(1 + E)·500`, броня `trunc(50·I)` вычитается из входящего урона (кроме огня по льду).
  - Теряет 10 HP каждые 0.2 с (**50 HP/с**).
  - Иммунитет к «противоречащим» элементам: холодный барьер не получает урон от Fire/Steam,
    паровой — от Cold, водяной — от Fire, огненный — от Water.
  - При появлении бьёт всех в радиусе `1.25·масштаб·радиус` уроном `(Projectile, Area)` один раз.
  - Контактный урон `(Shield, Force|Area)` (раз в 0.25 с на цель) активен, пока «эффект» жив:
    чисто ледяные шипы живут `I` секунд (затем барьер исчезает); у ледяного «гейзера» время
    эффекта = магнитуда доминирующего доп. элемента; у «теслы» (Ice+Lightning) = `Z` с;
    у Ice+Earth ледяная оболочка живёт `I` с.
- **Элементальный барьер** (без Earth/Ice — «шторм»/стена стихии):
  - HP `Total·100` (Shield входит в Total), та же убыль 50 HP/с.
  - Урон получает только от противоположных стихий: `HP −= trunc(доля·amount·5)`, где
    `доля = (число элементов барьера, InclusiveOpposite входящему) / (число элементов барьера без Shield)`.
  - Контакт: урон `(Shield, ...)` раз в 0.25 с на цель; с Lightning — бьёт разрядом.
- Появляющийся барьер уничтожает барьеры других кастов и взрывает мины в радиусе `0.8·масштаб·радиус`.
- Камера трясётся с силой `0.25..0.75` и длительностью `0.5·S`.

**Self:**
- X пусто → пузырь `SelfShield`: 500 HP, −100 HP/с; поглощает все атаки Damage/Status (полностью
  отражает); физические попадания превращаются в отбрасывание.
- X содержит Earth/Ice → **броня**:
  - HP 1500 при Earth, иначе 900 (Ice); не убывает со временем; масса персонажа 1001 (не
    толкается); скорость `0.9 − (0.075·E + 0.025·I)`.
  - Физический урон: `amount −= 10` (Earth) или `−= 70` (иначе), затем `× (1 − (0.05·I + 0.15·E))`;
    остаток сверх HP брони проходит дальше. Огонь (DoT) по ледяной броне ×`(1 + I)`; лечение по
    броне не проходит.
  - Ледяная броня при наложении наносит в радиусе 3 урон `Earth|Piercing` 275, магнитуда
    `max(1, I + E)`.
- X содержит Lightning/Arcane/Life/Steam/Water/Cold/Fire → **элементальная аура-оберег**: для
  каждого такого элемента — бафф-сопротивление с множителем 0 (иммунитет) для **всех** в радиусе
  `1.5·m`, длительность `15 + 5·m` с, где `m = max(1, макс. магнитуда этих элементов)`.
  Снимает прежний пузырь (если не накладывалась броня) и прежнюю ауру.

### 5.7 Magick — см. раздел 10.

### 5.8 Взрывы (`Blast`)

- `FullBlast(radius)`: все `IDamageable` в радиусе (кроме исключённого), с прямой видимостью от
  центра; расстояние = до центра минус радиус цели. Затухание магнитуды
  `k = 1 − (d / radius)³` — **кроме** коллекций, содержащих Arcane/Life. Тряска камеры
  `radius/3`, 0.3 с.
- `GroundBlast(radius)`: без затухания, не задевает левитирующих, тряска 2 / 0.9 с.
- Обе вызывают `Liquid.Freeze` по кругу (холод замораживает воду).

---

## 6. Расчёт урона заклинания (`Spell.CalculateDamage`)

Результат — `DamageCollection5`: до 5 записей `Damage {AttackProperty, Element, Amount, Magnitude}`.
Пустая запись тоже занимает слот. `ScaleAmount(base, m) = base·√m`.

### 6.1 Базовые константы (`Defines`, static — «таблица баланса»)

| Имя | Значение | Имя | Значение |
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

(Отрицательный amount = лечение. `SPELL_DAMAGE_ICE`, `SELF_EARTH/ICE/POISON`, `BARRIER_STEAM/FIRE/POISON`
в ядре заклинаний не используются.)

Общие «строительные блоки»:

- **StatusMix** (если есть W/C/F/T/P): `Status`, элементы `Element ∩ StatusEffects`,
  amount `50·min1(P) + 60·min1(F)`, magnitude `min1(W + F + C + P + T)`.
  Внимание: все статусы этой записи получают один и тот же amount (как DPS).
- **LifeStatus** (если есть L): `Status`, элемент Life, amount `−80·√L`, magnitude 1
  (при Self — `L`).
- **StatusB** («CalculateStatus»): `Status`, элементы `Element ∩ (StatusEffects ∪ Arcane)`, плюс
  Water если есть Steam; amount `60·√F`; magnitude `min1(W + F + C + 0.5·T)`.
- **Misc(cast)**: если есть W или A — флаг `Pushed`; элементы Water (+ Earth/Arcane для
  Area/Weapon); amount `trunc(70·W)` (+ `trunc(120·A)` для Area/Weapon); magnitude
  `W` (+ `E + A` для Area/Weapon); при Earth (Area/Weapon) — ещё флаг `Knockdown`.

### 6.2 Spray

| Запись | Force / Area | Weapon | Self |
|---|---|---|---|
| StatusMix | да | да | да |
| LifeStatus | (недостижимо для Spray) | | |
| Direct (если C/F/Z/T) | `Damage`, элементы `∩{C,F,Z,T}`, amount `60√F + 25√C + 280√T`, mag `min1(F+C+T+Z)` | то же | amount `60√F + 25√C + 80√T` |
| Water push (если W) | `Pushed`, Water, amount `trunc(70·W)`, mag `1 + W/4` | mag `W` | — |
| Water marker (если W) | `Damage`, Water, amount 0, mag 1 | то же | — |

Lightning в спрее (Steam+Lightning) не добавляет amount, но добавляет флаг Lightning (⇒ двойной
урон по мокрым, см. 7).

### 6.3 Projectile

- **Force, есть Earth**: `Damage`, элементы `Element ∩ {E, I}` (+Cold при Ice), amount
  `150·E² + 275·I²` (квадратично!), mag 1; × скорость/50 (5.3).
- **Force, только Ice**: `Damage`, элементы `Element ∪ {Cold, Earth}`, amount
  `275·√I + 250·√Z + 225·√A − 180·√L − 180·√L` (Life учитывается дважды), mag 1; Piercing;
  на осколок mag ×1/N.
- **Force, Ice + статусы**: дополнительно `Status`, элементы `∩StatusEffects`, amount
  `50·min1(P) − 80·min1(L)`, mag `min1(W + C + P + L)`.
- **Area**: 
  - Status (если W/C/F/T): элементы `∩StatusEffects`, amount `60·min1(F)`, mag `min1(W+F+C+T)`;
  - LifeStatus;
  - `Damage`, элементы `(Element ∩ Instant) ∪ Earth` (огонь/холод в маску не попадают!),
    amount `60√F + 25√C + 280√T + 250√Z + 275√I − 180√L + 225√A`, mag `min1(F+C+T+Z+I+A)`;
  - Water: `Pushed`, amount `trunc(70W)`, mag `W`;
  - Earth: `Knockdown`, amount `trunc(150E)`, mag `E`.
- **Weapon**: если Ice — `Damage` (+Piercing), Earth, amount `2·275·√I + 150·√E`, mag 1;
  затем Misc(Weapon), StatusB, пустая запись.
- **Self**: урон считается как у снарядов Force (они падают на заклинателя).

### 6.4 Shield (урон мин, элементальных стен, Wave и т.п.)

1. Life (без учёта Shield) → `Damage`, элементы `∩Beams`, amount −600, mag 1;
   иначе Arcane → `Damage`, Arcane, 225, mag 1.
2. Статусы → StatusB-подобная запись: элементы `∩(StatusEffects ∪ Arcane)` (+Water при Steam),
   amount `60·√F`, mag `min1(W + F + C + 0.5·T)`.
3. W и (E или I) → `Pushed`, Water, amount `trunc(75·√W)`, mag `√W`.
4. Z → `Damage`, Lightning, amount `130·√Z`, mag 1.
5. A и нет E/I → `Knockback`, None, amount 500, mag 2.

### 6.5 Beam

1. StatusMix (если W/C/F/T/P), с доп. флагом `Damage` при наличии W, +Water при Steam.
2. LifeStatus.
3. `Damage`, элементы `Element ∩ {Z, A, L, T, C, F}`, mag 1, amount:
   - не Self: `60√F + 25√C + 125√Z + 280√T − 180·min1(L) + 225·min1(A)`;
   - Self: `60√F + 25√C + 80√Z + 80√T − 180·min1(L) + 200·min1(A)`.
   Сила Arcane/Life **не растёт** с количеством — количество увеличивает длительность луча.
4. Water → `Pushed`, amount `trunc(70W)`, mag `W`.

### 6.6 Lightning

1. `Damage`, все элементы заклинания, amount `250√Z + 225√A − 180√L`, mag 1.
2. StatusMix (если W/C/F/T/P), +флаг Damage при W.
3. Пустая запись.
4. Misc(Area).
5. StatusB, если есть статусы или Steam.

### 6.7 Итоговый DPS (ориентир)

Для непрерывных эффектов (Spray Force, Beam) магнитуды делятся на 4 и применяются раз в 0.25 с,
поэтому «amount за секунду» ≈ amount записи × magnitude записи. Пример: 5×Steam спрей ≈
`280·√5 ≈ 626` до сопротивлений; Arcane-луч = 225/с весь срок `1 + 2A` с.

---

## 7. Применение урона к цели

### 7.1 Модификаторы атакующего

Перед отправкой каждой записи: `amount = amount·M + B`, где по всем баффам атакующего типа
`BoostDamage`, пересекающимся по элементам: `M = Π magnitude_баффа`, `B = Σ amount_баффа`.
(Сетевой режим: клиент шлёт `DamageRequestMessage`, флаги `DamageFeatures` D/N/K/E решают,
применяются ли урон/уведомление/отбрасывание/эффекты.)

### 7.2 `Character.InternalDamage` — порядок для одной записи

1. Мёртв / бессмертен / нет таблицы сопротивлений → ничего.
2. `Entanglement`: если цель не горит — опутывание силой magnitude, конец.
   Опутанного освобождают урон Damage и огненный Status.
3. Активный `SelfShield` обрабатывает запись (5.6) и может её изменить/обнулить.
4. Отбрасывание:
   - `Knockback` (= Pushed|Knockdown): импульс с подъёмом 0.698 рад (40°), цель сбивается с ног
     (если может);
   - иначе `Pushed`: импульс с подъёмом 0.1745 рад (10°); `Knockdown`: сбивание, если
     `magnitude ≥ KnockdownTolerance` (5000 = иммунитет).
   - Импульс: «мощность» = amount, «дистанция» = magnitude;
     `d' = d · max(0, 2·√(1 − (масса − мощность/4)/мощность))`; затем баллистическая скорость
     для полёта на `d'` под углом подъёма, ×1.4. Иммунитет: бессмертие, эфирность, твёрдая
     броня, масса > 1000, опутанность, смерть.
5. Блок (Earth/Lightning Damage при поднятом блоке предметом): вычитается блокированное; если
   полностью — `Deflected`.
6. **Статусы до сопротивлений** (раздел 9): Fire→Burning, Cold→Cold, Water→Wet, Poison→Poisoned,
   Life→Healing, Steam→Wet. DPS = amount записи, magnitude = magnitude записи.
7. **Сопротивления** (раздел 8) → изменённые amount и magnitude. `|magnitude| ≈ 0` → `Deflected`, конец.
8. `Stun` (amount > 0) → таймер оглушения; `Bleed` → статус Bleeding (DPS = `BleedRate` шаблона).
9. Для флага `Damage`:
   - Lightning по **Wet** цели → `amount ×2`;
   - Life → уменьшает магнитуду Poisoned на magnitude записи (до снятия);
   - Earth/Ice/Arcane по **Frozen** цели → `amount ×3` (иначе — кровь/гор);
   - `HP −= amount × magnitude`; HP ограничено сверху MaxHP.
   - Результаты: Damaged / Healed / Deflected (0) / Hit (`amount > HitTolerance`) / Pierced.
10. Смерть: если HP ≤ 0 → Killed; если цель была Frozen или HP ≤ `−0.5·MaxHP − 500` →
    **OverKill** (разлёт на куски, без трупа; HP = `−MaxHP − 1000`). Ачивка за урон > 9000.

Результат по коллекции — OR результатов записей, при этом `Deflected` остаётся только если все
пять записей «отражены».

DoT-урон статусов идёт отдельным путём (`Damage(float, element)`): сопротивления не применяются
(они уже учтены в DPS при наложении), но огонь по Greased ×2, щит/броня поглощают.

### 7.3 Регенерация

`HP += RegenRate·dt` (из шаблона), только если HP в (0, MaxHP] и цель не Burning/Poisoned.

---

## 8. Сопротивления

Структура `Resistance { ResistanceAgainst: Elements, Multiplier: float, Modifier: float, StatusResistance: bool }`.

Источники для персонажа:
1. Шаблон персонажа (`CharacterTemplate`, `Content/Data/Characters/*.xnb`): 11 записей по
   умолчанию `×1, +0, false`; файл содержит список переопределений
   `(element:int32, multiplier:float, modifier:float, statusResistance:bool)`.
2. Для аватара — дополнительно посох (`Equipment[1]`, `Item.Resistance`, `Content/Data/items`).
3. Баффы `BuffType.Resistance` (ауры, магики, оберег 5.6).

`Damage.ApplyResistances`: для **каждого** флага элемента записи и каждого источника:
- `mag_mult *= |Multiplier|`; если Multiplier < 0 — флаг «инверсия» (отдельно отмечается Life);
- `amount += Modifier` с отсечкой к нулю (положительный amount не уходит ниже 0, отрицательный —
  выше 0).
- После цикла: если был отрицательный множитель у Life → `amount = |amount|` (лечение вредит,
  например нежити); иначе если был любой отрицательный множитель → `amount = −amount`
  (поглощение стихии лечит). `magnitude *= mag_mult`.
- `ArmourPiercing`: игнорируются множители ≤ 1 и модификаторы ≤ 0.

Итог: `урон ≈ (amount + ΣModifier) · magnitude · Π|Multiplier| · спецмножители`.

Статус-сопротивление (`AddStatusEffect`, раздел 9): множитель/модификатор соответствующего
элемента (Fire для Burning, Cold, Water для Wet и Greased, Steam, Poison, Life) применяется к
DPS/магнитуде статуса; `StatusResistance = true` полностью запрещает статус. Для превращения
Wet→Frozen проверяется `StatusResistance` элемента **Ice**.

**Масштаб по числу игроков** (при загрузке шаблона не-дружественного персонажа):
`k = 1 − (игроков − 1)·0.00666` (`DIFFICULTY_RESISTANCE_MODIFIER`); положительные множители и
модификаторы `×k`, отрицательные `÷k`.

`ResistanceAgainst(elements)` (для ИИ и некоторых магиков): `1 − clamp(ΣModifier/300 + ΣMultiplier, −1, 1)`,
где Frozen даёт −350 к модификатору Earth, Greased удваивает множитель Fire. 1 = полный иммунитет.

---

## 9. Статус-эффекты

`StatusEffects` (short, флаги): Burning 1, Wet 2, Frozen 4, Cold 8, Poisoned 0x10, Healing(=Life) 0x20,
Greased 0x40, Steamed 0x80, Bleeding 0x100; `NR_OF_STATUSEFFECTS = 9`. У персонажа массив из 9
слотов (индекс = log2), маска текущих статусов пересчитывается после каждого изменения.

Каждый статус: `magnitude` (жив, пока > 0), `DPS`, накопитель дробного урона (урон наносится
целыми единицами).

| Статус | Кап магн. | Убыль магн. | Эффект |
|---|---:|---|---|
| Burning | 3 | 0.2/с (1 ед. ≈ 5 с) | DPS растёт на 25 %/с от себя, до `500·magnitude`; урон Fire; панику даёт |
| Cold | 3 | 0.1/с | множитель скорости `0.333 + 0.667·0.001^magnitude` |
| Wet | 1 | **нет** (до снятия) | Lightning ×2; призыв Lightning бьёт себя |
| Frozen | 3 | 0.25/с (1 ед. = 4 с) | обездвижен, каст прерывается; Earth/Ice/Arcane ×3; убийство → OverKill |
| Poisoned | 1 | нет (кроме спец. «истекающих») | урон `DPS·magnitude`/с (Poison); скорость ×0.5; нет регена |
| Healing | 3 | 1/с; при полном HP — сразу | лечение `DPS`/с (DPS отрицательный); при создании `magnitude = 0.5 + 0.5·m` |
| Greased | — | нет | Fire ×2; поджог: DPS ×4 и смазка снимается |
| Steamed | 1 | 1/с | панику даёт (источник в ванильном коде не найден) |
| Bleeding | — | нет (кроме «истекающих») | урон `BleedRate`/с; снимается лечением |

Сложение одинаковых статусов: магнитуды суммируются (Healing — максимум), DPS: Burning — максимум,
прочие — среднее, взвешенное по магнитудам. Огненный DoT наносится элементом Fire, яд — Poison,
лечение — Life, прочие — Earth.

Взаимодействия при наложении (`AddStatusEffect`):

| Накладывается | Условие | Результат |
|---|---|---|
| Burning | есть Cold | Cold снят, Burning **не** накладывается |
| Burning | есть Wet | Wet снят (сушка 0.9 с, эффект `drying_steam`), Burning не накладывается |
| Burning | есть Frozen | магн. Frozen −= магн. огня; Burning не накладывается |
| Burning | Greased | DPS ×4, Greased снят |
| Burning | — | `DPS = (DPS + modFire)·multFire` |
| Cold | Burning или идёт сушка | Burning снят, Cold не накладывается |
| Cold | Wet (и нет StatusResistance у Ice) | **Frozen** с магн. 1, Wet и Cold сняты, каст прерван |
| Cold | уже Frozen | магн. Frozen = 1 (обновление) |
| Wet / Steam | Burning или идёт сушка | Burning снят, Wet не накладывается |
| Wet | Greased | Greased снят |
| Greased | Wet | Wet снят |
| Steamed | Cold | Cold снят |
| Healing | Poisoned / Bleeding | оба сняты |

Состояние «сушки» (`DryTimer`, 0.9 с ≈ `STATUS_DRYING_TIME`) блокирует новый Wet/Cold сразу после
испарения.

---

## 10. Магики

### 10.1 Распознавание

`MagickTree`: префиксное дерево, узел имеет 11 потомков (по индексу элемента), лист хранит
`MagickType`. Последовательности (ниже) задаются **элементами очереди после слияний** (Steam/Ice —
отдельные элементы).

- `IsMagick` / `CombineMagick`: проход по очереди от первого элемента; каждый шаг обязан
  существовать, конечный узел должен содержать магик, и магик должен быть разрешён. Тогда
  `CombineMagick` заменяет очередь одной записью `{Element = All, MagickType}`.
- `GetMagickType` (подсказка HUD): то же, но допускает «обрыв».
- `Equatable(combo, queue)`: префиксное сравнение по флагам (для подсветки в книге магиков).
- Каст: Boost (Пробел) → `CombineMagick` или уже лежащая запись-магик → `CastType = Magick` →
  `SpecialAbility.Execute`. **Мгновенные** магики (Revive, Haste, Invisibility, Teleport, Napalm,
  ChainLightning) можно кастовать и будучи Frozen / в полёте / сбитым.

### 10.2 Разрешение (unlock)

Разблокировка — бит `1 << MagickType` в `Player.UnlockedMagicks` (ulong; сохраняется в сейве).
- Campaign / Mythos / Challenge / StoryChallenge: магик разрешён, если он открыт хотя бы у одного
  играющего игрока. Прочие режимы (Versus и т.д.) — по каждому игроку. NPC (player = null) могут
  использовать любой.
- Источники: действие триггера `<unlockMagick magicktype="…" delay="…"/>` и книга
  `<spawnMagick magick="…" area="…" immovable="…"/>` (`BookOfMagick`, подбор → unlock) в XML
  сцен (`Content/Levels/**.xml`). В Versus — `VersusRuleset`/`Krietor`.
- `VANILLA_CAMPAIGN_MAGICKS = 0x7FEFFE` (типы 1–22, кроме MeteorS) — прогресс ачивки
  `iputonmyrobeandwizar`. `MYTHOS_CAMPAIGN_MAGICKS` = Revive, Haste, Portal, TractorPull,
  ProppMagick, Levitate, ChainLightning.

### 10.3 Список магиков игрока

Клавиши: Q Water, W Life, E Shield, R Cold, A Lightning, S Arcane, D Earth, F Fire. Колонка
«клавиши» — одна из кратчайших раскладок, вычисленная собственным симулятором правил раздела 2
(не единственная).

| # | MagickType | Последовательность элементов | Клавиши | Instant | Где открывается (кампании) |
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
| 15 | TimeWarp | Cold, Shield | R E | | (в кампании не найден; Challenges: chs_osotc_s3, chs_vietnam_s3; BattleField) |
| 16 | Vortex | Ice, Arcane, Ice, Shield, Ice | Q R S Q E Q R R | | EndofWorld/ew2_s2 |
| 17 | SUndead (Raise Dead) | Ice, Earth, Arcane, Cold | Q R D S R | | Swamp/sw_s3 |
| 18 | SElemental | Arcane, Shield, Earth, Steam, Arcane | S E D Q S F | | Ruins/ru_s3s |
| 19 | SDeath | Arcane, Cold, Ice, Cold, Arcane | S R R Q R S | | Niflheim/nh_s3BF |
| 20 | SPhoenix | Life, Lightning, Fire | W A F | | Mines/mi_s1 |
| 21 | Nullify | Arcane, Shield | S E | | MountainDale/md_s4 |
| 22 | Corporealize | Arcane, Steam, Lightning, Shield, Arcane | S Q F A E S | | Ruins/ru_s6BF |
| 23 | CTD (Crash to Desktop) | Lightning, Lightning, Fire, Life | A A F W | | WizardCastle/wc_s2 |
| 24 | Napalm | Steam, Earth, Life, Fire, Fire | Q D W F F F | ✓ | только Challenges (chs_fields, chs_vietnam_s1..s3 — DLC Vietnam) |
| 25 | Portal | Steam, Lightning, Shield | Q F A E | | Tsar_Rlyeh/tr_s2 (Mythos) |
| 26 | TractorPull | Earth, Arcane | D S | | Tsar_WizardCastle/twc_s1 (Mythos) |
| 27 | ProppMagick | Fire, Steam, Arcane | F F Q S | | Tsar_Mountaindale/tmd_s2 (Mythos) |
| 28 | Levitate | Steam, Arcane, Steam | Q S Q F F | | Tsar_Mountaindale/tmd_s5BF (Mythos) |
| 29 | ChainLightning | Lightning ×3 | A A A | ✓ | Tsar_Mountaindale/tmd_s4 (Mythos) |
| 30 | Confuse | Arcane, Shield, Lightning | S E A | | WizardCastle/wc_s2 |
| 31 | Wave | Earth, Steam, Earth, Steam, Earth | D Q D Q D F F | | Challenges (chs_woot_bout_of_madness) |
| 32 | PerformanceEnchantment | Life, Fire, Lightning, Fire, Life | W F A F W | | Challenges (chs_woot_bout_of_madness) |
| 33 | JudgementSpray | Ice, Ice, Arcane, Shield | Q Q R R S E | | Challenges (chs_dungeons_ch2_s2, chs_woot_bout_of_madness) |
| 34 | Amalgameddon | Arcane, Water, Lightning | S Q R A F | | (требует трюка Ice+Fire→Water) |

Значение `NrOfMagicks = 35`. Типы ≥ 36 (Grow, Shrink, Etherealize, Polymorph, Earthquake, Votal,
KnockDownChance, SummonCross, VladZap, SpawnSlime, SpawnSlimeOverkill, GreaseSplash, EtherealClone,
InflictBleedDamage, InflictPoisonDamage) — служебные способности NPC/предметов, комбинации нет.

Классы эффектов (`Magicka.GameLogic.Entities.Abilities.SpecialAbilities`): Haste, Revive, Rain,
Thunderbolt, Teleport, MeteorShower, Charm, SummonZombie, Conflagration, SummonPhoenix, Fear,
Invisibility, Thunderstorm, TimeWarp, Blizzard, Tornado, Grease, Vortex, SummonElemental,
SummonDeath, Corporealize, Nullify, CTD, Napalm, Portal, TractorPull, ProppMagick, Levitate,
Zap (ChainLightning), Confuse, Wave, PerformanceEnchantment, JudgementSpray, Amalgameddon.
Их поведение — отдельный документ (вне этой спецификации).

Локализация: `#magick_<имя>` (название) и `#magick_<имя>d` (описание), имя в нижнем регистре
(`thunderb`, `sundead`, `performance` …).

---

## 11. Что лежит в данных, а что в коде

| Что | Где |
|---|---|
| Правила очереди, слияний, типов, формулы урона, статусов, длительности | **код** (захардкожено) |
| Комбинации магиков | **код** (`SpellManager`, таблица 10.3) |
| Сопротивления, HP, реген, BleedRate, KnockdownTolerance, HitTolerance персонажей | `Content/Data/Characters/*.xnb` (`CharacterTemplateReader`) |
| Сопротивления и пассивки посохов/оружия (`WetLightning`, `MeleeBoost`…) | `Content/Data/items/*.xnb` (`ItemReader`) |
| Разблокировка магиков | XML сцен: `unlockMagick`, `spawnMagick` |
| Визуальные эффекты заклинаний | `Content/Effects/Spells/**` (`Spray/Spray_Fire1.xml`, `Self/Self_Life.xml`…), `Effects/Barriers`, `Effects/StatusEffects`; ключ — хеш имени файла (`spray_<el>1`, `self_<el>`, `barrier_<el>1_b`, `projectile_<el>4`, `projectiletrail_<el>`, `projectilehit[_big]_<el>`, `statuseffect_*`, `drying_steam`, `fire_scourge`, `push_force`, `push_area`, `elementeffect_<el>`) |
| Звуки | XACT `Content/Audio/Spells.xsb/.xwb`: `spell_<el>_<spray|projectile|hit|blast|barrier|area|self|force|chasm>`, `spell_push_force/area`; UI: `ui_element_<el>` |
| Модели снарядов, стен | `Content/Models/Missiles/*` (Earth0–3, Ice3, IceEarth0–3…), модели барьеров |
| Текстура лучей | `Content/EffectTextures/Beams` |

---

## 12. Прочие константы `Defines`

Часть констант объявлена как `const` и в IL встраивается по месту, поэтому их фактическое
использование нужно подтверждать литералами в коде (в разделах выше указаны значения из кода).

| Константа | Значение | Комментарий |
|---|---:|---|
| `SPELL_QUECAP` | 5 | ёмкость очереди |
| `SPELL_RANGE_PROJECTILE_BASE/VAR` | 25 / 85 | скорость снаряда |
| `SPELL_STRENGTH_PUSH_BASE/VAR` | 50 / 500 | сила толчка |
| `SPELL_ANGLE_PUSH/SPRAY/LIGHTNING` | π/6, π/10, π/4 | углы |
| `SPELL_RANGE_PUSH/AREA_PUSH` | 1.5 / 3.5 | |
| `SPELL_RANGE_SPRAY/AREA/LIGHTNING/AREA_LIGHTNING` | 10 / 10 / 8 / 4 | максимумы при 5 элементах |
| `SPELL_RANGE_SHIELD/SHIELD_MIN/BUBBLE` | 5.5 / 3 / 1.3 | |
| `SPELL_RANGE_RAILGUN/_WIDTH` | 15 / 0.75 | (фактический луч до 75) |
| `SPELL_RAILGUN_BASE_TTL/MOD_TTL` | 1 / 2 | `1 + 2(A+L)` |
| `SPELL_TIME_SPRAY/LIGHTNING/PUSH/ICE/GROUND` | 4 / 1 / 0.25 / 0.35 / 15 | |
| `SPELL_AMOUNT_LIGHTNINGS/ICE` | 8 / 3 | разряды / осколков на Ice |
| `SPELL_STRENGTH_SHIELD/BARRIER/BARRIER_ELEMENTAL` | 500 / 500 / 100 | HP-коэффициенты |
| `SPELL_ARMOR_EARTH/ICE` | 90 / 50 | |
| `SELF_SHIELD_HEALTH/DECAY` | 500 / 100 | |
| `SELF_STONE_HEALTH/MULTIPLIER/MODIFIER` | 1500 / 0.15 / 10 | броня Earth |
| `SELF_ICE_HEALTH/MULTIPLIER/MODIFIER` | 900 / 0.05 / 70 | броня Ice |
| `SELF_ELEMENTAL_AURA_BASE_TTL/MODIFIER_TTL/RADIUS_MODIFIER` | 15 / 5 / 1.5 | оберег |
| `BARRIER_DAMAGE_OVER_TIME` | 10 | убыль HP барьера за шаг |
| `BARRIER_BLAST_RADIUS` | 2.6 | |
| `STATUS_DRYING_TIME` | 0.9 | |
| `STATUS_FROZEN_ARMOR/MAGNITUDE` | 50 / 10 | |
| `MAX_STATUS_MAGNITUDE` | 3 | кап статусов |
| `HITLIST_COOLDOWN` | 0.25 | повторный удар по цели |
| `KNOCKBACK_ELEVATION / PUSH_ELEVATION` | 0.698 / 0.1745 рад | |
| `BLOCKANGLE` | 1.0996 рад (63°) | сектор блока |
| `KNOCKDOWN_IMMUNITY_TOLERANCE` | 5000 | |
| `FALLING_DAMAGE_POWER / KNOCKDOWN_THRESHOLD` | −50 / 100 | падение |
| `LIGHTNING_FRIEDTIME` | 0.1 | |
| `MAX_NR_OF_SUMMONS_PER_CHARACTER / UNDEAD / FLAMER` | 16 / 1 / 4 | |
| `DIFFICULTY_RESISTANCE_MODIFIER` | 0.00666 | масштаб по игрокам |
| `PASSIVE_ABILITY_*` | 0.25 / 2 / 4 / 1.5 / 7 | пассивки предметов |

Цвета элементов (`Spell.*COLOR`, HDR RGB): Earth (0.3, 0.2, 0.1), Water (0, 0.7, 1.3), Cold
(1, 1, 1.4), Fire (1.8, 0.6, 0.4), Lightning (0.75, 0.5, 1), Arcane (2, 0.4, 0.6), Life
(0.2, 1.6, 0.2), Shield (2, 1.5, 1), Steam (1, 1, 1), Ice (0.8, 0.9, 1.4), Poison (1, 1.2, 0).
Цвет заклинания — среднее цветов (Fire и Lightning взвешены магнитудой).

---

## 13. Открытые вопросы

1. **Дальность спрея**: формула `R·(0.1·R·f)` даёт ≈ `R²/10` (1×Fire ≈ 3.6, 5 элементов ≈ 10).
   Это намеренно или артефакт? Нужно сверить с игрой; для 1:1 воспроизводим как есть.
2. **Wet и Greased не истекают** сами (магнитуда не убывает). Проверить в игре, нет ли внешнего
   снятия (вода уровня, время), не найденного в `Character`.
3. **Steamed**: в ванильном коде не найден источник наложения (только обработка). Возможно, мёртвый
   статус или накладывается из данных/DLC.
4. Константы `const` из `Defines` (SPELL_TIME_*, SELF_*_MODIFIER, STATUS_*_TIME …) — какие реально
   используются (встраивание в IL) — нужен поиск литералов в IL, а не по имени.
5. Life учитывается дважды в уроне ледяного снаряда (Force, только Ice) — баг оригинала; решить,
   сохранять ли (по умолчанию — сохранять для совместимости).
6. В `(Projectile, Area)` огонь/холод входят в amount, но не в маску элементов записи (урон
   резистится как Earth/прочие) — подтвердить в игре.
7. `Electrocute` у `Character` пустой: «вода + молния» в спреях/взрывах не бьёт током мокрых целей
   через этот путь. Проверить, нет ли другого механизма (например, через статусы).
8. Точная геометрия цепочек барьеров/мин (шаг, масштаб сегментов, подгонка числа сегментов)
   описана приближённо; нужен отдельный разбор `Barrier.SpawnNextBarrier` / `SpellMine`.
9. Поведение клинков (`IceBlade`, `ArcaneBlade`), `UnderGroundAttack`, `IceSpikes` и блока
   (`BlockDamage`) — частоты ударов и урон не разобраны детально.
10. TimeWarp, Wave, PerformanceEnchantment в кампании: найдены только в Challenges — уточнить
    (возможно, выпадают из персонажей через `DropMagicks` или из DLC-кампаний).
11. Порядок применения статусов **до** сопротивлений приводит к тому, что магнитуда статуса не
    зависит от `Multiplier` атаки (только от статус-сопротивления). Подтвердить на целях с
    иммунитетом к огню.
12. Как именно влияет масштаб по числу игроков на уже закешированные шаблоны (применяется при
    загрузке шаблона, а шаблоны кешируются `ContentManager`) — возможная зависимость от порядка
    загрузки.
13. Сетевая модель (кто считает урон, `DamageFeatures`) — нужна ли совместимость, решается вне
    этого документа.
