# ADR-0001. Technology stack: C# / .NET 10 LTS + FNA

- **Status:** accepted
- **Date:** 2026-10-23
- **Tasks:** OM-2, OM-19, OM-20

## Context

The original Magicka (2011) is written in C# / XNA 3.1 on .NET Framework 3.5 and runs poorly on
modern PCs. Causes found during the analysis ([Engine architecture](../engine/ARCHITECTURE.md)):

- 32-bit only, DirectX 9, XNA 3.1;
- the logic and loader threads touch the GPU under `lock(GraphicsDevice)`: stutters and crashes;
- variable logic time step clamped to dt ≤ 0.05 s: behaviour depends on the frame rate;
- gamepads through XInput and DirectInput;
- Windows only; file paths in code and on disk disagree on letter case;
- online play depends on dead services (Paradox, GameSparks) and the old Steam P2P.

OpenMagicka must load the assets of a purchased copy without conversion and run on Windows,
Linux, macOS and Steam Deck.

## Decision

**C# on .NET 10 LTS + FNA** (FNA3D, FAudio, SDL3).

| Layer | Choice |
|---|---|
| Language and runtime | C#, .NET 10 LTS, 64-bit |
| Framework | FNA (open-source reimplementation of XNA 4.0), as a submodule |
| Graphics | FNA3D: SDL GPU backend (Vulkan / D3D12 / Metal), OpenGL as fallback |
| Shaders | original fx_2_0 from `Content/Shaders` through MojoShader; our own HLSL as fallback |
| Audio | FAudio / FACT behind an `IAudioBackend` abstraction (XACT v45 and xWMA risk, OM-18) |
| Window, input | SDL3: any gamepad, hotplug, HiDPI, any resolution |
| Physics | Jitter2 behind our own facade ([ADR-0003](0003-physics.md)); the JigLibX license could not be confirmed |
| Animation | port of upstream XNAnimation (Ms-PL) |

Architecture rules that fix the problems of the original:

1. **Fixed simulation tick** (60 Hz), rendering interpolated between ticks.
   Behaviour does not depend on the frame rate (60, 144, 240 Hz), and the same tick is used by
   the network ([ADR-0002](0002-network.md)).
2. **Only the render thread touches the GPU.** Logic builds a CPU snapshot of the frame in a
   triple buffer; the loader decodes data and queues GPU resource creation on the render thread.
3. **Case-insensitive file system** on top of the user's `Content`.
4. **No services of the original:** DRM, telemetry, Paradox and GameSparks are not implemented.

## Options considered

| Option | Why not |
|---|---|
| MonoGame | does not run DX9 effect bytecode: the original shaders would have to be rewritten from scratch |
| Godot / Unity | moving the whole architecture onto a foreign engine with its own formats and shaders; XNA behaviour is lost |
| C++ from scratch | the most work, for performance a 2011 game does not need |
| .NET 8 | support ends in November 2026 |

## Consequences

- The original's model and ours are close (XNA), so behaviour carries over almost one to one.
- Assets are used as they are, without conversion.
- FNA is XNA 4.0 while the assets are XNA 3.1 (XNB v4): we write all readers ourselves (M1).
- FNA has no `EffectPool`: shared parameters (View, Projection, Time…) are set on every effect.
- `string.GetHashCode` is randomized in .NET: we implement the legacy string hash ourselves (OM-6).
- Two risks need checking before the main work: XACT on FAudio (OM-18) and fx_2_0 on FNA (OM-20).
  Results: [Risk checks](../engine/RISK_CHECKS.md).
