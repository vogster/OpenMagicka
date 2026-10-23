# OpenMagicka's own FNA3D

This folder holds OpenMagicka's fork of FNA3D, the native graphics library of FNA, and of
MojoShader, the shader translator compiled into FNA3D. The fork is a set of patches against the
versions FNA pins (`lib/FNA/lib/FNA3D` and its `MojoShader` submodule) and one prebuilt binary.

**These are altered versions of FNA3D and MojoShader**, not the originals, as their zlib license
requires us to say. The changes were written with AI help. FNA3D's and MojoShader's maintainers do
not accept AI-generated contributions, so the changes stay here and are **never sent upstream**: no
pull requests and no patches in issue reports. A human may report the bugs upstream in their own
words (OM-64).

## Patches

`patches/MojoShader/*.patch` are applied in name order to MojoShader. Each one starts with the
reason for the change; the changed code is marked `OpenMagicka fork`.

| Patch | What it fixes |
|---|---|
| `0001-hlsl-uniform-array-define.patch` | D3D11: the HLSL of effects with relatively addressed uniforms (the skinned characters' bone palette) did not compile |
| `0002-spirv-scalar-exp.patch` | Vulkan: `AdditiveEffect` (light shafts and other additive objects) drew wrong colours, because AMD's driver miscompiles Exp2 of a whole vec4; `exp` is now one scalar Exp2 per written component |
| `0003-preshader-dotswiz.patch` | All drivers: `ArcaneEffect` corrupted the heap when it was created, because its preshader uses `dotswiz6`, an opcode with 7 operands that MojoShader did not know and wrote past its 4-operand array; the opcode (and `dotswiz8`) is now supported and the array cannot overflow |

## Builds

| Platform | File | Built by |
|---|---|---|
| Windows x64 | `win-x64/FNA3D.dll` | `pwsh tools/build_fna3d.ps1` (Visual Studio C++ tools, CMake; downloads SDL3's development files) |

The C runtime is linked in, so the DLL needs only SDL3 and Windows. `win-x64/SOURCE.txt` records
the FNA3D and MojoShader commits, the SDL3 headers, the compiler and the hashes of the patches and
of the DLL; a test fails when they no longer match the submodules or the patches, so rebuild after
updating FNA or changing a patch. The licenses of FNA3D and MojoShader are in `licenses/`.

The launcher ships this DLL instead of the FNA3D of the fetched fnalibs, with SOURCE.txt next to
it as `FNA3D.OpenMagicka.txt`; the game takes FNA3D for ours only when the DLL's hash matches that
record (`GraphicsDriver.HasOwnFna3d`), so a stock FNA3D in its place keeps the stock defaults. On other platforms it uses
the stock FNA3D and defaults to the OpenGL driver, which the original effects need there
(`GraphicsDriver`, docs/engine/ARCHITECTURE.md §3.2).
