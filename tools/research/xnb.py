"""Minimal XNB (XNA 3.1, v4) reader with LZX decompression. Research tool."""
import struct, sys

EXTRA = []
j = 0
for i in range(0, 52, 2):
    EXTRA += [j, j]
    if i != 0 and j < 17: j += 1
POSBASE = [0]
for e in EXTRA[:-1]: POSBASE.append(POSBASE[-1] + (1 << e))

class Bits:
    def __init__(s, data): s.d, s.p, s.buf, s.n = data, 0, 0, 0
    def _fill(s, k):
        while s.n < k:
            w = s.d[s.p] | (s.d[s.p+1] << 8) if s.p + 1 < len(s.d) else 0
            s.p += 2; s.buf = (s.buf << 16) | w; s.n += 16
    def peek(s, k): s._fill(k); return (s.buf >> (s.n - k)) & ((1 << k) - 1)
    def skip(s, k): s.n -= k; s.buf &= (1 << s.n) - 1
    def read(s, k):
        if k == 0: return 0
        v = s.peek(k); s.skip(k); return v

def build(lens):
    tbl = {}; code = 0
    for L in range(1, 17):
        for sym, l in enumerate(lens):
            if l == L: tbl[(L, code)] = sym; code += 1
        code <<= 1
    return tbl

def decode(b, tbl):
    code = 0
    for L in range(1, 17):
        code = (code << 1) | b.read(1)
        s = tbl.get((L, code))
        if s is not None: return s
    raise ValueError("bad huffman code")

class Lzx:
    def __init__(s, wbits=16):
        s.wsize = 1 << wbits; s.win = bytearray(s.wsize); s.pos = 0
        s.main_n = 256 + (wbits * 2) * 8
        s.mainl = [0] * s.main_n; s.lenl = [0] * 249
        s.R = [1, 1, 1]; s.hdr = False; s.btype = 0; s.bremain = 0

    def _lens(s, b, lens, a, z):
        pre = build([b.read(4) for _ in range(20)])
        x = a
        while x < z:
            c = decode(b, pre)
            if c == 17:
                for _ in range(b.read(4) + 4): lens[x] = 0; x += 1
            elif c == 18:
                for _ in range(b.read(5) + 20): lens[x] = 0; x += 1
            elif c == 19:
                y = b.read(1) + 4; c = decode(b, pre); v = (lens[x] - c) % 17
                for _ in range(y): lens[x] = v; x += 1
            else:
                lens[x] = (lens[x] - c) % 17; x += 1

    def frame(s, data, out_len):
        b = Bits(data); out = bytearray()
        if not s.hdr:
            if b.read(1): b.read(32)
            s.hdr = True
        togo = out_len
        while togo > 0:
            if s.bremain == 0:
                s.btype = b.read(3)
                s.bremain = (b.read(16) << 8) | b.read(8)
                if s.btype == 2:
                    s.alig = build([b.read(3) for _ in range(8)])
                if s.btype in (1, 2):
                    s._lens(b, s.mainl, 0, 256); s._lens(b, s.mainl, 256, s.main_n)
                    s.maint = build(s.mainl); s._lens(b, s.lenl, 0, 249); s.lent = build(s.lenl)
                elif s.btype == 3:
                    if b.n == 0: pass
                    # realign: drop buffered bits; position is byte-aligned on 16-bit words
                    b.p -= (b.n // 16) * 2; b.n = 0; b.buf = 0
                    s.R = list(struct.unpack_from('<3I', data, b.p)); b.p += 12
                else:
                    raise ValueError(f"bad block type {s.btype}")
            n = min(s.bremain, togo); s.bremain -= n; togo -= n
            if s.btype == 3:
                chunk = data[b.p:b.p+n]; b.p += n
                for c in chunk: s._put(c, out)
                if s.bremain == 0 and (b.p & 1): b.p += 1
                continue
            while n > 0:
                m = decode(b, s.maint)
                if m < 256: s._put(m, out); n -= 1; continue
                m -= 256; ml = m & 7
                if ml == 7: ml += decode(b, s.lent)
                ml += 2; mo = m >> 3
                if mo > 2:
                    ex = EXTRA[mo]
                    if s.btype == 2:
                        mo = POSBASE[mo] - 2
                        if ex > 3: mo += (b.read(ex - 3) << 3) + decode(b, s.alig)
                        elif ex == 3: mo += decode(b, s.alig)
                        elif ex > 0: mo += b.read(ex)
                        else: mo = 1
                    else:
                        mo = POSBASE[mo] - 2 + b.read(ex) if mo != 3 else 1
                    s.R = [mo, s.R[0], s.R[1]]
                elif mo == 0: mo = s.R[0]
                elif mo == 1: mo = s.R[1]; s.R[1] = s.R[0]; s.R[0] = mo
                else: mo = s.R[2]; s.R[2] = s.R[0]; s.R[0] = mo
                for _ in range(ml):
                    s._put(s.win[(s.pos - mo) % s.wsize], out)
                n -= ml
                # match may overrun the requested count across block boundary; libmspack allows this
        return bytes(out)

    def _put(s, c, out):
        s.win[s.pos] = c; s.pos = (s.pos + 1) % s.wsize; out.append(c)

def read_xnb(path, limit=None):
    d = open(path, 'rb').read()
    assert d[:3] == b'XNB', path
    flags = d[5]; size = struct.unpack_from('<I', d, 6)[0]
    if not flags & 0x80: return d[10:size]
    dsize = struct.unpack_from('<I', d, 10)[0]
    p = 14; end = size; z = Lzx(); out = bytearray()
    while p < end and len(out) < dsize:
        hi, lo = d[p], d[p+1]; p += 2; fs = 0x8000
        if hi == 0xFF:
            fs = (lo << 8) | d[p]; hi, lo = d[p+1], d[p+2]; p += 3
        bs = (hi << 8) | lo
        if bs == 0 or fs == 0: break
        out += z.frame(d[p:p+bs], fs); p += bs
        if limit and len(out) >= limit: break
    return bytes(out)

def r7(d, i):
    v = sh = 0
    while True:
        c = d[i]; i += 1; v |= (c & 0x7F) << sh; sh += 7
        if c < 0x80: return v, i

def readers(body):
    n, i = r7(body, 0); res = []
    for _ in range(n):
        l, i = r7(body, i); res.append(body[i:i+l].decode('utf-8', 'replace')); i += l + 4
    return res, i

if __name__ == '__main__':
    body = read_xnb(sys.argv[1])
    print(len(body)); print(readers(body)[0])
