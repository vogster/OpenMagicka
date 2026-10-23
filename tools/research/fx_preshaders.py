"""Check the preshaders of DX9 fx_2_0 effects for temp register overflows.

Usage: python fx_preshaders.py <dir with .fxb> [-v]

A preshader is CPU-side bytecode (an FXLC block inside a PRES comment of a shader) that
computes constants. MojoShader sizes its temp register file from the highest temp index an
operand *starts* at, plus one, rounded up to a multiple of 4. An instruction that writes N
elements to a temp starting at index i needs i + N registers. This tool reports every
preshader where the second number is larger than the first.

FXLC layout: "FXLC", u32 instruction count, then per instruction:
  u32 opcode token (opcode in the high 16 bits, element count in the low 8 bits),
  u32 operand count without the destination, then (count + 1) operands of
  u32 array count, u32 type (1 literal, 2 input, 4 output, 7 temp), u32 index,
  followed by 2 u32 per array. The destination is the last operand.
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fx_states  # noqa: E402

TEMP = 7


def comment_blocks(code, start=4):
    """Yields (fourcc, payload) of the comment tokens at the start of a shader or PRES block."""
    p = start
    while p + 4 <= len(code):
        tok = struct.unpack_from("<I", code, p)[0]
        if tok & 0xFFFF != 0xFFFE:
            return
        n = (tok >> 16) & 0x7FFF
        payload = code[p + 4:p + 4 + 4 * n]
        yield payload[:4], payload[4:]
        p += 4 + 4 * n


def fxlc_instructions(fxlc):
    count = struct.unpack_from("<I", fxlc, 0)[0]
    p = 4
    for _ in range(count):
        opcodetok, nops = struct.unpack_from("<II", fxlc, p)
        p += 8
        operands = []
        for _ in range(nops + 1):
            arrays, kind, index = struct.unpack_from("<III", fxlc, p)
            p += 12 + 8 * arrays
            operands.append((kind, index))
        yield (opcodetok >> 16) & 0xFFFF, opcodetok & 0xFF, operands


def instructions_end(fxlc):
    """Byte offset just past the last instruction (the block may carry trailing data)."""
    count = struct.unpack_from("<I", fxlc, 0)[0]
    p = 4
    for _ in range(count):
        nops = struct.unpack_from("<I", fxlc, p + 4)[0]
        p += 8
        for _ in range(nops + 1):
            arrays = struct.unpack_from("<I", fxlc, p)[0]
            p += 12 + 8 * arrays
    return p


def check_preshader(fxlc):
    """Returns (MojoShader temp_count, registers needed, instructions)."""
    insts = list(fxlc_instructions(fxlc))
    highest_start = -1
    needed = 0
    for _op, elems, operands in insts:
        for kind, index in operands:
            if kind == TEMP:
                highest_start = max(highest_start, index)
        kind, index = operands[-1]
        if kind == TEMP:
            needed = max(needed, index + elems)
    allocated = (highest_start + 1 + 3) & ~3
    return allocated, needed, insts


MOV = 0x1000
COMMENT = 0xFFFE


def _comment_at(code, pos):
    tok = struct.unpack_from("<I", code, pos)[0]
    if tok & 0xFFFF != COMMENT:
        raise ValueError("expected a comment token")
    return (tok >> 16) & 0x7FFF


def widen_temps(code):
    """Work around MojoShader under-allocating preshader temps (a DOT writes as many
    elements as its inputs have, past the end of the temp file). Appends one
    MOV temp[need-1] <- temp[need-1] to every overflowing preshader: it does not change
    the result but makes MojoShader allocate need temps (rounded up to 4).
    Returns (new shader bytes, number of preshaders patched)."""
    out = bytearray(code)
    patched = 0
    pos = 4
    while pos + 4 <= len(out):
        if struct.unpack_from("<I", out, pos)[0] & 0xFFFF != COMMENT:
            break
        length = _comment_at(out, pos)
        if out[pos + 4:pos + 8] == b"PRES":
            inner = pos + 12  # comment token, "PRES", preshader version token
            end = pos + 4 + 4 * length
            while inner < end:
                if struct.unpack_from("<I", out, inner)[0] & 0xFFFF != COMMENT:
                    break  # the preshader ends with an end-of-shader token
                ilen = _comment_at(out, inner)
                if out[inner + 4:inner + 8] == b"FXLC":
                    fxlc = bytes(out[inner + 8:inner + 4 + 4 * ilen])
                    allocated, needed, _ = check_preshader(fxlc)
                    if needed > allocated:
                        mov = struct.pack("<8I", (MOV << 16) | 1, 1,
                                          0, TEMP, needed - 1, 0, TEMP, needed - 1)
                        insert_at = inner + 8 + instructions_end(fxlc)
                        out[insert_at:insert_at] = mov
                        count = struct.unpack_from("<I", out, inner + 8)[0]
                        struct.pack_into("<I", out, inner + 8, count + 1)
                        struct.pack_into("<I", out, inner, ((ilen + 8) << 16) | COMMENT)
                        length += 8
                        struct.pack_into("<I", out, pos, (length << 16) | COMMENT)
                        end += 32
                        patched += 1
                        ilen += 8
                inner += 4 + 4 * ilen
        pos += 4 + 4 * length
    return bytes(out), patched


def shader_preshaders(code):
    for cc, payload in comment_blocks(code):
        if cc == b"PRES":
            for inner_cc, inner in comment_blocks(payload, 4):
                if inner_cc == b"FXLC":
                    yield inner


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    verbose = "-v" in sys.argv
    bad = 0
    for f in sorted(os.listdir(sys.argv[1])):
        if not f.endswith(".fxb"):
            continue
        with open(os.path.join(sys.argv[1], f), "rb") as fh:
            data = fh.read()
        found, fx = fx_states.parse(data)
        blob = data[8:]
        for at in fx_states.resources(fx):
            ti, pi, _el, si, _usage, size = struct.unpack_from("<IIIIII", blob, at)
            if ti == 0xFFFFFFFF:
                continue
            code = blob[at + 24:at + 24 + size]
            for fxlc in shader_preshaders(code):
                allocated, needed, insts = check_preshader(fxlc)
                if needed > allocated:
                    bad += 1
                    print(f"OVERFLOW {f[:-4]} tech {ti} pass {pi} state {si}: "
                          f"MojoShader allocates {allocated} temps, instructions need {needed}")
                    if verbose:
                        for op, elems, operands in insts:
                            print(f"    op {op:04x} x{elems}: {operands}")
    print(f"{bad} preshaders overflow their temp registers")


if __name__ == "__main__":
    main()
