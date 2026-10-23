# Stack risk checks (OM-20, OM-18, OM-2)

Date: 2026-10-23. Machine: Windows 11, AMD RX 9070 XT, FNA master `00498b0`, fnalibs
`FNA-XNA/fnalibs-dailies` run 39715067086. Stack decision: [ADR-0001](../adr/0001-stack.md).

## Summary

| Risk | Result |
|---|---|
| Original fx_2_0 shaders on FNA (OM-20) | **46 of 51** effects load and draw after patching the bytecode; 5 crash inside MojoShader, cause not found yet |
| XACT on FAudio (OM-18) | content version 45 is supported; **1000 cues** from 9 banks play; **xWMA does not work** (Windows: crash, Linux/macOS: no decoder) |
| Linux (OM-2) | build, 21 tests and the window work in Docker (`tools/linux_check.sh`, Xvfb + Mesa llvmpipe) |

The FNA stack holds, but needs two pieces of work on our side: an effect patcher and xWMA
conversion. Neither requires changes to FNA, FNA3D, FAudio or MojoShader.

> FNA, FNA3D and MojoShader ship a `CLAUDE.md`: their maintainers do not accept AI-written code.
> We do not modify them; problems we find can be reported to them as issues, but a human has to
> write and verify the report.

## 1. Shaders (OM-20)

Tools: `tools/research/extract_effects.py` (pulls fx_2_0 out of `Content/Shaders/*.xnb`),
`tools/research/fx_states.py` (lists pass states and patches them), `spikes/ShaderSpike`
(loads an effect into FNA and draws a quad with every pass; `run_all.sh` runs one process per effect).

### 1.1 Unsupported pass states

FNA throws `NotImplementedException` on render states that XNA 4.0 does not have. The 51
effects use four of them: `ALPHATESTENABLE` (18 effects), `ALPHAFUNC`, `ALPHAREF` (2 each),
`POINTSCALEENABLE` (1). Almost everywhere alpha test is **turned off** (`=0`), which is the
default behaviour of modern APIs anyway. It is turned on in 3 passes
(`ChasmEffect/AnimatedModelShadow`, `SkinnedModelDeferredEffect/AnimatedModelAlpha` and
`/AnimatedModelOccluded`) with the rule `alpha > 0`: only fully transparent pixels are
discarded. These passes need their own replacement (discard in the shader) — not done yet.

**Patch:** state entries (16 bytes) are removed from the structure part of the fx_2_0 file and
the pass state count is decreased. Every offset points into the data blob before the structure,
so no offset moves. But resources (shaders) are bound to a state by index (technique, pass,
**state index**), so the indices in the resource table are renumbered. The format is taken from
the documentation of Wine's d3dx9 parser.

### 1.2 Samplers

XNA has no `BORDER` / `MIRRORONCE` addressing and no border colour. `BloomEffect` and
`BuffEffect` use `BORDER` (Buff also uses `BORDERCOLOR`). The patch changes the values to
`CLAMP` in place and turns the `BORDERCOLOR` entry into a repeated `ADDRESSU = CLAMP`; the
structure does not change. Difference from the original: at texture edges the edge pixel is
repeated instead of transparent black.

### 1.3 Unresolved: 5 effects crash in MojoShader

| Effect | Crash |
|---|---|
| `RenderDeferredEffectWOVT`, `RenderDeferredEffectWVT` | `0xC0000005` in `FNA3D_ApplyEffect`: `memcpy` writes 32 bytes to address `NULL` |
| `CapsuleLight`, `Maya/SkinnedModelEffect_0` | the same |
| `ArcaneEffect` | `0xC0000374` (heap corruption) |

The same on Vulkan (SDL_GPU) and OpenGL, and without the patch too — so the cause is in the
effects' contents, not in the backend and not in the patch. Hypotheses checked and **rejected**:
shader selection from an array through a preshader (these passes hold plain shaders), int/bool
preshader inputs (all float), bool shader constants (working effects have them too). From the
MojoShader code, a write to `NULL` is possible in `MOJOSHADER_effectCommitChanges` while
parameters are copied into registers. Only a debug build of FNA3D will show the exact spot.

**This is critical:** `RenderDeferredEffect` is the main material of static level geometry.

Options (in order):
1. Build FNA3D + MojoShader in Debug locally (not for contribution), find the line and the
   condition, and work around it with a data patch if the cause is in the data. If it is a
   MojoShader bug, report it in an issue.
2. Our own HLSL for these 5 effects with the same parameter and G-buffer layout, compiled to
   fx_2_0 with `fxc` from the Windows SDK; FNA already accepts such bytecode.

### 1.4 Other notes

- D3D12 (`FNA3D_FORCE_DRIVER=SDLGPU`, `SDL_GPU_DRIVER=direct3d12`) printed nothing even for a
  working effect — not investigated. FNA3D picks Vulkan by default.
- The spike draws without textures and with zero parameters: "draws" means "the pipeline was
  built and drawing did not crash", not "the picture matches the original". Visual comparison
  is the job of the viewer (OM-22).

## 2. Audio (OM-18)

Tool: `spikes/AudioSpike` (`magicka.xgs`, every `.xwb` — streaming ones through the constructor
with an offset, every `.xsb`; each cue plays for 50 ms). `AUDIOSPIKE_BANKS` limits the banks,
`AUDIOSPIKE_TRACE=1` prints every cue.

- All banks are XACT content version **45** (XACT 3.4), which FACT supports.
- Banks without xWMA: `Additional` (451 cues), `Additionalmusic` (29), `Ambience` (99),
  `Footsteps` (16), `Misc` (97), `Music` (36), `Spells` (159), `UI` (28), `Weapons` (85) —
  **1000 cues, 0 errors**, including streamed music.
- xWMA is in `Wave Bank.xwb` (384 sounds) and `Language/<lang>/Loc.xwb` (speech). It is used by
  `Characters.xsb` and `Sound Bank.xsb`. On Windows FAudio is built with `wmadec` (Media
  Foundation) but crashes with `0xC0000094` (division by zero) on the first WMA cue. The Linux
  and macOS builds have no WMA decoder at all.

**Solution:** on first launch the launcher transcodes the WMA entries of the user's
`Wave Bank.xwb` and `Loc.xwb` to PCM or MS-ADPCM and writes new banks into the OpenMagicka data
folder (not into the game folder and not into the repository). Decoder: FFmpeg (it has an xWMA
demuxer) or our own WMAv2 decoder. Audio stays behind `IAudioBackend`, as planned in ADR-0001.

## 3. Linux (OM-2)

`tools/linux_check.sh` in the `mcr.microsoft.com/dotnet/sdk:10.0` container: builds the
solution, runs the 21 tests and runs the game for 8 seconds under Xvfb. FNA3D picked OpenGL 4.5
(Mesa llvmpipe), MojoShader the `glsl120` profile. The native `lib64/` libraries are picked up
from the output folder. Not checked on real Linux hardware with Vulkan.
