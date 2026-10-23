# OpenMagicka — backlog

The backlog lives in ChillaDesk, project **OM** (tasks with blocking links between them).
The documentation in `docs/` is mirrored into the project wiki with:

```bash
python tools/wiki_sync.py
```

## Milestones

| Milestone | Scope | Critical path |
|---|---|---|
| M0 Project foundation | license, .NET 10 + FNA scaffold, launcher, case-insensitive VFS, legacy string hash | OM-2 → OM-5, OM-6 |
| M1 Content pipeline | LZX, XNB v4, all readers, XML parsers, XACT check | OM-8 → OM-9 → OM-10 → OM-12 → OM-13 |
| M2 Rendering | threads, original shaders through MojoShader, deferred, particles | OM-19 → OM-20 → OM-22 → OM-23 |
| M3 Game core | physics, entities, spells, damage, magicks, AI, nav mesh | OM-34 → OM-35 → OM-36 |
| M4 Levels and scripts | triggers, 24 conditions, 113 actions, checkpoints | OM-43 → OM-45 → OM-47 → OM-50 (chapter 1) |
| M5 UI, audio, saves | audio, localization, menu, HUD, save import | |
| M6 Multiplayer | local co-op, own network protocol | |

Main open risks: `ArcaneEffect` still crashes in MojoShader (OM-62); xWMA playback (OM-61);
the JigLibX license (OM-7).
