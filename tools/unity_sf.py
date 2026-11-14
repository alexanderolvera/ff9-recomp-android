"""Unity 5.2 serialized files (format 15, no type trees): object table, externals, and the
few classes needed to list a scene's GameObjects and their scripts.

  py unity_sf.py <FF9_Data> <file>        e.g. level0
"""
import os, struct, sys

CLASS = {1: "GameObject", 4: "Transform", 20: "Camera", 114: "MonoBehaviour", 115: "MonoScript",
         224: "RectTransform", 223: "Canvas", 222: "CanvasRenderer", 81: "AudioListener",
         124: "Behaviour", 92: "GUILayer", 104: "RenderSettings", 157: "LightmapSettings",
         196: "NavMeshSettings", 23: "MeshRenderer", 33: "MeshFilter", 108: "Light",
         65: "BoxCollider", 21: "Material", 28: "Texture2D", 43: "Mesh", 48: "Shader",
         49: "TextAsset", 83: "AudioClip", 95: "Animator", 111: "Animation", 74: "AnimationClip",
         128: "Font", 213: "Sprite", 212: "SpriteRenderer", 137: "SkinnedMeshRenderer",
         142: "AssetBundle", 147: "ResourceManager", 141: "BuildSettings", 129: "PlayerSettings",
         6: "GlobalGameManager", 11: "AudioManager", 13: "InputManager", 30: "GraphicsSettings",
         47: "QualitySettings", 55: "PhysicsManager", 78: "TagManager", 5: "TimeManager",
         126: "NavMeshProjectSettings", 159: "EditorSettings", 182: "ScriptMapper",
         150: "PreloadData", 120: "LineRenderer", 198: "ParticleSystem", 199: "ParticleSystemRenderer",
         102: "TextMesh", 82: "AudioSource", 135: "SphereCollider", 136: "CapsuleCollider",
         54: "Rigidbody", 64: "MeshCollider", 61: "BoxCollider2D", 50: "Rigidbody2D",
         58: "CircleCollider2D", 225: "CanvasGroup", 221: "AnimatorOverrideController",
         91: "AnimatorController", 89: "Cubemap", 117: "Texture3D", 27: "Texture", 84: "RenderTexture",
         68: "EdgeCollider2D", 60: "PolygonCollider2D", 290: "AssetBundleManifest", 3: "LevelGameManager",
         290: "AssetBundleManifest"}


class R:
    def __init__(self, d, p=0):
        self.d, self.p = d, p

    def u8(self):
        v = self.d[self.p]; self.p += 1; return v

    def u16(self):
        v = struct.unpack_from("<H", self.d, self.p)[0]; self.p += 2; return v

    def i16(self):
        v = struct.unpack_from("<h", self.d, self.p)[0]; self.p += 2; return v

    def i32(self):
        v = struct.unpack_from("<i", self.d, self.p)[0]; self.p += 4; return v

    def u32(self):
        v = struct.unpack_from("<I", self.d, self.p)[0]; self.p += 4; return v

    def i64(self):
        v = struct.unpack_from("<q", self.d, self.p)[0]; self.p += 8; return v

    def cstr(self):
        e = self.d.index(b"\0", self.p); s = self.d[self.p:e].decode("utf-8", "replace"); self.p = e + 1; return s

    def align(self):
        self.p = (self.p + 3) & ~3

    def string(self):
        n = self.i32(); s = self.d[self.p:self.p + n].decode("utf-8", "replace"); self.p += n; self.align(); return s

    def pptr(self):
        return (self.i32(), self.i64())


class SerializedFile:
    def __init__(self, path):
        self.path = path
        self.name = os.path.basename(path)
        d = open(path, "rb").read()
        self.d = d
        meta_size, file_size, version, data_off = struct.unpack_from(">IIII", d, 0)
        assert version == 15, version
        assert d[16] == 0, "big endian"
        r = R(d, 20)
        self.unity = r.cstr()
        self.platform = r.i32()
        tree = r.u8()
        assert tree == 0, "type trees present"
        self.types = []
        for _ in range(r.i32()):
            cid = r.i32()
            script_hash = None
            if cid < 0:
                script_hash = d[r.p:r.p + 16]; r.p += 16
            r.p += 16
            self.types.append((cid, script_hash))
        self.objects = {}
        for _ in range(r.i32()):
            r.align()
            pid = r.i64(); start = r.u32(); size = r.u32(); tid = r.i32()
            cid = r.u16(); sidx = r.i16(); stripped = r.u8()
            self.objects[pid] = dict(start=data_off + start, size=size, type=tid, cls=cid,
                                     script_index=sidx)
        self.script_types = []
        for _ in range(r.i32()):
            fi = r.i32(); r.align(); self.script_types.append((fi, r.i64()))
        self.externals = []
        for _ in range(r.i32()):
            r.cstr(); r.p += 16; r.i32(); self.externals.append(r.cstr())

    def reader(self, pid):
        return R(self.d, self.objects[pid]["start"])


def game_object(sf, pid):
    r = sf.reader(pid)
    comps = [(r.i32(), r.pptr()) for _ in range(r.i32())]
    layer = r.u32()
    name = r.string()
    return dict(name=name, components=comps, layer=layer)


def mono_behaviour_script(sf, pid):
    r = sf.reader(pid)
    go = r.pptr(); enabled = r.u8(); r.align()
    script = r.pptr()
    name = r.string()
    return dict(go=go, enabled=enabled, script=script, name=name, fields_at=r.p)


def mono_script(sf, pid):
    r = sf.reader(pid)
    name = r.string(); order = r.i32(); r.p += 16
    cls = r.string(); ns = r.string(); asm = r.string()
    return dict(name=name, order=order, cls=(ns + "." if ns else "") + cls, assembly=asm)


def main():
    data_dir, fname = sys.argv[1], sys.argv[2]
    files = {}

    def load(name):
        if name not in files:
            p = os.path.join(data_dir, name)
            files[name] = SerializedFile(p) if os.path.exists(p) else None
        return files[name]

    sf = load(fname)
    print(f"{fname}: Unity {sf.unity}, platform {sf.platform}, {len(sf.objects)} objects, "
          f"externals {sf.externals}")
    counts = {}
    for o in sf.objects.values():
        counts[CLASS.get(o["cls"], o["cls"])] = counts.get(CLASS.get(o["cls"], o["cls"]), 0) + 1
    print("classes:", counts)

    def resolve(fileid, pid):
        if fileid == 0:
            return sf, pid
        ext = sf.externals[fileid - 1]
        base = ext.split("/")[-1]
        return load(base), pid

    for pid, o in sf.objects.items():
        if o["cls"] != 1:
            continue
        go = game_object(sf, pid)
        print(f"GameObject '{go['name']}' (layer {go['layer']})")
        for cid, (fi, cp) in go["components"]:
            if fi != 0 or cp not in sf.objects:
                print(f"    {CLASS.get(cid, cid)} -> external {fi}:{cp}")
                continue
            if sf.objects[cp]["cls"] == 114:
                mb = mono_behaviour_script(sf, cp)
                tf, tp = resolve(*mb["script"])
                ms = mono_script(tf, tp) if tf and tp in tf.objects else None
                print(f"    MonoBehaviour {ms['cls'] if ms else mb['script']}"
                      f"{' [' + ms['assembly'] + ']' if ms else ''}{'' if mb['enabled'] else ' (disabled)'}")
            else:
                print(f"    {CLASS.get(sf.objects[cp]['cls'], sf.objects[cp]['cls'])}")


if __name__ == "__main__":
    main()
