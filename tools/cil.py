"""Minimal ECMA-335 metadata + IL reader (no dependencies besides pefile).

Reads the tables needed to survey which UnityEngine API a set of managed
assemblies uses: TypeRef/TypeDef/MethodDef/MemberRef/MethodSpec/TypeSpec/
ModuleRef/ImplMap/AssemblyRef/InterfaceImpl/NestedClass, plus method bodies.
"""
import struct
import pefile

T = dict(Module=0, TypeRef=1, TypeDef=2, FieldPtr=3, Field=4, MethodPtr=5, MethodDef=6,
         ParamPtr=7, Param=8, InterfaceImpl=9, MemberRef=10, Constant=11, CustomAttribute=12,
         FieldMarshal=13, DeclSecurity=14, ClassLayout=15, FieldLayout=16, StandAloneSig=17,
         EventMap=18, EventPtr=19, Event=20, PropertyMap=21, PropertyPtr=22, Property=23,
         MethodSemantics=24, MethodImpl=25, ModuleRef=26, TypeSpec=27, ImplMap=28, FieldRVA=29,
         ENCLog=30, ENCMap=31, Assembly=32, AssemblyProcessor=33, AssemblyOS=34, AssemblyRef=35,
         AssemblyRefProcessor=36, AssemblyRefOS=37, File=38, ExportedType=39,
         ManifestResource=40, NestedClass=41, GenericParam=42, MethodSpec=43,
         GenericParamConstraint=44)

CODED = {
    "TypeDefOrRef": (2, ["TypeDef", "TypeRef", "TypeSpec"]),
    "HasConstant": (2, ["Field", "Param", "Property"]),
    "HasCustomAttribute": (5, ["MethodDef", "Field", "TypeRef", "TypeDef", "Param", "InterfaceImpl",
                               "MemberRef", "Module", "DeclSecurity", "Property", "Event",
                               "StandAloneSig", "ModuleRef", "TypeSpec", "Assembly", "AssemblyRef",
                               "File", "ExportedType", "ManifestResource", "GenericParam",
                               "GenericParamConstraint", "MethodSpec"]),
    "HasFieldMarshal": (1, ["Field", "Param"]),
    "HasDeclSecurity": (2, ["TypeDef", "MethodDef", "Assembly"]),
    "MemberRefParent": (3, ["TypeDef", "TypeRef", "ModuleRef", "MethodDef", "TypeSpec"]),
    "HasSemantics": (1, ["Event", "Property"]),
    "MethodDefOrRef": (1, ["MethodDef", "MemberRef"]),
    "MemberForwarded": (1, ["Field", "MethodDef"]),
    "Implementation": (2, ["File", "AssemblyRef", "ExportedType"]),
    "CustomAttributeType": (3, [None, None, "MethodDef", "MemberRef", None]),
    "ResolutionScope": (2, ["Module", "ModuleRef", "AssemblyRef", "TypeRef"]),
    "TypeOrMethodDef": (1, ["TypeDef", "MethodDef"]),
}

S, G, B = "s", "g", "b"
SCHEMA = {
    0: [("Generation", 2), ("Name", S), ("Mvid", G), ("EncId", G), ("EncBaseId", G)],
    1: [("Scope", "c:ResolutionScope"), ("Name", S), ("Namespace", S)],
    2: [("Flags", 4), ("Name", S), ("Namespace", S), ("Extends", "c:TypeDefOrRef"),
        ("FieldList", "t:Field"), ("MethodList", "t:MethodDef")],
    3: [("Field", "t:Field")],
    4: [("Flags", 2), ("Name", S), ("Signature", B)],
    5: [("Method", "t:MethodDef")],
    6: [("RVA", 4), ("ImplFlags", 2), ("Flags", 2), ("Name", S), ("Signature", B),
        ("ParamList", "t:Param")],
    7: [("Param", "t:Param")],
    8: [("Flags", 2), ("Sequence", 2), ("Name", S)],
    9: [("Class", "t:TypeDef"), ("Interface", "c:TypeDefOrRef")],
    10: [("Class", "c:MemberRefParent"), ("Name", S), ("Signature", B)],
    11: [("Type", 2), ("Parent", "c:HasConstant"), ("Value", B)],
    12: [("Parent", "c:HasCustomAttribute"), ("Type", "c:CustomAttributeType"), ("Value", B)],
    13: [("Parent", "c:HasFieldMarshal"), ("NativeType", B)],
    14: [("Action", 2), ("Parent", "c:HasDeclSecurity"), ("PermissionSet", B)],
    15: [("PackingSize", 2), ("ClassSize", 4), ("Parent", "t:TypeDef")],
    16: [("Offset", 4), ("Field", "t:Field")],
    17: [("Signature", B)],
    18: [("Parent", "t:TypeDef"), ("EventList", "t:Event")],
    19: [("Event", "t:Event")],
    20: [("Flags", 2), ("Name", S), ("EventType", "c:TypeDefOrRef")],
    21: [("Parent", "t:TypeDef"), ("PropertyList", "t:Property")],
    22: [("Property", "t:Property")],
    23: [("Flags", 2), ("Name", S), ("Type", B)],
    24: [("Semantics", 2), ("Method", "t:MethodDef"), ("Association", "c:HasSemantics")],
    25: [("Class", "t:TypeDef"), ("Body", "c:MethodDefOrRef"), ("Decl", "c:MethodDefOrRef")],
    26: [("Name", S)],
    27: [("Signature", B)],
    28: [("Flags", 2), ("Member", "c:MemberForwarded"), ("ImportName", S),
         ("ImportScope", "t:ModuleRef")],
    29: [("RVA", 4), ("Field", "t:Field")],
    30: [("Token", 4), ("FuncCode", 4)],
    31: [("Token", 4)],
    32: [("HashAlgId", 4), ("Major", 2), ("Minor", 2), ("Build", 2), ("Revision", 2),
         ("Flags", 4), ("PublicKey", B), ("Name", S), ("Culture", S)],
    33: [("Processor", 4)],
    34: [("Platform", 4), ("Major", 4), ("Minor", 4)],
    35: [("Major", 2), ("Minor", 2), ("Build", 2), ("Revision", 2), ("Flags", 4),
         ("PublicKey", B), ("Name", S), ("Culture", S), ("Hash", B)],
    36: [("Processor", 4), ("AssemblyRef", "t:AssemblyRef")],
    37: [("Platform", 4), ("Major", 4), ("Minor", 4), ("AssemblyRef", "t:AssemblyRef")],
    38: [("Flags", 4), ("Name", S), ("Hash", B)],
    39: [("Flags", 4), ("TypeDefId", 4), ("Name", S), ("Namespace", S),
         ("Implementation", "c:Implementation")],
    40: [("Offset", 4), ("Flags", 4), ("Name", S), ("Implementation", "c:Implementation")],
    41: [("Nested", "t:TypeDef"), ("Enclosing", "t:TypeDef")],
    42: [("Number", 2), ("Flags", 2), ("Owner", "c:TypeOrMethodDef"), ("Name", S)],
    43: [("Method", "c:MethodDefOrRef"), ("Instantiation", B)],
    44: [("Owner", "t:GenericParam"), ("Constraint", "c:TypeDefOrRef")],
}

# IL operand sizes: 0 none, 1 u8, 2 u16, 4 u32/token, 8 u64, -1 switch
OP1 = {}
for o in range(0x00, 0x0E): OP1[o] = 0
for o in range(0x0E, 0x14): OP1[o] = 1
for o in range(0x14, 0x1F): OP1[o] = 0
OP1.update({0x1F: 1, 0x20: 4, 0x21: 8, 0x22: 4, 0x23: 8, 0x25: 0, 0x26: 0, 0x27: 4, 0x28: 4,
            0x29: 4, 0x2A: 0})
for o in range(0x2B, 0x38): OP1[o] = 1
for o in range(0x38, 0x45): OP1[o] = 4
OP1[0x45] = -1
for o in range(0x46, 0x6F): OP1[o] = 0
for o in range(0x6F, 0x76): OP1[o] = 4
OP1[0x76] = 0
OP1.update({0x79: 4, 0x7A: 0})
for o in range(0x7B, 0x82): OP1[o] = 4
for o in range(0x82, 0x8C): OP1[o] = 0
OP1.update({0x8C: 4, 0x8D: 4, 0x8E: 0, 0x8F: 4})
for o in range(0x90, 0xA3): OP1[o] = 0
OP1.update({0xA3: 4, 0xA4: 4, 0xA5: 4})
for o in range(0xB3, 0xBB): OP1[o] = 0
OP1.update({0xC2: 4, 0xC3: 0, 0xC6: 4, 0xD0: 4})
for o in range(0xD1, 0xDD): OP1[o] = 0
OP1.update({0xDD: 4, 0xDE: 1, 0xDF: 0, 0xE0: 0})
OP2 = {0x00: 0, 0x01: 0, 0x02: 0, 0x03: 0, 0x04: 0, 0x05: 0, 0x06: 4, 0x07: 4, 0x09: 2,
       0x0A: 2, 0x0B: 2, 0x0C: 2, 0x0D: 2, 0x0E: 2, 0x0F: 0, 0x11: 0, 0x12: 1, 0x13: 0,
       0x14: 0, 0x15: 4, 0x16: 4, 0x17: 0, 0x18: 0, 0x19: 1, 0x1A: 0, 0x1C: 4, 0x1D: 0,
       0x1E: 0}
TOKEN_OPS1 = {0x27, 0x28, 0x29, 0x6F, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x79, 0x7B, 0x7C,
              0x7D, 0x7E, 0x7F, 0x80, 0x81, 0x8C, 0x8D, 0x8F, 0xA3, 0xA4, 0xA5, 0xC2, 0xC6,
              0xD0}
TOKEN_OPS2 = {0x06, 0x07, 0x15, 0x16, 0x1C}


def read_compressed(b, i):
    x = b[i]
    if x & 0x80 == 0:
        return x, i + 1
    if x & 0xC0 == 0x80:
        return ((x & 0x3F) << 8) | b[i + 1], i + 2
    return ((x & 0x1F) << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3], i + 4


class Assembly:
    def __init__(self, path):
        self.path = path
        self.pe = pefile.PE(data=open(path, "rb").read(), fast_load=True)
        self.data = bytes(self.pe.__data__)
        cli = self.off(self.pe.OPTIONAL_HEADER.DATA_DIRECTORY[14].VirtualAddress)
        md_rva, md_size = struct.unpack_from("<II", self.data, cli + 8)
        md = self.off(md_rva)
        assert self.data[md:md + 4] == b"BSJB"
        vlen = struct.unpack_from("<I", self.data, md + 12)[0]
        p = md + 16 + vlen
        nstreams = struct.unpack_from("<H", self.data, p + 2)[0]
        p += 4
        self.streams = {}
        for _ in range(nstreams):
            o, sz = struct.unpack_from("<II", self.data, p)
            p += 8
            e = self.data.index(b"\0", p)
            name = self.data[p:e].decode()
            p = (e + 4) & ~3
            self.streams[name] = (md + o, sz)
        self._parse_tables()

    def off(self, rva):
        return self.pe.get_offset_from_rva(rva)

    def _parse_tables(self):
        d = self.data
        base, _ = self.streams.get("#~") or self.streams["#-"]
        heap = d[base + 6]
        valid = struct.unpack_from("<Q", d, base + 8)[0]
        p = base + 24
        self.rows = {}
        for t in range(64):
            if valid >> t & 1:
                self.rows[t] = struct.unpack_from("<I", d, p)[0]
                p += 4
        if heap & 0x40:
            p += 4
        ssz = 4 if heap & 1 else 2
        gsz = 4 if heap & 2 else 2
        bsz = 4 if heap & 4 else 2

        def tsize(name):
            return 4 if self.rows.get(T[name], 0) > 0xFFFF else 2

        def csize(cname):
            bits, tabs = CODED[cname]
            m = max(self.rows.get(T[n], 0) if n else 0 for n in tabs)
            return 2 if m < (1 << (16 - bits)) else 4

        self.tables = {}
        for t in range(45):
            n = self.rows.get(t, 0)
            if not n:
                continue
            cols = []
            for name, kind in SCHEMA[t]:
                if kind in (2, 4):
                    cols.append((name, kind, None))
                elif kind == S:
                    cols.append((name, ssz, "s"))
                elif kind == G:
                    cols.append((name, gsz, "g"))
                elif kind == B:
                    cols.append((name, bsz, "b"))
                elif kind.startswith("t:"):
                    cols.append((name, tsize(kind[2:]), None))
                else:
                    cols.append((name, csize(kind[2:]), kind[2:]))
            rows = []
            for _ in range(n):
                r = {}
                for name, sz, k in cols:
                    v = struct.unpack_from("<I" if sz == 4 else "<H", d, p)[0]
                    p += sz
                    if k == "s":
                        v = self.string(v)
                    elif k in CODED:
                        bits, tabs = CODED[k]
                        v = (tabs[v & ((1 << bits) - 1)], v >> bits)
                    r[name] = v
                rows.append(r)
            self.tables[t] = rows

    def string(self, i):
        o, _ = self.streams["#Strings"]
        e = self.data.index(b"\0", o + i)
        return self.data[o + i:e].decode("utf-8", "replace")

    def blob(self, i):
        o, _ = self.streams["#Blob"]
        n, j = read_compressed(self.data, o + i)
        return self.data[j:j + n]

    def table(self, name):
        return self.tables.get(T[name], [])

    def method_body(self, rva):
        if not rva:
            return None
        d = self.data
        p = self.off(rva)
        h = d[p]
        if h & 3 == 2:
            return d[p + 1:p + 1 + (h >> 2)]
        size = struct.unpack_from("<I", d, p + 4)[0]
        hdr = (struct.unpack_from("<H", d, p)[0] >> 12) * 4
        return d[p + hdr:p + hdr + size]


def il_tokens(code):
    """Yield (opcode, token) for every token-operand instruction."""
    i, n = 0, len(code)
    while i < n:
        op = code[i]
        i += 1
        if op == 0xFE:
            op2 = code[i]
            i += 1
            sz = OP2.get(op2)
            if sz is None:
                return
            if op2 in TOKEN_OPS2:
                yield (0xFE00 | op2, struct.unpack_from("<I", code, i)[0])
            i += sz
            continue
        sz = OP1.get(op)
        if sz is None:
            return
        if sz == -1:
            cnt = struct.unpack_from("<I", code, i)[0]
            i += 4 + 4 * cnt
            continue
        if op in TOKEN_OPS1:
            yield (op, struct.unpack_from("<I", code, i)[0])
        i += sz


def sig_param_count(sig):
    cc = sig[0]
    i = 1
    if cc & 0x10:
        _, i = read_compressed(sig, i)
    n, _ = read_compressed(sig, i)
    return n
