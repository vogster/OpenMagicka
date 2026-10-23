# Enumerations of the original

> Clean-room document for OpenMagicka: names and values of the original's enumerations that
> content files and saves refer to. Names are parsed ignoring case (`Enum.Parse(..., true)`),
> which also accepts a number or a comma-separated list of names (their values OR-ed).

## Animations (`ushort`, 236 names)

Used by dialog lines (`anim`), character templates, AI events (`animation`, `idleAnimation`) and
trigger actions. Several names share one value: the generic name (`idlelong`) equals its first
variant (`idlelong0`); the game picks a random variant for the generic one.

| Value | Name |
|---|---|
| 0 | `None` |
| 1 | `idle` |
| 2 | `idlelong` |
| 2 | `idlelong0` |
| 3 | `idlelong1` |
| 4 | `idlelong2` |
| 5 | `idle_agg` |
| 6 | `idlelong_agg` |
| 6 | `idlelong_agg0` |
| 7 | `idlelong_agg1` |
| 8 | `idlelong_agg2` |
| 9 | `idle_wnd` |
| 10 | `idlelong_wnd` |
| 10 | `idlelong_wnd0` |
| 11 | `idlelong_wnd1` |
| 12 | `idlelong_wnd2` |
| 13 | `idle_grip` |
| 14 | `die0` |
| 15 | `die1` |
| 16 | `die_drown` |
| 16 | `die_drown0` |
| 17 | `die_drown1` |
| 18 | `die_drown2` |
| 19 | `hit` |
| 20 | `hit_fly` |
| 21 | `hit_stun_begin` |
| 22 | `hit_stun_end` |
| 23 | `hit_slide` |
| 24 | `attack_hit` |
| 25 | `attack_melee0` |
| 26 | `attack_melee1` |
| 27 | `attack_melee2` |
| 28 | `attack_melee3` |
| 29 | `attack_melee4` |
| 30 | `attack_ranged0` |
| 31 | `attack_ranged1` |
| 32 | `attack_ranged2` |
| 33 | `attack_ranged3` |
| 34 | `attack_recoil` |
| 35 | `block` |
| 36 | `boost` |
| 37 | `charge_area` |
| 38 | `charge_area_loop` |
| 39 | `charge_force` |
| 40 | `charge_force_loop` |
| 41 | `cast_area_blast` |
| 42 | `cast_area_fireworks` |
| 43 | `cast_area_ground` |
| 44 | `cast_area_lightning` |
| 45 | `cast_area_push` |
| 46 | `cast_charge` |
| 47 | `cast_force_lightning` |
| 48 | `cast_force_projectile` |
| 49 | `cast_force_push` |
| 50 | `cast_force_railgun` |
| 51 | `cast_force_shield` |
| 52 | `cast_force_spray` |
| 53 | `cast_force_zap` |
| 54 | `cast_magick_global` |
| 55 | `cast_magick_direct` |
| 56 | `cast_magick_self` |
| 57 | `cast_magick_sweep` |
| 58 | `cast_self` |
| 59 | `cast_sword` |
| 60 | `cast_sword_lightning` |
| 61 | `cast_sword_projectile` |
| 62 | `cast_sword_railgun` |
| 63 | `cast_sword_shield` |
| 64 | `cast_sword_spray` |
| 65 | `cast_teleport` |
| 66 | `cast_spell0` |
| 67 | `cast_spell1` |
| 68 | `cast_spell2` |
| 69 | `cast_spell3` |
| 70 | `cast_spell4` |
| 71 | `cast_spell5` |
| 72 | `cast_spell6` |
| 73 | `cast_spell7` |
| 74 | `cast_spell8` |
| 75 | `cast_spell9` |
| 76 | `chant` |
| 77 | `cutscene_fall` |
| 78 | `cutscene_eat` |
| 79 | `cutscene_eaten` |
| 80 | `cutscene_sit` |
| 81 | `cutscene_sit_talk0` |
| 82 | `cutscene_sit_talk1` |
| 83 | `cutscene_rise` |
| 84 | `cutscene_hide` |
| 85 | `cutscene_unhide` |
| 86 | `cutscene_cheer` |
| 87 | `cutscene_complete0` |
| 88 | `cutscene_complete1` |
| 89 | `cutscene_complete2` |
| 90 | `cutscene_complete3` |
| 91 | `cutscene_fail` |
| 92 | `cutscene_teleport_in` |
| 93 | `cutscene_teleport_out` |
| 94 | `cutscene_introduction` |
| 95 | `cutscene_idle` |
| 96 | `cutscene_talkfriendly0` |
| 97 | `cutscene_talkfriendly1` |
| 98 | `cutscene_talkidle` |
| 99 | `cutscene_talkagg0` |
| 100 | `cutscene_talkagg1` |
| 101 | `cutscene_talkagg2` |
| 102 | `cutscene_walklaugh` |
| 103 | `cutscene_slashvlad` |
| 104 | `cutscene_slashvladidle` |
| 105 | `cutscene_lookingforvlad` |
| 106 | `cutscene_shame` |
| 107 | `cutscene_woundedkneeling` |
| 108 | `cutscene_lastbreath` |
| 109 | `cutscene_stabbed` |
| 110 | `cutscene_stabbedidle` |
| 111 | `cutscene_woundedstanding` |
| 112 | `cutscene_woundedtalk` |
| 113 | `cutscene_losinghandbegin` |
| 114 | `cutscene_losinghandidle` |
| 115 | `cutscene_losinghandend` |
| 116 | `cutscene_diepose0` |
| 117 | `cutscene_diepose1` |
| 118 | `cutscene_flipcoin_begin` |
| 119 | `cutscene_flipcoin_idle` |
| 120 | `cutscene_flipcoin_end` |
| 121 | `cutscene_talkshame_begin` |
| 122 | `cutscene_talkshame_idle` |
| 123 | `cutscene_talkshame_end` |
| 124 | `emote_talk0` |
| 125 | `emote_point0` |
| 126 | `emote_terrified0` |
| 127 | `emote_horrified0` |
| 128 | `emote_sigh0` |
| 129 | `emote_rejoice0` |
| 130 | `emote_rejoice1` |
| 131 | `emote_confused0` |
| 132 | `emote_confused1` |
| 133 | `emote_confused2` |
| 134 | `emote_confused3` |
| 135 | `move_walk` |
| 136 | `move_wnd` |
| 137 | `move_run` |
| 138 | `move_sprint` |
| 139 | `move_panic` |
| 140 | `move_stumble` |
| 141 | `move_fall` |
| 142 | `move_jump_begin` |
| 143 | `move_jump_mid` |
| 144 | `move_jump_up` |
| 145 | `move_jump_down` |
| 146 | `move_jump_end` |
| 147 | `pickup_weapon` |
| 148 | `pickup_staff` |
| 149 | `pickup_magick` |
| 150 | `revive` |
| 151 | `talk_casual0` |
| 152 | `talk_casual1` |
| 153 | `talk_casual2` |
| 154 | `talk_casual3` |
| 155 | `talk_casual4` |
| 156 | `talk_alarmed0` |
| 157 | `talk_alarmed1` |
| 158 | `talk_alarmed2` |
| 159 | `talk_alarmed3` |
| 160 | `talk_greeting0` |
| 161 | `talk_greeting1` |
| 162 | `talk_terrified0` |
| 163 | `talk_terrified1` |
| 164 | `talk_sad0` |
| 165 | `talk_sad1` |
| 166 | `talk_intro0` |
| 167 | `talk_intro1` |
| 168 | `talk_intro2` |
| 169 | `use` |
| 170 | `spawn` |
| 171 | `spawn_woundedkneeling` |
| 172 | `despawn` |
| 173 | `removestatus0` |
| 174 | `removestatus1` |
| 175 | `removestatus2` |
| 176 | `spec_alert0` |
| 177 | `spec_alert1` |
| 178 | `spec_alert2` |
| 179 | `spec_alert3` |
| 180 | `spec_action0` |
| 181 | `spec_action1` |
| 182 | `spec_action2` |
| 183 | `spec_action3` |
| 184 | `spec_action4` |
| 185 | `spec_arm` |
| 186 | `spec_disarm` |
| 187 | `spec_materialize` |
| 188 | `spec_dormant` |
| 189 | `spec_etherealize` |
| 190 | `spec_grapple` |
| 191 | `spec_entangled` |
| 192 | `spec_entangled_attack` |
| 193 | `spec_sit_sleep` |
| 194 | `spec_sit` |
| 195 | `spec_throwitem` |
| 196 | `spec_musician_microphone` |
| 197 | `spec_musician_drum` |
| 198 | `spec_musician_saxophone` |
| 199 | `spec_ghost_drink` |
| 200 | `spec_ghost_poisoned` |
| 201 | `spec_wounded_idle` |
| 202 | `spec_wounded_talk0` |
| 203 | `spec_wounded_talk1` |
| 204 | `spec_wounded_talk2` |
| 205 | `spec_cart_front_enter` |
| 206 | `spec_cart_front_depart` |
| 207 | `spec_cart_front_arrive` |
| 208 | `spec_cart_front_exit` |
| 209 | `spec_cart_back_enter` |
| 210 | `spec_cart_back_sit` |
| 211 | `spec_cart_back_exit` |
| 212 | `spec_spawn_climbup0` |
| 213 | `spec_spawn_climbup1` |
| 214 | `special0` |
| 215 | `special1` |
| 216 | `special2` |
| 217 | `special3` |
| 218 | `special4` |
| 219 | `special5` |
| 220 | `special6` |
| 221 | `special7` |
| 222 | `special8` |
| 223 | `special_getup0` |
| 224 | `special_getup1` |
| 225 | `special_delorean` |
| 226 | `special_lookup` |
| 227 | `stunned` |
| 228 | `taunt0` |
| 229 | `taunt1` |
| 230 | `taunt2` |
| 231 | `totalanimations` |

## Sound banks (`SoundBanks`, flags, `int`)

The bank part of a `bank/cue` sound reference (dialog lines, actions, templates). Default for
dialog lines: Characters. (`ushort` in the original; XNB stores it as `int`.)

| Value | Name |
|---|---|
| 0 | `None` |
| 0x1 | `WaveBank` |
| 0x2 | `Music` |
| 0x4 | `Ambience` |
| 0x8 | `UI` |
| 0x10 | `Spells` |
| 0x20 | `Characters` |
| 0x40 | `Footsteps` |
| 0x80 | `Weapons` |
| 0x100 | `Misc` |
| 0x200 | `Additional` |
| 0x400 | `AdditionalMusic` |

## Dialog message type (`MessageType`)

A single value: `Say` = 0.

## Elements (flags, `int`)

Spell elements: damage, resistances, abilities, trigger conditions (`element`, `elements`). `Physical` is another name of Earth (the content writes `physical`); the names after Poison are groups (`beams`, `PhysicalElements`).

| Value | Name |
|---|---|
| 0 | `None` |
| 0x1 | `Earth` |
| 0x1 | `Physical` |
| 0x2 | `Water` |
| 0x4 | `Cold` |
| 0x8 | `Fire` |
| 0x10 | `Lightning` |
| 0x20 | `Arcane` |
| 0x40 | `Life` |
| 0x80 | `Shield` |
| 0x100 | `Ice` |
| 0x200 | `Steam` |
| 0x400 | `Poison` |
| 0xff3f | `Offensive` |
| 0xb0 | `Defensive` |
| 0xffff | `All` |
| 0xffff | `Magick` |
| 0xff | `Basic` |
| 0x371 | `Instant` |
| 0x171 | `InstantPhysical` |
| 0x270 | `InstantNonPhysical` |
| 0x64e | `StatusEffects` |
| 0xe0 | `ShieldElements` |
| 0x101 | `PhysicalElements` |
| 0x60 | `Beams` |

## Status effects (`StatusEffects`, flags, `short`)

`Life` is another name of Healing. The original's count sentinel `NumberOfTypes` (0x200) is left out.

| Value | Name |
|---|---|
| 0 | `None` |
| 0x1 | `Burning` |
| 0x2 | `Wet` |
| 0x4 | `Frozen` |
| 0x8 | `Cold` |
| 0x10 | `Poisoned` |
| 0x20 | `Healing` |
| 0x20 | `Life` |
| 0x40 | `Greased` |
| 0x80 | `Steamed` |
| 0x100 | `Bleeding` |

## Attack properties (`AttackProperties`, flags)

`short` in the original, stored as `int` in XNB. Knockback = Knockdown | Pushed.

| Value | Name |
|---|---|
| 0 | `None` |
| 0x1 | `Damage` |
| 0x2 | `Knockdown` |
| 0x4 | `Pushed` |
| 0x6 | `Knockback` |
| 0x8 | `Piercing` |
| 0x10 | `ArmourPiercing` |
| 0x20 | `Status` |
| 0x40 | `Entanglement` |
| 0x80 | `Stun` |
| 0x100 | `Bleed` |

## Factions (flags, `int`)

`factions="evil,friendly"` in scripts; NEUTRAL (all non-player factions) and PLAYERS are groups. The sentinel `NUMBER_OF_FACTIONS` (0xF) is left out.

| Value | Name |
|---|---|
| 0 | `NONE` |
| 0x1 | `EVIL` |
| 0x2 | `WILD` |
| 0x4 | `FRIENDLY` |
| 0x8 | `DEMON` |
| 0x10 | `UNDEAD` |
| 0x20 | `HUMAN` |
| 0x40 | `WIZARD` |
| 0xff | `NEUTRAL` |
| 0x100 | `PLAYER0` |
| 0x200 | `PLAYER1` |
| 0x400 | `PLAYER2` |
| 0x800 | `PLAYER3` |
| 0x1000 | `TEAM_RED` |
| 0x2000 | `TEAM_BLUE` |
| 0x3f00 | `PLAYERS` |

## AI orders (`Order`, `byte`)

`order` and `reaction` attributes, the order and reaction of spawn events in XNB.

| Value | Name |
|---|---|
| 0 | `None` |
| 1 | `Idle` |
| 2 | `Attack` |
| 3 | `Defend` |
| 4 | `Flee` |
| 5 | `Wander` |
| 6 | `Panic` |

## AI provocation (`ReactTo`, flags, `byte`)

`reactto="proximity, attack"` (spaces around names are allowed).

| Value | Name |
|---|---|
| 0 | `None` |
| 0x1 | `Attack` |
| 0x2 | `Proximity` |

## Cast types (`CastType`)

None and Blast share 0.

| Value | Name |
|---|---|
| 0 | `None` |
| 0 | `Blast` |
| 1 | `Force` |
| 2 | `Area` |
| 3 | `Self` |
| 4 | `Weapon` |
| 5 | `Magick` |

## Magicks (`MagickType`)

`magick` / `magickType` attributes, also as numbers (`magick="37"`). NrOfMagicks (35) ends the castable magicks; the values after it are item and enemy effects.

| Value | Name |
|---|---|
| 0 | `None` |
| 1 | `Revive` |
| 2 | `Grease` |
| 3 | `Haste` |
| 4 | `Invisibility` |
| 5 | `Teleport` |
| 6 | `Fear` |
| 7 | `Charm` |
| 8 | `ThunderB` |
| 9 | `Rain` |
| 10 | `Tornado` |
| 11 | `Blizzard` |
| 12 | `MeteorS` |
| 13 | `Conflagration` |
| 14 | `ThunderS` |
| 15 | `TimeWarp` |
| 16 | `Vortex` |
| 17 | `SUndead` |
| 18 | `SElemental` |
| 19 | `SDeath` |
| 20 | `SPhoenix` |
| 21 | `Nullify` |
| 22 | `Corporealize` |
| 23 | `CTD` |
| 24 | `Napalm` |
| 25 | `Portal` |
| 26 | `TractorPull` |
| 27 | `ProppMagick` |
| 28 | `Levitate` |
| 29 | `ChainLightning` |
| 30 | `Confuse` |
| 31 | `Wave` |
| 32 | `PerformanceEnchantment` |
| 33 | `JudgementSpray` |
| 34 | `Amalgameddon` |
| 35 | `NrOfMagicks` |
| 36 | `Grow` |
| 37 | `Shrink` |
| 38 | `Etherealize` |
| 39 | `Polymorph` |
| 40 | `Earthquake` |
| 41 | `Votal` |
| 42 | `KnockDownChance` |
| 43 | `SummonCross` |
| 44 | `VladZap` |
| 45 | `SpawnSlime` |
| 46 | `SpawnSlimeOverkill` |
| 47 | `GreaseSplash` |
| 48 | `EtherealClone` |
| 49 | `InflictBleedDamage` |
| 50 | `InflictPoisonDamage` |

## AI events (`AIEventType`) and loops (`LoopType`)

The tag of an AI event, and the `type` of a loop event.

| Value | Name |
|---|---|
| 0 | `Move` |
| 1 | `Animation` |
| 2 | `Face` |
| 3 | `Kill` |
| 4 | `Loop` |

| Value | Name |
|---|---|
| 0 | `Loop` |
| 1 | `Reverse` |

## Collision materials (`CollisionMaterial`, `byte`)

Surface materials of level collision meshes. The sentinel `NR_OF_MATERIAL` is left out.

| Value | Name |
|---|---|
| 0 | `Generic` |
| 1 | `Gravel` |
| 2 | `Grass` |
| 3 | `Wood` |
| 4 | `Snow` |
| 5 | `Stone` |
| 6 | `Mud` |
| 7 | `Reflect` |
| 8 | `Water` |
| 9 | `Lava` |

## Checked against the content

Every enum-valued attribute in the level scripts parses with these tables (6352 values of
`element(s)`, `faction(s)`, `order`, `reaction`, `reactto`, `castType`, `magick(Type)`,
`statusEffect` and the `animation` of AI events): `GameContentTests`.
