"""List the render states set by every pass of DX9 fx_2_0 effect binaries.

Usage: python fx_states.py <dir with .fxb> [--unsupported] [--strip <out dir>]
--unsupported prints only states FNA's Effect.Apply does not handle (it throws on them).
--strip writes copies of the effects with those state entries removed and Border/MirrorOnce
        sampler addressing replaced by Clamp (research only).

fx_2_0 layout (as documented by Wine's d3dx9 effect parser): u32 tag 0xFEFF0901, u32 offset;
the data blob starts at byte 8 and the structure part at 8 + offset:
  u32 parameterCount, techniqueCount, unused, objectCount
  parameter: u32 typedef, value, flags, annotationCount; annotationCount x (u32 typedef, value)
  technique: u32 name, annotationCount, passCount; annotations; passes
  pass:      u32 name, annotationCount, stateCount; annotations; states
  state:     u32 operation, index, typedef, value
Offsets are relative to the data blob. Strings are u32 length + bytes.
Operations 0..102 are render states in D3DRENDERSTATETYPE order (same as MojoShader).
"""
import os
import struct
import sys

RENDER_STATES = (
    "ZENABLE FILLMODE SHADEMODE ZWRITEENABLE ALPHATESTENABLE LASTPIXEL SRCBLEND DESTBLEND CULLMODE "
    "ZFUNC ALPHAREF ALPHAFUNC DITHERENABLE ALPHABLENDENABLE FOGENABLE SPECULARENABLE FOGCOLOR "
    "FOGTABLEMODE FOGSTART FOGEND FOGDENSITY RANGEFOGENABLE STENCILENABLE STENCILFAIL STENCILZFAIL "
    "STENCILPASS STENCILFUNC STENCILREF STENCILMASK STENCILWRITEMASK TEXTUREFACTOR "
    + " ".join(f"WRAP{i}" for i in range(16)) +
    " CLIPPING LIGHTING AMBIENT FOGVERTEXMODE COLORVERTEX LOCALVIEWER NORMALIZENORMALS "
    "DIFFUSEMATERIALSOURCE SPECULARMATERIALSOURCE AMBIENTMATERIALSOURCE EMISSIVEMATERIALSOURCE "
    "VERTEXBLEND CLIPPLANEENABLE POINTSIZE POINTSIZE_MIN POINTSPRITEENABLE POINTSCALEENABLE "
    "POINTSCALE_A POINTSCALE_B POINTSCALE_C MULTISAMPLEANTIALIAS MULTISAMPLEMASK PATCHEDGESTYLE "
    "DEBUGMONITORTOKEN POINTSIZE_MAX INDEXEDVERTEXBLENDENABLE COLORWRITEENABLE TWEENFACTOR BLENDOP "
    "POSITIONDEGREE NORMALDEGREE SCISSORTESTENABLE SLOPESCALEDEPTHBIAS ANTIALIASEDLINEENABLE "
    "MINTESSELLATIONLEVEL MAXTESSELLATIONLEVEL ADAPTIVETESS_X ADAPTIVETESS_Y ADAPTIVETESS_Z "
    "ADAPTIVETESS_W ENABLEADAPTIVETESSELLATION TWOSIDEDSTENCILMODE CCW_STENCILFAIL CCW_STENCILZFAIL "
    "CCW_STENCILPASS CCW_STENCILFUNC COLORWRITEENABLE1 COLORWRITEENABLE2 COLORWRITEENABLE3 "
    "BLENDFACTOR SRGBWRITEENABLE DEPTHBIAS SEPARATEALPHABLENDENABLE SRCBLENDALPHA DESTBLENDALPHA "
    "BLENDOPALPHA"
).split()

# States FNA's Effect.Apply handles (src/Graphics/Effect/Effect.cs); anything else throws.
FNA_HANDLED = set(
    "ZENABLE FILLMODE ZWRITEENABLE SRCBLEND DESTBLEND CULLMODE ZFUNC ALPHABLENDENABLE STENCILENABLE "
    "STENCILFAIL STENCILZFAIL STENCILPASS STENCILFUNC STENCILREF STENCILMASK STENCILWRITEMASK "
    "MULTISAMPLEANTIALIAS MULTISAMPLEMASK COLORWRITEENABLE BLENDOP SCISSORTESTENABLE "
    "SLOPESCALEDEPTHBIAS TWOSIDEDSTENCILMODE CCW_STENCILFAIL CCW_STENCILZFAIL CCW_STENCILPASS "
    "CCW_STENCILFUNC COLORWRITEENABLE1 COLORWRITEENABLE2 COLORWRITEENABLE3 BLENDFACTOR DEPTHBIAS "
    "SEPARATEALPHABLENDENABLE SRCBLENDALPHA DESTBLENDALPHA BLENDOPALPHA".split()
)


class Fx:
    def __init__(self, data):
        tag, offset = struct.unpack_from("<II", data, 0)
        if tag != 0xFEFF0901:
            raise ValueError("not an fx_2_0 effect")
        self.blob = data[8:]
        self.pos = offset

    def u32(self):
        v = struct.unpack_from("<I", self.blob, self.pos)[0]
        self.pos += 4
        return v

    def string(self, off):
        n = struct.unpack_from("<I", self.blob, off)[0]
        return self.blob[off + 4:off + 4 + n].rstrip(b"\0").decode("ascii", "replace")

    def value(self, off):
        return struct.unpack_from("<I", self.blob, off)[0]

    def skip_annotations(self, n):
        self.pos += 8 * n


# Sampler state operations (Wine's d3dx9 state table) and D3DTEXTUREADDRESS values.
SAMP_ADDRESS_U, SAMP_ADDRESS_W, SAMP_BORDER_COLOR = 165, 167, 168
ADDRESS_CLAMP, ADDRESS_BORDER, ADDRESS_MIRRORONCE = 3, 4, 5
SAMPLER_TYPES = range(10, 15)  # D3DXPT_SAMPLER .. D3DXPT_SAMPLERCUBE


def sampler_states(data):
    """Yields (parameter name, state entry position, operation, value position) for every
    sampler state stored in a sampler parameter's value: u32 count + count x state."""
    fx = Fx(data)
    params = fx.u32()
    fx.pos += 12
    for _ in range(params):
        typedef, value = fx.u32(), fx.u32()
        fx.u32()
        fx.skip_annotations(fx.u32())
        ptype, _pclass, name = struct.unpack_from("<III", fx.blob, typedef)
        if ptype not in SAMPLER_TYPES:
            continue
        count = struct.unpack_from("<I", fx.blob, value)[0]
        for k in range(count):
            at = value + 4 + 16 * k
            op, _index, _typedef, sval = struct.unpack_from("<IIII", fx.blob, at)
            yield fx.string(name), at, op, sval


def parse(data):
    """Returns (passes, fx) where passes are (tech index, pass index, tech name, pass name,
    states, stateCount field position); fx.pos is left at the end of the technique section."""
    fx = Fx(data)
    params, techniques, _, _ = fx.u32(), fx.u32(), fx.u32(), fx.u32()
    for _ in range(params):
        fx.u32(), fx.u32(), fx.u32()
        fx.skip_annotations(fx.u32())
    result = []
    for ti in range(techniques):
        tname, tann, npass = fx.string(fx.u32()), fx.u32(), fx.u32()
        fx.skip_annotations(tann)
        for pi in range(npass):
            pname, pann, nstate = fx.string(fx.u32()), fx.u32(), fx.u32()
            fx.skip_annotations(pann)
            count_at = fx.pos - 8 * pann - 4  # the stateCount field
            states = []
            for _ in range(nstate):
                at = fx.pos
                op, _index, _typedef, value = fx.u32(), fx.u32(), fx.u32(), fx.u32()
                name = RENDER_STATES[op] if op < len(RENDER_STATES) else f"op{op}"
                states.append((name, fx.value(value), at))
            result.append((ti, pi, tname, pname, states, count_at))
    return result, fx


def resources(fx):
    """Walks the string and resource tables that follow the techniques. Yields the position
    of each resource header: u32 technique, pass (or parameter), element, state, usage."""
    strings, count = fx.u32(), fx.u32()
    for _ in range(strings):
        fx.u32()
        size = fx.u32()
        fx.pos += (size + 3) & ~3
    for _ in range(count):
        at = fx.pos
        fx.pos += 20
        size = fx.u32()
        fx.pos += (size + 3) & ~3
        yield at


def passes(data):
    for _, _, tname, pname, states, _ in parse(data)[0]:
        yield tname, pname, states


def unsupported(name):
    return name in RENDER_STATES and name not in FNA_HANDLED


def strip(data):
    """Remove unsupported state entries and renumber the resources (shaders) bound to the
    remaining states. Entries live in the structure part and every offset points into the
    data blob before it, so deleting 16-byte entries keeps offsets valid."""
    found, fx = parse(data)
    blob = bytearray(data[8:])
    remap, cuts, cuts_extra = {}, [], []
    for ti, pi, _, _, states, count_at in found:
        kept = 0
        for si, (name, _, at) in enumerate(states):
            if unsupported(name):
                cuts.append(at)
            else:
                remap[(ti, pi, si)] = kept
                kept += 1
        struct.pack_into("<I", blob, count_at, kept)
    for at in resources(fx):
        ti, pi, _element, si = struct.unpack_from("<IIII", blob, at)
        if ti == 0xFFFFFFFF:
            continue  # bound to a parameter (sampler state), not to a pass
        if (ti, pi, si) not in remap:
            raise ValueError(f"resource bound to removed state {(ti, pi, si)}")
        struct.pack_into("<I", blob, at + 12, remap[(ti, pi, si)])
    if fx.pos != len(blob):
        raise ValueError(f"unparsed tail: {len(blob) - fx.pos} bytes")
    # XNA has no Border / MirrorOnce addressing and no border colour: use Clamp instead.
    # Values are patched in place, so no offsets move.
    for _, at, op, sval in sampler_states(data):
        value = struct.unpack_from("<I", blob, sval)[0]
        if SAMP_ADDRESS_U <= op <= SAMP_ADDRESS_W and value in (ADDRESS_BORDER, ADDRESS_MIRRORONCE):
            struct.pack_into("<I", blob, sval, ADDRESS_CLAMP)
            cuts_extra.append(at)
        elif op == SAMP_BORDER_COLOR:
            struct.pack_into("<I", blob, at, SAMP_ADDRESS_U)
            struct.pack_into("<I", blob, sval, ADDRESS_CLAMP)
            cuts_extra.append(at)
    for at in sorted(cuts, reverse=True):
        del blob[at:at + 16]
    return data[:8] + bytes(blob), len(cuts) + len(cuts_extra)


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    only_unsupported = "--unsupported" in sys.argv
    out = sys.argv[sys.argv.index("--strip") + 1] if "--strip" in sys.argv else None
    if out:
        os.makedirs(out, exist_ok=True)
    totals = {}
    for f in sorted(os.listdir(sys.argv[1])):
        if not f.endswith(".fxb"):
            continue
        with open(os.path.join(sys.argv[1], f), "rb") as fh:
            data = fh.read()
        if out:
            patched, removed = strip(data)
            with open(os.path.join(out, f), "wb") as fh:
                fh.write(patched)
            if removed:
                print(f"strip {f[:-4]}: {removed} state entries removed")
            continue
        for tech, pas, states in passes(data):
            shown = [(n, v) for n, v, _ in states if not only_unsupported or unsupported(n)]
            for n, v in shown:
                totals.setdefault(n, set()).add(f[:-4])
            if shown:
                print(f"{f[:-4]}/{tech}/{pas}: " + ", ".join(f"{n}={v}" for n, v in shown))
    print("== effects per state")
    for n, effects in sorted(totals.items(), key=lambda kv: -len(kv[1])):
        print(f"{n}: {len(effects)}")


if __name__ == "__main__":
    main()
