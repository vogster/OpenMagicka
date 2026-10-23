"""Extract DX9 effect bytecode (fx_2_0) from Effect .xnb files.

Usage: python extract_effects.py <Magicka/Content/Shaders> <out dir>
Writes <name>.fxb for every Effect .xnb found (recursively). The output is game data:
keep it outside the repository.
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xnb_parse  # noqa: E402


def extract(path):
    r = xnb_parse.R(xnb_parse.xnb.read_xnb(path), path)
    if not r.readers or r.readers[0] != 'EffectReader':
        return None
    if r.u7() != 1:  # object reference to the first type reader
        raise ValueError(f'{path}: unexpected root object')
    size = r.u32()
    code = r.take(size)
    if code[:4] != b'\x01\x09\xff\xfe':
        raise ValueError(f'{path}: not fx_2_0 bytecode')
    return code


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    src, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    count = 0
    for root, _, files in os.walk(src):
        for f in sorted(files):
            if not f.lower().endswith('.xnb'):
                continue
            code = extract(os.path.join(root, f))
            if code is None:
                continue
            rel = os.path.relpath(os.path.join(root, f), src)[:-4].replace(os.sep, '_')
            with open(os.path.join(out, rel + '.fxb'), 'wb') as fh:
                fh.write(code)
            count += 1
            print(f'{rel}: {len(code)} bytes')
    print(f'{count} effects extracted')


if __name__ == '__main__':
    main()
