"""Survey the Unity engine surface FF9's managed code needs.

  py unity_surface.py <FF9_Data/Managed> <outdir>

Roots: every method in the game's own assemblies. Walks calls (call/callvirt/newobj/
ldftn/ldvirtftn, plus virtual overrides) through the game and Unity's managed DLLs and
stops at the .NET framework. Reports the UnityEngine internal calls (native engine
entry points: what a Unity-less host must implement) and every P/Invoke.
"""
import collections, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cil import Assembly, il_tokens, sig_param_count, read_compressed, T

managed, outdir = sys.argv[1], sys.argv[2]
UNITY = ["UnityEngine", "UnityEngine.UI", "UnityEngine.Networking"]
ALL = sorted(f[:-4] for f in os.listdir(managed) if f.endswith(".dll"))
FRAMEWORK = [n for n in ALL if n == "mscorlib" or n.startswith("System") or n.startswith("Mono.")]
GAME = [n for n in ALL if n not in UNITY and n not in FRAMEWORK]
print("game assemblies:", GAME, file=sys.stderr)
os.makedirs(outdir, exist_ok=True)
asms = {}
for n in GAME + UNITY + FRAMEWORK:
    p = os.path.join(managed, n + ".dll")
    if os.path.exists(p):
        asms[n] = Assembly(p)

# ---- types -------------------------------------------------------------------------
types = {}        # (asm, ti) -> dict(name, methods[list of mi], extends, ifaces)
by_name = {}      # (asm, fullname) -> (asm, ti)
for an, a in asms.items():
    tds = a.table("TypeDef")
    md_n = len(a.table("MethodDef"))
    enclosing = {r["Nested"]: r["Enclosing"] for r in a.table("NestedClass")}

    def full(ti, a=a, tds=tds, enclosing=enclosing):
        r = tds[ti - 1]
        if ti in enclosing:
            return full(enclosing[ti]) + "/" + r["Name"]
        return (r["Namespace"] + "." if r["Namespace"] else "") + r["Name"]

    for ti, r in enumerate(tds, 1):
        start = r["MethodList"]
        end = tds[ti]["MethodList"] if ti < len(tds) else md_n + 1
        types[(an, ti)] = dict(name=full(ti), flags=r["Flags"], extends=r["Extends"],
                               methods=list(range(start, end)), ifaces=[])
        by_name[(an, full(ti))] = (an, ti)
    for r in a.table("InterfaceImpl"):
        types[(an, r["Class"])]["ifaces"].append(r["Interface"])

method_owner = {}
for (an, ti), t in types.items():
    for mi in t["methods"]:
        method_owner[(an, mi)] = (an, ti)


def asm_of_ref(an, aref_idx):
    return asms[an].table("AssemblyRef")[aref_idx - 1]["Name"]


tr_cache = {}


def resolve_typeref(an, idx):
    k = (an, idx)
    if k in tr_cache:
        return tr_cache[k]
    r = asms[an].table("TypeRef")[idx - 1]
    name = (r["Namespace"] + "." if r["Namespace"] else "") + r["Name"]
    kind, si = r["Scope"]
    res = None
    if kind == "AssemblyRef":
        res = ("ref", asm_of_ref(an, si), name)
    elif kind == "TypeRef":
        outer = resolve_typeref(an, si)
        if outer:
            res = ("ref", outer[1], outer[2] + "/" + r["Name"])
    elif kind == "Module":
        res = ("ref", an, name)
    tr_cache[k] = res
    return res


def resolve_type(an, coded):
    """coded = (tablename, index) -> (asm, ti) of a loaded TypeDef, or ('ext', asm, name)."""
    kind, idx = coded
    if idx == 0:
        return None
    if kind == "TypeDef":
        return (an, idx)
    if kind == "TypeRef":
        r = resolve_typeref(an, idx)
        if not r:
            return None
        if (r[1], r[2]) in by_name:
            return by_name[(r[1], r[2])]
        return ("ext", r[1], r[2])
    if kind == "TypeSpec":
        b = asms[an].blob(asms[an].table("TypeSpec")[idx - 1]["Signature"])
        if b and b[0] == 0x15 and b[1] in (0x11, 0x12):
            v, _ = read_compressed(b, 2)
            return resolve_type(an, (["TypeDef", "TypeRef", "TypeSpec"][v & 3], v >> 2))
    return None


def type_name(t):
    if t is None:
        return "?"
    if t[0] == "ext":
        return f"[{t[1]}]{t[2]}"
    return f"[{t[0]}]{types[t]['name']}"


def find_method(t, name, pc, depth=0):
    if t is None or t[0] == "ext" or depth > 30:
        return None
    a = asms[t[0]]
    mds = a.table("MethodDef")
    for mi in types[t]["methods"]:
        r = mds[mi - 1]
        if r["Name"] == name and sig_param_count(a.blob(r["Signature"])) == pc:
            return (t[0], mi)
    return find_method(resolve_type(t[0], types[t]["extends"]), name, pc, depth + 1)


ext_used = collections.Counter()      # framework / unresolved members referenced
unresolved = collections.Counter()


def resolve_token(an, tok):
    tab, idx = tok >> 24, tok & 0xFFFFFF
    a = asms[an]
    if tab == T["MethodDef"]:
        return (an, idx)
    if tab == T["MethodSpec"]:
        kind, i = a.table("MethodSpec")[idx - 1]["Method"]
        return resolve_token(an, (T[kind] << 24) | i)
    if tab == T["MemberRef"]:
        r = a.table("MemberRef")[idx - 1]
        sig = a.blob(r["Signature"])
        if sig[0] & 0x0F == 0x06:          # field
            return None
        kind, ci = r["Class"]
        if kind == "MethodDef":
            return (an, ci)
        if kind not in ("TypeDef", "TypeRef", "TypeSpec"):
            return None
        t = resolve_type(an, (kind, ci))
        pc = sig_param_count(sig)
        if t is None:
            unresolved[f"?::{r['Name']}"] += 1
            return None
        if t[0] == "ext" or t[0] in FRAMEWORK:
            ext_used[f"{type_name(t)}::{r['Name']}/{pc}"] += 1
            return None
        m = find_method(t, r["Name"], pc)
        if m is None:
            unresolved[f"{type_name(t)}::{r['Name']}/{pc}"] += 1
        return m
    return None


def ancestors(t, seen=None):
    seen = seen if seen is not None else set()
    if t is None or t[0] == "ext" or t in seen:
        return seen
    seen.add(t)
    ancestors(resolve_type(t[0], types[t]["extends"]), seen)
    for i in types[t]["ifaces"]:
        ancestors(resolve_type(t[0], i), seen)
    return seen


anc = {t: ancestors(t) for t in types}
subtypes = collections.defaultdict(set)
for t, s in anc.items():
    for x in s:
        subtypes[x].add(t)

# ---- reachability --------------------------------------------------------------------
reached = set()
work = []


def reach(m):
    if m and m not in reached and m[0] not in FRAMEWORK:
        reached.add(m)
        work.append(m)


for an in GAME:
    if an in asms:
        for mi in range(1, len(asms[an].table("MethodDef")) + 1):
            reach((an, mi))

virt_seen = set()
while work:
    while work:
        an, mi = work.pop()
        a = asms[an]
        r = a.table("MethodDef")[mi - 1]
        body = a.method_body(r["RVA"]) if not (r["ImplFlags"] & 0x1000) else None
        if body:
            for op, tok in il_tokens(body):
                if tok >> 24 in (T["MethodDef"], T["MemberRef"], T["MethodSpec"]):
                    reach(resolve_token(an, tok))
        # a reached constructor of a Unity component type: Unity calls its messages
    # virtual / interface dispatch: overrides in subtypes
    for m in list(reached):
        if m in virt_seen:
            continue
        virt_seen.add(m)
        an, mi = m
        r = asms[an].table("MethodDef")[mi - 1]
        if not r["Flags"] & 0x40:
            continue
        pc = sig_param_count(asms[an].blob(r["Signature"]))
        owner = method_owner[m]
        for d in subtypes.get(owner, ()):
            da = asms[d[0]]
            for dmi in types[d]["methods"]:
                dr = da.table("MethodDef")[dmi - 1]
                if (dr["Name"] == r["Name"] or dr["Name"].endswith("." + r["Name"])) and \
                        sig_param_count(da.blob(dr["Signature"])) == pc:
                    reach((d[0], dmi))
    # Unity UI components referenced by the game: their message methods are entry points
    for t, ti in list(types.items()):
        pass

# ---- report --------------------------------------------------------------------------
def mname(m):
    an, mi = m
    r = asms[an].table("MethodDef")[mi - 1]
    pc = sig_param_count(asms[an].blob(r["Signature"]))
    return f"{types[method_owner[m]]['name']}::{r['Name']}/{pc}"


icalls_all = collections.defaultdict(list)
icalls_reached = collections.defaultdict(list)
pinvokes = collections.defaultdict(list)
for an in asms:
    if an in FRAMEWORK:
        continue
    a = asms[an]
    mrefs = {r["Name"] for r in a.table("ModuleRef")}
    implmap = {}
    for r in a.table("ImplMap"):
        if r["Member"][0] == "MethodDef":
            implmap[r["Member"][1]] = (a.table("ModuleRef")[r["ImportScope"] - 1]["Name"],
                                       r["ImportName"])
    for mi, r in enumerate(a.table("MethodDef"), 1):
        m = (an, mi)
        if r["ImplFlags"] & 0x1000:
            icalls_all[an].append(mname(m))
            if m in reached:
                icalls_reached[an].append(mname(m))
        if mi in implmap:
            mod, imp = implmap[mi]
            pinvokes[mod].append(dict(asm=an, method=mname(m), import_name=imp,
                                      reached=m in reached))

by_class = collections.Counter(n.split("::")[0] for n in icalls_reached.get("UnityEngine", []))
reached_by_asm = collections.Counter(m[0] for m in reached)

game_types_used = collections.Counter()
a = asms["Assembly-CSharp"]
for i, r in enumerate(a.table("TypeRef"), 1):
    t = resolve_typeref("Assembly-CSharp", i)
    if t and t[1].startswith("UnityEngine"):
        game_types_used[f"[{t[1]}]{t[2]}"] += 1

mono_behaviours = 0
for t in types:
    if t[0] in GAME and any(types[x]["name"] == "UnityEngine.MonoBehaviour" for x in anc[t]
                            if x[0] == "UnityEngine"):
        mono_behaviours += 1

summary = dict(
    assemblies={an: dict(types=len(a.table("TypeDef")), methods=len(a.table("MethodDef")),
                         reached=reached_by_asm.get(an, 0),
                         icalls=len(icalls_all.get(an, [])),
                         icalls_reached=len(icalls_reached.get(an, [])))
                for an, a in asms.items() if an not in FRAMEWORK},
    unity_types_referenced_by_game=len(game_types_used),
    monobehaviour_subclasses_in_game=mono_behaviours,
    pinvoke_modules={mod: dict(total=len(v), reached=sum(x["reached"] for x in v))
                     for mod, v in pinvokes.items()},
    framework_members_used=len(ext_used),
    unresolved_refs=len(unresolved),
    unityengine_icalls_reached_by_class=by_class.most_common(),
)
json.dump(summary, open(os.path.join(outdir, "unity_surface.json"), "w"), indent=1)
with open(os.path.join(outdir, "unity_icalls_reached.txt"), "w") as f:
    for an, v in icalls_reached.items():
        for n in sorted(v):
            f.write(f"{an}\t{n}\n")
with open(os.path.join(outdir, "pinvokes.txt"), "w") as f:
    for mod, v in sorted(pinvokes.items()):
        for x in v:
            f.write(f"{mod}\t{x['import_name']}\t{x['asm']}\t{x['method']}\t"
                    f"{'reached' if x['reached'] else '-'}\n")
with open(os.path.join(outdir, "framework_members.txt"), "w") as f:
    for k, v in ext_used.most_common():
        f.write(f"{v}\t{k}\n")
with open(os.path.join(outdir, "unresolved.txt"), "w") as f:
    for k, v in unresolved.most_common():
        f.write(f"{v}\t{k}\n")
with open(os.path.join(outdir, "unity_types_used_by_game.txt"), "w") as f:
    for k, v in sorted(game_types_used.items()):
        f.write(f"{k}\n")

s = dict(summary)
s["unityengine_icalls_reached_by_class"] = by_class.most_common(40)
print(json.dumps(s, indent=1))
