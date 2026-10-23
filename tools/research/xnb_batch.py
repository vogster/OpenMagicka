"""Parse every .xnb under a Content dir and report files not consumed byte-exactly.

Usage: python xnb_batch.py <Magicka/Content>
"""
import sys, os, json, collections, traceback
from multiprocessing import Pool
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xnb_parse as parse
ROOT = sys.argv[1] if len(sys.argv) > 1 else None

def work(rel):
    p = os.path.join(ROOT, rel)
    try:
        r, root, shared, left = parse.parse_file(p)
        return (rel, r.readers[0] if r.readers else '?', 'ok' if left == 0 else 'left%d' % left, dict(parse.STATS), {k: v[:3] for k, v in parse.TEXFMT.items()})
    except Exception as e:
        return (rel, '?', 'ERR %s: %s' % (type(e).__name__, e), {}, {})

if __name__ == '__main__':
    if not ROOT:
        sys.exit(__doc__)
    files = [os.path.relpath(os.path.join(r, f), ROOT) for r, _, fs in os.walk(ROOT) for f in fs if f.endswith('.xnb')]
    res = collections.Counter(); errs = []; stats = collections.Counter(); tf = collections.defaultdict(set)
    with Pool(12) as pool:
        for rel, rd, st, s, t in pool.imap_unordered(work, files, chunksize=4):
            res[(rd, st if st == 'ok' else 'bad')] += 1
            if st != 'ok': errs.append((rel, st))
            stats.update(s)
            for k, v in t.items():
                for x in v: tf[k].add((x[0], x[1], x[2], x[3]))
    for k, v in sorted(res.items()): print(k, v)
    for e in errs[:40]: print(*e)
    print(sorted(stats.items()))
    for k, v in sorted(tf.items()): print('fmt', k, sorted(v)[:4])
