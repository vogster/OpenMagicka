"""Research parser for Magicka XNB v4 content. Verifies byte-exact consumption."""
import struct, sys, os, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xnb

STATS = collections.Counter()


class R:
    def __init__(s, body, path=''):
        s.d = body; s.p = 0; s.path = path
        n = s.u7(); s.readers = []
        for _ in range(n):
            name = s.str_(); ver = s.i32()
            s.readers.append(short(name))
        s.nshared = s.u7()

    # primitives
    def u7(s):
        v = sh = 0
        while True:
            c = s.d[s.p]; s.p += 1; v |= (c & 0x7F) << sh; sh += 7
            if c < 0x80: return v
    def take(s, n):
        if s.p + n > len(s.d): raise EOFError('read past end at %d (+%d)' % (s.p, n))
        b = s.d[s.p:s.p+n]; s.p += n; return b
    def u8(s): return s.take(1)[0]
    def bool_(s):
        b = s.u8()
        if b > 1: raise ValueError('bad bool %d at %d' % (b, s.p - 1))
        return b == 1
    def u16(s): return struct.unpack('<H', s.take(2))[0]
    def i32(s): return struct.unpack('<i', s.take(4))[0]
    def u32(s): return struct.unpack('<I', s.take(4))[0]
    def f32(s): return struct.unpack('<f', s.take(4))[0]
    def v2(s): return struct.unpack('<2f', s.take(8))
    def v3(s): return struct.unpack('<3f', s.take(12))
    def v4(s): return struct.unpack('<4f', s.take(16))
    quat = v4
    def mat(s): return struct.unpack('<16f', s.take(64))
    def str_(s):
        n = s.u7(); return s.take(n).decode('utf-8')
    def char(s):
        c = s.d[s.p]
        n = 1 if c < 0x80 else 2 if c < 0xE0 else 3 if c < 0xF0 else 4
        return s.take(n).decode('utf-8')
    def ext(s):
        return s.str_()  # ExternalReference: asset name, '' = null

    # object model
    def obj(s, expect=None):
        t = s.u7()
        if t == 0: return None
        name = s.readers[t - 1]
        if expect and not any(e in name for e in expect):
            raise ValueError('expected %s got %s at %d' % (expect, name, s.p))
        STATS[name] += 1
        return READERS[name](s)
    def shared_ref(s):
        return s.u7()  # 0 = null, else 1-based index into shared resources


def short(name):
    import re
    if '`' not in name: return name.split(',')[0].split('.')[-1]
    base = name.split('`')[0].split('.')[-1]
    args = re.findall(r'\[([A-Za-z0-9_.]+),', name)
    return base + '<' + ','.join(a.split('.')[-1] for a in args) + '>'


# ---------------- XNA 3.1 standard readers ----------------
def tex2d(r):
    fmt = r.i32(); w = r.u32(); h = r.u32(); mips = r.u32()
    sizes = []
    for _ in range(mips):
        n = r.u32(); r.take(n); sizes.append(n)
    TEXFMT[fmt].append((w, h, mips, sizes[0]))
    return ('Texture2D', fmt, w, h, mips)

def tex3d(r):
    fmt = r.i32(); w = r.u32(); h = r.u32(); d = r.u32(); mips = r.u32()
    for _ in range(mips):
        n = r.u32(); r.take(n)
    return ('Texture3D', fmt, w, h, d, mips)

def texcube(r):
    fmt = r.i32(); size = r.u32(); mips = r.u32()
    for _ in range(6):
        for _ in range(mips):
            n = r.u32(); r.take(n)
    return ('TextureCube', fmt, size, mips)

def vdecl(r):
    n = r.u32(); els = []
    for _ in range(n):
        stream = r.u16(); off = r.u16(); fmt = r.u8(); meth = r.u8(); usage = r.u8(); ui = r.u8()
        els.append((stream, off, fmt, meth, usage, ui))
    return els

def vbuf(r):
    n = r.u32(); r.take(n); return ('VB', n)

def ibuf(r):
    is16 = r.bool_(); n = r.u32(); r.take(n); return ('IB', is16, n)

def effect(r):
    n = r.u32(); code = r.take(n); return ('Effect', n, code[:4])

def basic_effect(r):
    tex = r.ext(); diff = r.v3(); emis = r.v3(); spec = r.v3(); sp = r.f32(); a = r.f32(); vc = r.bool_()
    return ('BasicEffect', tex)

def effect_material(r):
    e = r.ext(); params = r.obj(['DictionaryReader'])
    return ('EffectMaterial', e, params)

def dictionary_str_obj(r):
    n = r.u32(); d = {}
    for _ in range(n):
        k = r.obj(['StringReader']); v = r.obj(); d[k] = v
    return d

def list_vec3(r):
    n = r.u32(); return [r.v3() for _ in range(n)]

def list_str(r):
    n = r.u32(); return [r.obj(['StringReader']) for _ in range(n)]

def bone_ref(r, nb):
    return r.u8() if nb < 255 else r.u32()

def model(r):
    nb = r.u32(); bones = []
    for _ in range(nb):
        name = r.obj(['StringReader']); m = r.mat(); bones.append(name)
    for _ in range(nb):
        parent = bone_ref(r, nb); nc = r.u32()
        for _ in range(nc): bone_ref(r, nb)
    nvd = r.u32()
    decls = [r.obj(['VertexDeclarationReader']) for _ in range(nvd)]
    nm = r.u32(); meshes = []
    for _ in range(nm):
        name = r.obj(['StringReader']); pb = bone_ref(r, nb); bs = r.v4()
        vb = r.obj(['VertexBufferReader']); ib = r.obj(['IndexBufferReader']); tag = r.obj()
        np = r.u32(); parts = []
        for _ in range(np):
            so = r.u32(); bv = r.u32(); nv = r.u32(); si = r.u32(); pc = r.u32()
            vdi = r.u32(); assert vdi < nvd; ptag = r.obj(); eff = r.shared_ref()
            parts.append((nv, pc, eff))
        meshes.append((name, parts))
    root = bone_ref(r, nb); tag = r.obj()
    return ('Model', bones, meshes)

def single(r): return r.f32()
def boolean(r): return r.bool_()
def matrix(r): return r.mat()
def vector3(r): return r.v3()

# ---------------- XNAnimation ----------------
def pose(r): r.v3(); r.quat(); r.v3()

def skinned_model(r):
    m = r.obj(['ModelReader'])
    nb = r.i32(); bones = [r.shared_ref() for _ in range(nb)]
    na = r.i32(); anims = [r.shared_ref() for _ in range(na)]
    return ('SkinnedModel', m, nb, na)

def skinned_bone(r):
    idx = r.u16(); name = r.str_(); pose(r); r.mat()
    parent = r.shared_ref(); nc = r.i32()
    for _ in range(nc): r.shared_ref()
    return ('Bone', idx, name)

def anim_channel(r):
    n = r.i32()
    for _ in range(n): r.f32(); pose(r)
    return n

def anim_clip(r):
    name = r.str_(); dur = r.f32(); n = r.i32()
    for _ in range(n): r.str_(); anim_channel(r)
    return ('Clip', name, dur, n)

def skinned_basic_effect(r):
    tech = r.u8(); r.f32(); r.v3(); r.f32(); r.f32(); r.f32()
    for _ in range(7): r.bool_()
    texs = [r.obj(['Texture2DReader', 'ExternalReferenceReader']) for _ in range(6)]
    return ('SkinnedBasicEffect', tech, texs)

# ---------------- PolygonHead ----------------
def render_deferred(r):
    r.f32(); r.f32(); r.bool_(); r.bool_(); refl = r.ext()
    def layer():
        r.bool_(); r.bool_(); r.v3(); r.f32(); r.f32(); r.f32(); r.f32(); r.f32()
        return (r.ext(), r.ext(), r.ext())
    l0 = layer(); l1 = layer() if r.bool_() else None
    return ('RenderDeferred', refl, l0, l1)

def render_deferred_liquid(r):
    refl = r.ext(); r.f32(); r.v2(); r.v2(); r.f32(); r.v3(); r.v3(); r.f32(); r.f32(); r.f32()
    t = [r.ext(), r.ext()]; r.f32(); r.v3(); r.f32(); r.f32(); r.f32(); t += [r.ext(), r.ext()]
    return ('Liquid', refl, t)

def additive(r):
    r.v3(); r.bool_(); r.bool_(); return ('Additive', r.ext())

def lava(r):
    r.f32(); r.v2(); r.v2(); r.f32(); r.f32(); r.f32(); r.f32(); r.f32()
    t = [r.ext(), r.ext(), r.ext()]; r.v3(); r.f32(); r.f32(); r.f32(); r.f32(); t += [r.ext(), r.ext()]
    return ('Lava', t)

def skinned_deferred_nm(r):
    r.v3(); r.f32(); r.f32(); r.f32(); r.f32()
    return ('SkinnedDeferredNM', [r.ext() for _ in range(5)])

def bitree_node(r):
    r.i32(); r.i32(); r.v3(); r.v3()
    n = 1
    if r.bool_(): n += bitree_node(r)
    if r.bool_(): n += bitree_node(r)
    return n

def bitree_model(r):
    n = r.i32(); roots = []
    for _ in range(n):
        vis = r.bool_(); cs = r.bool_(); sway = r.f32(); ei = r.f32(); gl = r.f32()
        nv = r.i32(); stride = r.i32()
        vd = r.obj(['VertexDeclarationReader']); vb = r.obj(['VertexBufferReader'])
        ib = r.obj(['IndexBufferReader']); eff = r.obj()
        r.i32(); r.i32(); r.v3(); r.v3()
        nodes = 1
        if r.bool_(): nodes += bitree_node(r)
        if r.bool_(): nodes += bitree_node(r)
        roots.append((nv, stride, vb, eff[0] if eff else None, nodes))
    return ('BiTreeModel', roots)

def bitmap_font(r):
    lh = r.i32(); bl = r.i32(); dc = r.char()
    tex = r.obj(['Texture2DReader', 'ExternalReferenceReader'])
    n = r.i32()
    for _ in range(n):
        r.char(); [r.i32() for _ in range(6)]; r.bool_()
    k = r.i32()
    for _ in range(k): r.char(); r.char(); r.i32()
    return ('BitmapFont', lh, bl, dc, n, k)

# ---------------- Magicka ----------------
def tri_list(r):
    n = r.i32()
    r.take(12 * n); return n

def locator(r): r.mat(); r.f32()

def vfx_storage(r):
    r.str_(); r.v3(); r.v3(); r.f32(); r.str_()

def liquid(r):
    eff = r.obj(['RenderDeferredLiquidEffectReader', 'LavaEffectReader'])
    vb = r.obj(['VertexBufferReader']); ib = r.obj(['IndexBufferReader']); vd = r.obj(['VertexDeclarationReader'])
    stride = r.i32(); nv = r.i32(); pc = r.i32()
    coll = r.bool_(); frz = r.bool_(); auto = r.bool_()
    return (eff[0], coll, frz, auto)

def navmesh(r):
    nv = r.u16(); r.take(12 * nv); nt = r.u16(); r.take(nt * (12 + 12 + 1)); return (nv, nt)

def animated_part(r, depth=0):
    name = r.str_(); r.bool_(); m = r.obj(['ModelReader'])
    n = r.i32()
    for _ in range(n): r.str_(); r.bool_(); r.bool_()
    liqs = [liquid(r) for _ in range(r.i32())]
    for _ in range(r.i32()): r.str_(); locator(r)
    r.f32(); anim_channel(r)
    for _ in range(r.i32()): vfx_storage(r)
    for _ in range(r.i32()): r.str_(); r.mat()
    if r.bool_():
        r.u8(); r.obj(['ListReader<Vector3']); tri_list(r)
    nav = navmesh(r) if r.bool_() else None
    kids = [animated_part(r, depth + 1) for _ in range(r.i32())]
    return (name, liqs, nav, kids)

def level_model(r):
    bt = r.obj(['BiTreeModelReader'])
    parts = [animated_part(r) for _ in range(r.i32())]
    nl = r.i32(); lights = []
    for _ in range(nl):
        name = r.str_(); r.v3(); r.v3(); lt = r.i32(); var = r.i32(); r.f32(); r.bool_(); r.f32(); r.f32()
        r.v3(); r.v3(); r.f32(); r.f32(); r.f32(); r.i32(); r.bool_()
        lights.append((name, lt, var))
    for _ in range(r.i32()): vfx_storage(r)
    phys = []
    for _ in range(r.i32()): r.mat(); phys.append(r.str_())
    liqs = [liquid(r) for _ in range(r.i32())]
    nff = r.i32()
    for _ in range(nff):
        r.v3(); [r.f32() for _ in range(6)]; r.bool_(); r.ext(); r.f32()
        r.obj(['VertexBufferReader']); r.obj(['IndexBufferReader']); r.obj(['VertexDeclarationReader'])
        r.i32(); r.i32(); r.i32()
    coll = []
    for _ in range(10):
        if r.bool_():
            v = r.obj(['ListReader<Vector3']); coll.append((len(v), tri_list(r)))
        else: coll.append(None)
    cam = None
    if r.bool_():
        v = r.obj(['ListReader<Vector3']); cam = (len(v), tri_list(r))
    ntr = r.i32()
    for _ in range(ntr): r.str_(); r.v3(); r.v3(); r.quat()
    nloc = r.i32()
    for _ in range(nloc): r.str_(); locator(r)
    nav = navmesh(r)
    return dict(bitree=bt, parts=parts, lights=lights, phys=phys, liquids=liqs, forcefields=nff,
                coll=coll, cam=cam, triggers=ntr, locators=nloc, nav=nav)

# --- shared entity sub-structures
def damage(r, int_amount=False):
    r.i32(); r.i32(); (r.i32() if int_amount else r.f32()); r.f32()

def resistances(r, with_status=True):
    for _ in range(r.i32()):
        r.i32(); r.f32(); r.f32()
        if with_status: r.bool_()

def point_light(r):
    r.f32(); r.v3(); r.v3(); r.f32(); r.u8(); r.f32(); r.f32()

def special_ability(r):
    name = r.str_(); r.str_(); r.str_()
    for _ in range(r.i32()): r.i32()
    return name

EVENTS = {
    0: lambda r: (damage(r), r.bool_()),
    1: lambda r: (damage(r, True), r.f32()),
    2: lambda r: (r.i32(), r.str_(), r.f32(), r.bool_()),
    3: lambda r: (r.bool_(), r.bool_(), r.str_()),
    4: lambda r: r.i32(),
    5: lambda r: (r.f32(), r.f32(), r.bool_()),
    6: lambda r: (r.i32(), r.i32(), r.i32()),
    8: lambda r: (r.str_(), r.str_(), r.str_(), r.f32(), r.u8(), r.u8(), r.u8(), r.f32(), r.v3()),
    9: lambda r: None,
    10: lambda r: (r.i32(), r.i32()),
    11: lambda r: r.str_(),
    12: lambda r: r.str_(),
    13: lambda r: (r.str_(), r.v3(), r.bool_()),
    14: lambda r: point_light(r),
    15: lambda r: (r.str_(), [r.i32() for _ in range(r.i32())]),
    16: lambda r: (damage(r), r.bool_()),
}

def condition_collection(r):
    n = r.i32()
    if n > 5: raise ValueError('ConditionCollection > 5: %d' % n)
    for _ in range(n):
        r.u8(); r.i32(); r.i32(); r.f32(); r.f32()  # EventCondition
        r.bool_()  # repeat
        for _ in range(r.i32()):
            t = r.u8(); STATS['event%d' % t] += 1
            EVENTS[t](r)
    return n

def anim_action(r):
    name = r.str_().lower(); STATS['aa:' + name] += 1
    r.f32(); r.f32()
    A = {
        'block': lambda: r.i32(), 'breakfree': lambda: (r.f32(), r.i32()),
        'camerashake': lambda: (r.str_(), r.f32(), r.f32()),
        'castspell': lambda: (r.str_() if not r.bool_() else None),
        'crouch': lambda: (r.f32(), r.f32()),
        'damagegrip': lambda: (r.bool_(), [damage(r) for _ in range(r.i32())]),
        'dealdamage': lambda: (r.i32(), r.u8()), 'detachitem': lambda: (r.i32(), r.v3()),
        'ethereal': lambda: (r.bool_(), r.f32(), r.f32()), 'footstep': lambda: None,
        'grip': lambda: (r.u8(), r.f32(), r.f32(), r.str_(), r.str_(), r.bool_()),
        'gunfire': lambda: (r.i32(), r.f32()), 'immortal': lambda: r.bool_(),
        'invisible': lambda: r.bool_(),
        'jump': lambda: (r.f32(), r.f32() if r.bool_() else None, r.f32() if r.bool_() else None),
        'move': lambda: r.v3(), 'overkillgrip': lambda: None,
        'playeffect': lambda: (r.str_(), r.bool_(), r.str_(), r.f32()),
        'playsound': lambda: (r.str_(), r.i32()), 'releasegrip': lambda: None,
        'removestatus': lambda: r.str_(), 'setitemattach': lambda: (r.i32(), r.str_()),
        'spawnmissile': lambda: (r.i32(), r.v3(), r.bool_()),
        'specialability': lambda: (special_ability(r) if r.i32() < 0 else None),
        'suicide': lambda: r.bool_(), 'throwgrip': lambda: None, 'tongue': lambda: r.f32(),
        'weaponvisibility': lambda: (r.i32(), r.bool_()),
    }
    A[name]()

def anim_clip_action(r):
    r.str_(); r.f32(); r.f32(); r.bool_()
    for _ in range(r.i32()): anim_action(r)

def anim_sets(r):
    total = 0
    for _ in range(27):
        n = r.i32()
        for _ in range(n): r.str_(); anim_clip_action(r); total += 1
    return total

def ability(r):
    name = r.str_().lower(); STATS['ab:' + name] += 1
    r.f32(); r.u8()
    if r.bool_(): r.str_()
    for _ in range(r.i32()): r.str_()
    f = r.f32
    B = {
        'block': lambda: (f(), r.i32()),
        'castspell': lambda: (f(), f(), f(), f(), f(), r.i32(), [r.i32() for _ in range(r.i32())]),
        'confusegrip': lambda: None, 'damagegrip': lambda: None, 'removestatus': lambda: None,
        'dash': lambda: (f(), f(), f(), r.v3()), 'elementsteal': lambda: (f(), f()),
        'gripcharacterfrombehind': lambda: (f(), f(), f(), f()),
        'jump': lambda: (f(), f(), f(), f()),
        'melee': lambda: (f(), f(), f(), [r.i32() for _ in range(r.i32())], r.bool_()),
        'pickupcharacter': lambda: (f(), f(), f(), f(), r.str_()),
        'ranged': lambda: (f(), f(), f(), f(), f(), [r.i32() for _ in range(r.i32())]),
        'specialabilityability': lambda: (f(), f(), f(), r.i32()),
        'throwgrip': lambda: (f(), f(), f(), [damage(r, True) for _ in range(r.i32())]),
        'zombiegrip': lambda: (f(), f(), f(), f(), r.str_()),
    }
    B[name]()

BUFFS = {0: lambda r: damage(r), 1: lambda r: damage(r), 2: lambda r: (r.i32(), r.f32(), r.f32(), r.bool_()),
         3: lambda r: None, 4: lambda r: r.f32(), 5: lambda r: r.f32(), 6: lambda r: (r.f32(), r.f32()),
         7: lambda r: (r.f32(), r.f32()), 8: lambda r: (r.f32(), r.f32())}

def buff(r):
    t = r.u8(); r.u8(); r.v3(); r.f32(); r.str_(); BUFFS[t](r); STATS['buff%d' % t] += 1

def aura(r):
    r.u8(); t = r.u8(); r.u8(); r.v3(); r.str_(); r.f32(); r.f32(); r.str_(); r.i32()
    STATS['aura%d' % t] += 1
    if t == 0: buff(r)
    elif t in (1, 2, 3): r.f32()
    elif t == 4: r.f32(); r.f32()

def gibs(r):
    for _ in range(r.i32()): r.ext(); r.f32(); r.f32()

def model_props(r):
    n = r.i32()
    for _ in range(n): r.ext(); r.f32(); r.v3()
    return n

def attached_effects(r):
    for _ in range(r.i32()): r.str_(); r.str_()

def character_template(r):
    cid = r.str_(); r.str_(); r.i32(); r.i32()
    for _ in range(7): r.bool_()
    for _ in range(r.i32()): r.str_(); r.i32()  # sounds (only first 4 kept, but all read)
    gibs(r)
    for _ in range(r.i32()): r.str_(); point_light(r)
    r.f32(); r.i32(); r.bool_(); r.f32(); r.f32(); r.i32(); r.f32(); r.i32(); r.i32(); r.bool_(); r.bool_()
    r.i32(); [r.f32() for _ in range(9)]
    r.i32(); r.str_()
    resistances(r)
    model_props(r); r.ext()
    attached_effects(r)
    na = anim_sets(r)
    for _ in range(r.i32()): r.i32(); r.str_(); r.v3(); r.ext()  # equipment
    condition_collection(r)
    [r.f32() for _ in range(13)]; r.bool_(); r.f32()
    for _ in range(r.i32()): ability(r)
    for _ in range(r.i32()):
        r.u8()
        for _ in range(r.i32()): r.str_()
    for _ in range(r.i32()): buff(r)
    for _ in range(r.i32()): aura(r)
    return ('CharacterTemplate', cid, na)

def item(r):
    name = r.str_(); r.str_(); r.str_()
    for _ in range(r.i32()): r.str_(); r.i32()
    r.bool_(); r.bool_(); r.i32(); r.u8(); r.f32(); r.bool_(); r.bool_(); r.bool_()
    resistances(r)
    r.u8(); r.f32()
    for _ in range(r.i32()): r.str_()
    for _ in range(r.i32()): point_light(r)
    if r.bool_(): r.f32(); special_ability(r)
    r.f32(); r.bool_(); condition_collection(r)
    r.f32(); r.bool_(); r.f32(); r.f32(); r.f32(); r.f32(); r.i32(); r.i32(); r.f32()
    r.str_(); r.str_(); r.str_(); r.f32(); r.str_(); r.str_()
    condition_collection(r); r.ext(); condition_collection(r); r.f32(); mdl = r.ext()
    for _ in range(r.i32()): aura(r)
    return ('Item', name, mdl)

def phys_template(r):
    r.bool_(); r.bool_(); r.bool_(); r.f32(); r.i32(); r.bool_()
    resistances(r, with_status=False)
    gibs(r)
    r.str_(); r.str_(); r.str_()
    m = r.obj(['ModelReader'])
    if r.bool_():
        r.obj(['ListReader<Vector3']); tri_list(r)
    nb = r.i32()
    for _ in range(nb): r.str_(); r.v3(); r.v3(); r.quat()
    for _ in range(r.i32()): r.str_(); r.mat()
    nl = r.i32()
    if nl: raise ValueError('lights not implemented')
    condition_collection(r)
    pid = r.str_()
    ext = False
    if r.p < len(r.d):
        ext = r.bool_()
    if ext:
        r.f32(); model_props(r)
        if r.bool_(): r.ext()
        attached_effects(r)
        anim_sets(r)
    return ('PhysicsEntityTemplate', pid, nb, ext)


READERS = {
    'Texture2DReader': tex2d, 'Texture3DReader': tex3d, 'TextureCubeReader': texcube,
    'VertexDeclarationReader': vdecl, 'VertexBufferReader': vbuf, 'IndexBufferReader': ibuf,
    'EffectReader': effect, 'BasicEffectReader': basic_effect, 'EffectMaterialReader': effect_material,
    'StringReader': lambda r: r.str_(), 'ExternalReferenceReader': lambda r: ('ExtRef', r.ext()), 'ModelReader': model, 'SingleReader': single,
    'BooleanReader': boolean, 'MatrixReader': matrix, 'Vector3Reader': vector3,
    'ListReader<Vector3>': list_vec3, 'ListReader<String>': list_str,
    'DictionaryReader<String,Object>': dictionary_str_obj,
    'SkinnedModelReader': skinned_model, 'SkinnedModelBoneReader': skinned_bone,
    'AnimationClipReader': anim_clip, 'SkinnedModelBasicEffectReader': skinned_basic_effect,
    'RenderDeferredEffectReader': render_deferred, 'RenderDeferredLiquidEffectReader': render_deferred_liquid,
    'AdditiveEffectReader': additive, 'LavaEffectReader': lava,
    'SkinnedModelDeferredNormalMappedEffectReader': skinned_deferred_nm,
    'BiTreeModelReader': bitree_model, 'BitmapFontReader': bitmap_font,
    'LevelModelReader': level_model, 'CharacterTemplateReader': character_template,
    'ItemReader': item, 'PhysicsEntityTemplateReader': phys_template,
}
TEXFMT = collections.defaultdict(list)


def parse_file(path):
    body = xnb.read_xnb(path)
    r = R(body, path)
    root = r.obj()
    shared = [r.obj() for _ in range(r.nshared)]
    return r, root, shared, len(body) - r.p


if __name__ == '__main__':
    for p in sys.argv[1:]:
        r, root, shared, left = parse_file(p)
        print(p, r.readers, 'shared', r.nshared, 'left', left)
        print(' ', str(root)[:300])
