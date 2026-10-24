# OpenMagicka documentation

OpenMagicka is a clean-room open-source reimplementation of the Magicka engine (Arrowhead, 2011),
in the spirit of OpenMW: the player buys the original on Steam (AppID 42910) and OpenMagicka
loads the assets from that installation.

## Project rules

- **Clean-room.** The original code is studied only to understand formats and behaviour.
  Decompiled code never enters the repository, the wiki or the tasks: only descriptions in our
  own words, tables and constants.
- **No asset redistribution.** The repository contains no game files. The launcher finds the
  Steam installation and checks it.
- **No services of the original** (Steam/Paradox/GameSparks, DRM, telemetry, monetization).
- **FNA, FNA3D, FAudio and MojoShader are not changed upstream:** their maintainers do not accept
  AI-written code. Our fixes to FNA3D and MojoShader live in our own fork, as patches in
  [native/](../native/README.md), and are never sent upstream.

## Stack

C# / .NET 10 LTS + FNA (SDL3, FNA3D: Vulkan / D3D12 / Metal), 64-bit, fixed simulation tick.
Layers: `OpenMagicka.Content` → `OpenMagicka.Engine` → `OpenMagicka.Game` → `OpenMagicka.Launcher`;
game rules: `OpenMagicka.Gameplay`; physics: `OpenMagicka.Physics` (Jitter2 behind our facade,
[ADR-0003](adr/0003-physics.md)); network: `OpenMagicka.Net` and `OpenMagicka.Server`. Details:
[ADR-0001](adr/0001-stack.md).

Network play: our own protocol, host authority, star topology through the host, transport behind
an abstraction (UDP + our own lobby/relay server, Steam optional). Details:
[ADR-0002](adr/0002-network.md).

## Documents

- [Engine analysis](ENGINE_ANALYSIS.md): stack and content of the original
- [Stack risk checks](engine/RISK_CHECKS.md): original shaders on FNA, XACT on FAudio, Linux
- [XNB formats](formats/XNB_READERS.md): binary layouts of the content readers
- [Levels and triggers](formats/LEVELS_TRIGGERS.md): .lvl, scenes, conditions and actions, dialogs
- [Enumerations](formats/ENUMS.md): names and values of the original's enums (elements, factions, magicks, animations…)
- [Spells and combat](gameplay/SPELLS.md): elements, combinations, magicks, damage, statuses
- [Magicks](gameplay/MAGICKS.md): unlocks, casting magicks, the boost, what each magick does
- [Engine architecture](engine/ARCHITECTURE.md): main loop, rendering, physics, audio, input, saves
- [Entities, AI and network](engine/ENTITIES_AI.md)
- Decisions: [ADR-0001 Stack](adr/0001-stack.md), [ADR-0002 Network play](adr/0002-network.md), [ADR-0003 Physics](adr/0003-physics.md)
- [Backlog](TASKS.md): milestones and critical path; the tasks live in ChillaDesk, project OM

These files are mirrored into the ChillaDesk wiki of project OM with `python tools/wiki_sync.py`;
this page becomes the wiki home page.
