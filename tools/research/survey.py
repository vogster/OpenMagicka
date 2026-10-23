import os, sys, collections, struct, re
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xnb import read_xnb, readers
root = sys.argv[1]
prim = collections.Counter(); byreader = collections.Counter(); bydir = collections.defaultdict(collections.Counter); errs = []
for r, _, fs in os.walk(root):
    for f in fs:
        if not f.endswith('.xnb'): continue
        p = os.path.join(r, f)
        try:
            body = read_xnb(p, limit=1)
            rs, _ = readers(body)
            short = [re.sub(r'`.*', '', x.split(',')[0]).split('.')[-1] for x in rs]
            prim[short[0]] += 1
            top = os.path.relpath(p, root).split(os.sep)[0]
            bydir[top][short[0]] += 1
            for x in rs: byreader[re.sub(r', Version=[^\]]*', '', x)] += 1
        except Exception as e: errs.append((p, repr(e)))
print("== primary reader per file"); [print(v, k) for k, v in prim.most_common()]
print("== by dir"); [print(k, dict(v)) for k, v in sorted(bydir.items())]
print("== all readers"); [print(v, k) for k, v in byreader.most_common()]
print("errors", len(errs)); [print(e) for e in errs[:10]]
