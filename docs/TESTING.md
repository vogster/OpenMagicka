# Testing the OpenMagicka beta

OpenMagicka is a free, open-source re-creation of Magicka's engine. It has none of the game's files: it plays
**your own copy** of Magicka (the Steam version, 1.10.4.2), which it finds by itself. Thank you for testing it!

## What you need

- Magicka installed through Steam (any library folder). OpenMagicka only reads its files and never changes them.
- Windows 10/11 (64-bit) or Linux (x64). No .NET or other runtime to install: the beta carries its own.
- On Linux: `ffmpeg` installed (`sudo apt install ffmpeg` or your distribution's package), for the game's music and
  voices. Without it the game runs, but some of its sound is missing.

## Starting it

1. Unpack the archive anywhere (not inside Magicka's folder): the `.zip` on Windows, the `.tar.gz` on Linux
   (`tar -xzf OpenMagicka-*.tar.gz`).
2. Run `OpenMagicka.exe` (Windows) or `./OpenMagicka` (Linux).
3. The first start converts the game's sound into a format OpenMagicka can play. This happens once and takes a minute
   or two. The window may show the menu before the conversion finishes.

If Magicka is not found (it lives outside Steam, say), start OpenMagicka with its folder:

```
OpenMagicka --content "D:\Games\Magicka"
```

Or set the `MAGICKA_DIR` environment variable to that folder. Once found, the folder is remembered in the settings.

## What is in the beta

- The main menu, the three campaign save slots, the chapter choice and the options (sound, health bars, damage
  numbers, spell wheel, language, window or full screen, resolution, V-Sync).
- The campaign's twelve chapters, one local player, with keyboard and mouse or a game pad.
- Pause (Esc or Start): resume, restart the chapter, options, quit to the menu.
- Checkpoints. When every player dies, the chapter starts again from the last checkpoint. A finished chapter goes on
  to the next one.

What is not in it yet: the 3D book and its page turning, the intro and the map cutscenes between chapters,
multiplayer, challenges, versus, DLC, the magick book, and saving at a checkpoint across runs (continuing a slot
starts its chapter from the beginning).

## Controls

The original's: the elements on Q W E R / A S D F, walk with the left mouse button, cast with the right one (with
Shift held: an area spell), cast on yourself with the middle one, Ctrl to block, Tab for the inventory, the mouse wheel
to pick a magick, Esc to pause. A game pad works as in the original (Start pauses).

## Where OpenMagicka keeps its files

| What | Windows | Linux |
|---|---|---|
| Settings, saves | `%LOCALAPPDATA%\OpenMagicka` | `~/.local/share/OpenMagicka` |
| Log of the last run (and the one before) | `%LOCALAPPDATA%\OpenMagicka\logs` | `~/.local/share/OpenMagicka/logs` |
| Converted sound | `%LOCALAPPDATA%\OpenMagicka\Audio` | `~/.local/share/OpenMagicka/Audio` |

Deleting the folder resets everything (the sound is converted again on the next start).

## Reporting a problem

Please tell us:

1. What you did and what happened (a screenshot helps a lot).
2. Which chapter and where in it.
3. The log: `openmagicka.log` from the logs folder above, from the run where it happened. Its first line has the
   version and your system.

Crashes, things drawn wrongly, wrong sounds, spells that behave differently from the original: all of it is useful.
