# OpenMagicka

An open-source reimplementation of the engine of **Magicka** (Arrowhead Game Studios, 2011),
in the spirit of [OpenMW](https://openmw.org): you bring the game data from your own copy,
OpenMagicka runs it on modern systems.

> Status: early development. Nothing is playable yet.

## You need the original game

OpenMagicka contains **no game assets** and never will. Buy Magicka on Steam
([AppID 42910](https://store.steampowered.com/app/42910/Magicka/)); OpenMagicka reads the
models, levels, textures, sounds and texts from that installation. Nothing is copied into this
repository or redistributed.

## Goals

- run on Windows, Linux, macOS and Steam Deck, 64-bit, modern graphics APIs (Vulkan, D3D12, Metal);
- behave like the original: spells, levels, triggers, co-op;
- fix what breaks on modern PCs: frame-rate dependent logic, controllers, resolutions;
- keep online co-op with our own protocol (no compatibility with the original client).

## Clean-room rules

The original executable is studied only to understand file formats and behaviour. Decompiled
code never enters this repository, its wiki or its issues: specifications are written in our own
words, tables and constants. See [docs/](docs/ENGINE_ANALYSIS.md).

Third-party libraries in `lib/` (FNA, FNA3D, FAudio, MojoShader) are used as they are, except
for our own FNA3D build for Windows x64: FNA3D with a few MojoShader fixes, kept as patches in
[native/](native/README.md). Those fixes were written with AI help, and the libraries'
maintainers do not accept AI-generated contributions: they stay in our fork and are never sent
upstream. Please respect that when reporting issues upstream.

Physics uses [Jitter2](https://github.com/notgiven688/jitterphysics2) (MIT) from NuGet, behind our
own facade in `OpenMagicka.Physics` ([ADR-0003](docs/adr/0003-physics.md)).

## Building

Requirements: .NET 10 SDK, Git, GitHub CLI (to fetch the native FNA libraries).

```bash
git clone --recursive https://github.com/vogster/OpenMagicka.git
cd OpenMagicka
sh tools/fetch_fnalibs.sh
dotnet build OpenMagicka.slnx
dotnet test tests/OpenMagicka.Tests
dotnet run --project src/OpenMagicka.Launcher
```

On Windows run the shell scripts from Git Bash. `tools/linux_check.sh` builds, tests and
smoke-runs the game on Linux inside Docker. On Windows x64 the launcher uses our FNA3D from
`native/win-x64` instead of the fetched one; `pwsh tools/build_fna3d.ps1` rebuilds it (Visual
Studio's C++ tools and CMake needed).

Tests that need the game files run when `MAGICKA_DIR` points at a Magicka installation.

## Level viewer

With the game's content, the launcher opens the level viewer (the game itself comes later):

```bash
dotnet run --project src/OpenMagicka.Launcher -- --content "<Magicka folder>" --scene wc_s1
```

WASD move, Q/E (or C/Space) go down/up, Shift is faster, the right mouse button looks around,
PageUp/PageDown switch between all scenes and models, Tab jumps between the scenes and the models
(props and characters, in their rest pose), F frames, Escape quits. G switches to the game's
camera (WASD then walk a stand-in player it follows; `--game-camera` starts with it). Scenes are
drawn with the original shaders; R switches to a simple forward view. `--screenshot <file.png>`
saves the first frame and exits.
`--check-effects` loads every original effect on the GPU and reports the result.

## Content tool

`omx` reads the game's content with OpenMagicka's readers:

```bash
dotnet run --project src/OpenMagicka.Tools -- check "<Magicka folder>"
dotnet run --project src/OpenMagicka.Tools -- dump "<Magicka folder>/Content/Levels/Swamp/sw_s1.xml"
dotnet run --project src/OpenMagicka.Tools -- info "<Magicka folder>/Content/Levels/Swamp/sw_s1.xnb"
```

`check` reads every XNB file, level, scene, dialog file, visual effect and language and lists
what fails. `dump` prints an `.xnb`, `.lvl` or content `.xml` file as JSON (lists and dictionaries are cut after
256 items; `--full` prints them all; byte buffers show their length). `info` prints an XNB
header and its type readers.

## Documentation

- [Engine analysis](docs/ENGINE_ANALYSIS.md): stack and content of the original
- [Architecture decisions](docs/adr/): stack, network
- [Risk checks](docs/engine/RISK_CHECKS.md): original shaders on FNA, XACT on FAudio, Linux
- Format and gameplay specifications in [docs/formats](docs/formats/), [docs/gameplay](docs/gameplay/), [docs/engine](docs/engine/)

Most documentation is in Russian.

## License

OpenMagicka is released under the [MIT License](LICENSE). Libraries in `lib/` keep their own
licenses (FNA: Ms-PL). Magicka and its assets are property of their respective owners;
OpenMagicka is not affiliated with Arrowhead Game Studios or Paradox Interactive.
