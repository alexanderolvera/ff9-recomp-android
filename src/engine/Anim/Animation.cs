// Legacy animation: AnimationClip (asset curves or runtime SetCurve), Animation component,
// AnimationState; sampled onto transforms each frame between Update and LateUpdate.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FF9Engine.Assets;
using FF9Engine.Core;
using FF9Engine.Native;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace FF9Engine.Anim;

public sealed class QuatKey { public float Time; public Quaternion Value, In, Out; }
public sealed class Vec3Key { public float Time; public Vector3 Value, In, Out; }

public sealed class ClipCurve {
    public string Path = "";
    public string Property = "";          // localPosition.x, m_LocalRotation.w, ...
    public Type? Target;
    public CurveData? Float;
    public List<QuatKey>? Rot;
    public List<Vec3Key>? Pos, Scale;
}

public sealed class NClip : NObject, ICloneable {
    public float FrameRate = 60, Length;
    public WrapMode Wrap = WrapMode.Default;
    public bool Legacy = true;
    public readonly List<ClipCurve> Curves = new();
    public object Clone() { var c = (NClip)MemberwiseClone(); return c; }

    public void Recalc() {
        float len = 0;
        foreach (var c in Curves) {
            if (c.Float != null && c.Float.Keys.Count > 0) len = Math.Max(len, c.Float.Keys[^1].time);
            if (c.Rot != null && c.Rot.Count > 0) len = Math.Max(len, c.Rot[^1].Time);
            if (c.Pos != null && c.Pos.Count > 0) len = Math.Max(len, c.Pos[^1].Time);
            if (c.Scale != null && c.Scale.Count > 0) len = Math.Max(len, c.Scale[^1].Time);
        }
        Length = len;
    }

    // ---- sampling ----
    static float Hermite(float t, float t0, float t1, float v0, float v1, float out0, float in1) {
        float dt = t1 - t0;
        if (dt <= 0) return v1;
        if (float.IsInfinity(out0) || float.IsInfinity(in1)) return v0;
        float s = (t - t0) / dt, s2 = s * s, s3 = s2 * s;
        return (2 * s3 - 3 * s2 + 1) * v0 + (s3 - 2 * s2 + s) * dt * out0 + (-2 * s3 + 3 * s2) * v1 + (s3 - s2) * dt * in1;
    }

    static int Find(int n, Func<int, float> time, float t) {
        int lo = 0, hi = n - 1;
        while (lo < hi) { int mid = (lo + hi + 1) / 2; if (time(mid) <= t) lo = mid; else hi = mid - 1; }
        return lo;
    }

    public static Quaternion Sample(List<QuatKey> k, float t) {
        if (k.Count == 1 || t <= k[0].Time) return k[0].Value;
        if (t >= k[^1].Time) return k[^1].Value;
        int i = Find(k.Count, j => k[j].Time, t);
        var a = k[i]; var b = k[i + 1];
        var q = new Quaternion(
            Hermite(t, a.Time, b.Time, a.Value.x, b.Value.x, a.Out.x, b.In.x), Hermite(t, a.Time, b.Time, a.Value.y, b.Value.y, a.Out.y, b.In.y),
            Hermite(t, a.Time, b.Time, a.Value.z, b.Value.z, a.Out.z, b.In.z), Hermite(t, a.Time, b.Time, a.Value.w, b.Value.w, a.Out.w, b.In.w));
        return M.Normalize(q);
    }

    public static Vector3 Sample(List<Vec3Key> k, float t) {
        if (k.Count == 1 || t <= k[0].Time) return k[0].Value;
        if (t >= k[^1].Time) return k[^1].Value;
        int i = Find(k.Count, j => k[j].Time, t);
        var a = k[i]; var b = k[i + 1];
        return new Vector3(Hermite(t, a.Time, b.Time, a.Value.x, b.Value.x, a.Out.x, b.In.x), Hermite(t, a.Time, b.Time, a.Value.y, b.Value.y, a.Out.y, b.In.y),
            Hermite(t, a.Time, b.Time, a.Value.z, b.Value.z, a.Out.z, b.In.z));
    }

    /// Applies the clip at time t under root (paths are relative to it).
    public void Apply(NTransform root, float t, Dictionary<string, NTransform?> cache) {
        // float curves for one property group (localPosition.x/.y/.z) accumulate per transform
        var pending = new Dictionary<NTransform, (Vector3? p, Quaternion? r, Vector3? s, Vector3? euler)>();
        foreach (var c in Curves) {
            if (!cache.TryGetValue(c.Path, out var tr)) cache[c.Path] = tr = c.Path.Length == 0 ? root : root.Find(c.Path);
            if (tr == null) continue;
            if (c.Rot != null && c.Rot.Count > 0) { tr.LocalRotation = Sample(c.Rot, t); continue; }
            if (c.Pos != null && c.Pos.Count > 0) { tr.LocalPosition = Sample(c.Pos, t); continue; }
            if (c.Scale != null && c.Scale.Count > 0) { tr.LocalScale = Sample(c.Scale, t); continue; }
            if (c.Float == null || c.Float.Keys.Count == 0) continue;
            float v = c.Float.Evaluate(t);
            string p = c.Property;
            if (p.StartsWith("m_")) p = char.ToLowerInvariant(p[2]) + p[3..];   // m_LocalPosition.x -> localPosition.x
            if (!pending.TryGetValue(tr, out var cur)) cur = (null, null, null, null);
            switch (p) {
                case "localPosition.x": cur.p = new Vector3(v, (cur.p ?? tr.LocalPosition).y, (cur.p ?? tr.LocalPosition).z); break;
                case "localPosition.y": cur.p = new Vector3((cur.p ?? tr.LocalPosition).x, v, (cur.p ?? tr.LocalPosition).z); break;
                case "localPosition.z": cur.p = new Vector3((cur.p ?? tr.LocalPosition).x, (cur.p ?? tr.LocalPosition).y, v); break;
                case "localScale.x": cur.s = new Vector3(v, (cur.s ?? tr.LocalScale).y, (cur.s ?? tr.LocalScale).z); break;
                case "localScale.y": cur.s = new Vector3((cur.s ?? tr.LocalScale).x, v, (cur.s ?? tr.LocalScale).z); break;
                case "localScale.z": cur.s = new Vector3((cur.s ?? tr.LocalScale).x, (cur.s ?? tr.LocalScale).y, v); break;
                case "localRotation.x": { var q = cur.r ?? tr.LocalRotation; cur.r = new Quaternion(v, q.y, q.z, q.w); break; }
                case "localRotation.y": { var q = cur.r ?? tr.LocalRotation; cur.r = new Quaternion(q.x, v, q.z, q.w); break; }
                case "localRotation.z": { var q = cur.r ?? tr.LocalRotation; cur.r = new Quaternion(q.x, q.y, v, q.w); break; }
                case "localRotation.w": { var q = cur.r ?? tr.LocalRotation; cur.r = new Quaternion(q.x, q.y, q.z, v); break; }
                case "localEulerAngles.x": case "localEulerAnglesBaked.x": case "localEulerAnglesRaw.x": { var e = cur.euler ?? M.ToEuler(tr.LocalRotation); cur.euler = new Vector3(v, e.y, e.z); break; }
                case "localEulerAngles.y": case "localEulerAnglesBaked.y": case "localEulerAnglesRaw.y": { var e = cur.euler ?? M.ToEuler(tr.LocalRotation); cur.euler = new Vector3(e.x, v, e.z); break; }
                case "localEulerAngles.z": case "localEulerAnglesBaked.z": case "localEulerAnglesRaw.z": { var e = cur.euler ?? M.ToEuler(tr.LocalRotation); cur.euler = new Vector3(e.x, e.y, v); break; }
                default: Log.Once("animprop:" + p, $"animation property {c.Target?.Name}.{p} not applied"); break;
            }
            pending[tr] = cur;
        }
        foreach (var (tr, (p, r, s, e)) in pending) {
            if (p is { } pp) tr.LocalPosition = pp;
            if (s is { } ss) tr.LocalScale = ss;
            if (r is { } rr) tr.LocalRotation = M.Normalize(rr);
            if (e is { } ee) tr.LocalRotation = M.Euler(ee);
            tr.MarkChanged();
        }
    }
}

public sealed class NAnimState {
    public IntPtr Handle;
    public AnimationState? Managed;
    public string Name = "";
    public NClip? Clip;
    public AnimationClip? ClipObj;
    public float Time, Speed = 1, Weight, TargetWeight, FadeSpeed;
    public bool Enabled;
    public WrapMode Wrap = WrapMode.Default;
    public int Layer;
    public NAnimation Owner = null!;
    public float Length => Clip?.Length ?? 0;
}

public sealed class NAnimation : NComponent {
    public AnimationClip? DefaultClip;
    public readonly List<NAnimState> States = new();
    public WrapMode Wrap = WrapMode.Default;
    public bool PlayAutomatically = true;
    public readonly Dictionary<string, NTransform?> PathCache = new();
    public readonly Queue<(string name, float fade, PlayMode mode)> Queued = new();

    public NAnimState? State(string name) => States.FirstOrDefault(s => s.Name == name);

    public NAnimState AddState(string name, AnimationClip clip) {
        var s = State(name);
        if (s == null) {
            s = new NAnimState { Name = name, Owner = this };
            s.Handle = Handles.New(s);
            var m = (AnimationState)RuntimeHelpers.GetUninitializedObject(typeof(AnimationState));
            Handles.SetPtr(m, s.Handle);
            s.Managed = m;
            States.Add(s);
        }
        s.ClipObj = clip;
        s.Clip = Clips.Of(clip);
        return s;
    }

    WrapMode EffectiveWrap(NAnimState s) {
        var w = s.Wrap != WrapMode.Default ? s.Wrap : s.Clip?.Wrap is { } cw && cw != WrapMode.Default ? cw : Wrap;
        return w;
    }

    public bool Playing => States.Any(s => s.Enabled);

    public void Advance(float dt) {
        foreach (var s in States) {
            if (!s.Enabled) continue;
            s.Time += dt * s.Speed;
            if (s.FadeSpeed != 0) {
                s.Weight += s.FadeSpeed * dt;
                if (s.FadeSpeed > 0 && s.Weight >= s.TargetWeight || s.FadeSpeed < 0 && s.Weight <= s.TargetWeight) {
                    s.Weight = s.TargetWeight; s.FadeSpeed = 0;
                    if (s.Weight <= 0) s.Enabled = false;
                }
            }
            var w = EffectiveWrap(s);
            float len = s.Length;
            if ((w == WrapMode.Once || w == WrapMode.Default) && len > 0 && (s.Speed >= 0 ? s.Time >= len : s.Time <= 0)) {
                s.Enabled = false;      // Once: stops (and rewinds) at the end
                s.Time = 0;
                Apply(s, w, len);       // last frame applied
                if (Queued.Count > 0) { var (n, f, m) = Queued.Dequeue(); Animations.PlayByName(this, n, m); }
            }
        }
    }

    float WrapTime(NAnimState s, WrapMode w, float len) {
        float t = s.Time;
        if (len <= 0) return 0;
        switch (w) {
            case WrapMode.Loop: t %= len; if (t < 0) t += len; return t;
            case WrapMode.PingPong: { float x = t % (2 * len); if (x < 0) x += 2 * len; return x > len ? 2 * len - x : x; }
            case WrapMode.ClampForever: return Math.Clamp(t, 0, len);
            default: return Math.Clamp(t, 0, len);
        }
    }

    void Apply(NAnimState s, WrapMode w, float len) {
        if (s.Clip == null) return;
        float t = s.Time >= len && (w == WrapMode.Once || w == WrapMode.Default) ? len : WrapTime(s, w, len);
        s.Clip.Apply(GameObject.Transform, t, PathCache);
    }

    /// Applies enabled states in order of layer then weight (the strongest last).
    public void Sample() {
        foreach (var s in States.Where(s => s.Enabled && s.Clip != null).OrderBy(s => s.Layer).ThenBy(s => s.Weight))
            Apply(s, EffectiveWrap(s), s.Length);
    }
}

public static class Clips {
    public static NClip? Of(AnimationClip? c) => Registry.Get(c) switch { NAsset { Data: NClip x } => x, NClip x => x, _ => null };
    static NClip C(AnimationClip c) => Of(c) ?? throw new NullReferenceException("clip is null");

    public static void Register() {
        AssetDB.Readers[ClassIds.AnimationClip] = Read;
    }

    static void Read(NAsset a, BinReader r) {
        var c = new NClip();
        a.Data = c;
        var tree = a.File!.TreeOf(a.File.Objects[a.PathId]);
        if (tree != null) ReadTree(c, a, r, tree);
        else ReadBinary(c, a, r);
        c.Name = a.Name;
        c.Recalc();
    }

    static float F(object? o) => o switch { float f => f, int i => i, _ => 0 };
    static Quaternion Q(object? o) { var d = (Dictionary<string, object?>)o!; return new Quaternion(F(d["x"]), F(d["y"]), F(d["z"]), F(d["w"])); }
    static Vector3 V(object? o) { var d = (Dictionary<string, object?>)o!; return new Vector3(F(d["x"]), F(d["y"]), F(d["z"])); }

    static void ReadTree(NClip c, NAsset a, BinReader r, TypeTree tree) {
        var d = new Dictionary<string, object?>();
        foreach (var n in tree.Root.Children) {
            // stop before the mecanim muscle data (large, unused by legacy clips)
            if (n.Name == "m_MuscleClipSize") break;
            d[n.Name] = TreeFields.Decode(n, r);
        }
        a.Name = d["m_Name"] as string ?? "";
        c.Legacy = d.TryGetValue("m_Legacy", out var lg) && lg is bool b && b;
        c.FrameRate = d.TryGetValue("m_SampleRate", out var sr) ? F(sr) : 60;
        c.Wrap = d.TryGetValue("m_WrapMode", out var wm) && wm is int wi ? (WrapMode)wi : WrapMode.Default;
        if (d.TryGetValue("m_RotationCurves", out var rc) && rc is List<object?> rcs)
            foreach (Dictionary<string, object?> e in rcs) {
                var curve = (Dictionary<string, object?>)e["curve"]!;
                var keys = ((List<object?>)curve["m_Curve"]!).Cast<Dictionary<string, object?>>()
                    .Select(k => new QuatKey { Time = F(k["time"]), Value = Q(k["value"]), In = Q(k["inSlope"]), Out = Q(k["outSlope"]) }).ToList();
                c.Curves.Add(new ClipCurve { Path = e["path"] as string ?? "", Rot = keys, Target = typeof(Transform) });
            }
        foreach (var (field, isPos) in new[] { ("m_PositionCurves", true), ("m_ScaleCurves", false) })
            if (d.TryGetValue(field, out var pc) && pc is List<object?> pcs)
                foreach (Dictionary<string, object?> e in pcs) {
                    var curve = (Dictionary<string, object?>)e["curve"]!;
                    var keys = ((List<object?>)curve["m_Curve"]!).Cast<Dictionary<string, object?>>()
                        .Select(k => new Vec3Key { Time = F(k["time"]), Value = V(k["value"]), In = V(k["inSlope"]), Out = V(k["outSlope"]) }).ToList();
                    var cc = new ClipCurve { Path = e["path"] as string ?? "", Target = typeof(Transform) };
                    if (isPos) cc.Pos = keys; else cc.Scale = keys;
                    c.Curves.Add(cc);
                }
        if (d.TryGetValue("m_FloatCurves", out var fc) && fc is List<object?> fcs)
            foreach (Dictionary<string, object?> e in fcs) {
                var curve = (Dictionary<string, object?>)e["curve"]!;
                var cd = new CurveData();
                foreach (Dictionary<string, object?> k in (List<object?>)curve["m_Curve"]!)
                    cd.Keys.Add(new Keyframe(F(k["time"]), F(k["value"]), F(k["inSlope"]), F(k["outSlope"])));
                c.Curves.Add(new ClipCurve { Path = e["path"] as string ?? "", Property = e["attribute"] as string ?? "", Float = cd, Target = ClassIds.ManagedType(e["classID"] is int ci ? ci : 4) });
            }
        if (d.TryGetValue("m_CompressedRotationCurves", out var crc) && crc is List<object?> crcs && crcs.Count > 0)
            Log.Once("compressedrot", $"clip '{a.Name}': compressed rotation curves not decoded");
    }

    static List<T> ReadKeys<T>(BinReader r, Func<BinReader, T> key) {
        int n = r.I32();
        var l = new List<T>(n);
        for (int i = 0; i < n; i++) l.Add(key(r));
        r.I32(); r.I32();      // pre/post infinity
        return l;
    }

    /// AnimationClip (5.2, no type tree): name, legacy/compressed/highQuality flags, rotation,
    /// compressed rotation, position, scale, float and PPtr curves, sample rate, wrap mode.
    static void ReadBinary(NClip c, NAsset a, BinReader r) {
        a.Name = r.String();
        c.Legacy = r.Bool(); bool compressed = r.Bool(); r.Bool(); r.Align4();
        int nr = r.I32();
        for (int i = 0; i < nr; i++) {
            var keys = ReadKeys(r, b => new QuatKey { Time = b.F32(), Value = b.Quat(), In = b.Quat(), Out = b.Quat() });
            c.Curves.Add(new ClipCurve { Rot = keys, Path = r.String(), Target = typeof(Transform) });
        }
        int ncr = r.I32();
        if (ncr > 0) { Log.Once("compressedrot", $"clip '{a.Name}': compressed rotation curves not decoded"); return; }
        foreach (bool isPos in new[] { true, false }) {
            int np = r.I32();
            for (int i = 0; i < np; i++) {
                var keys = ReadKeys(r, b => new Vec3Key { Time = b.F32(), Value = b.V3(), In = b.V3(), Out = b.V3() });
                var cc = new ClipCurve { Path = r.String(), Target = typeof(Transform) };
                if (isPos) cc.Pos = keys; else cc.Scale = keys;
                c.Curves.Add(cc);
            }
        }
        int nf = r.I32();
        for (int i = 0; i < nf; i++) {
            var cd = new CurveData();
            int nk = r.I32();
            for (int k = 0; k < nk; k++) cd.Keys.Add(new Keyframe(r.F32(), r.F32(), r.F32(), r.F32()));
            cd.Pre = Curves.FromInternal(r.I32()); cd.Post = Curves.FromInternal(r.I32());
            string attr = r.String(), path = r.String();
            int cls = r.I32(); r.PPtr();
            c.Curves.Add(new ClipCurve { Float = cd, Property = attr, Path = path, Target = ClassIds.ManagedType(cls) });
        }
        int npp = r.I32();
        for (int i = 0; i < npp; i++) { int nk = r.I32(); r.Skip(nk * 16L); r.String(); r.String(); r.I32(); r.PPtr(); }
        c.FrameRate = r.F32();
        c.Wrap = (WrapMode)r.I32();
    }

    [Icall("UnityEngine.AnimationClip::Internal_CreateAnimationClip")] static void Create(AnimationClip self) => Registry.Add(new NClip(), self);
    [Icall("UnityEngine.AnimationClip::get_length")] static float Length(AnimationClip c) => C(c).Length;
    [Icall("UnityEngine.AnimationClip::get_startTime")] static float Start(AnimationClip c) => 0;
    [Icall("UnityEngine.AnimationClip::get_stopTime")] static float Stop(AnimationClip c) => C(c).Length;
    [Icall("UnityEngine.AnimationClip::get_frameRate")] static float GetRate(AnimationClip c) => C(c).FrameRate;
    [Icall("UnityEngine.AnimationClip::set_frameRate")] static void SetRate(AnimationClip c, float v) => C(c).FrameRate = v;
    [Icall("UnityEngine.AnimationClip::get_wrapMode")] static WrapMode GetWrap(AnimationClip c) => C(c).Wrap;
    [Icall("UnityEngine.AnimationClip::set_wrapMode")] static void SetWrap(AnimationClip c, WrapMode v) => C(c).Wrap = v;
    [Icall("UnityEngine.AnimationClip::get_legacy")] static bool GetLegacy(AnimationClip c) => C(c).Legacy;
    [Icall("UnityEngine.AnimationClip::set_legacy")] static void SetLegacy(AnimationClip c, bool v) => C(c).Legacy = v;
    [Icall("UnityEngine.AnimationClip::get_humanMotion")] static bool Human(AnimationClip c) => false;
    [Icall("UnityEngine.AnimationClip::INTERNAL_CALL_EnsureQuaternionContinuity")] static void Continuity(AnimationClip c) { }
    [Icall("UnityEngine.AnimationClip::INTERNAL_CALL_ClearCurves")] static void Clear(AnimationClip c) { var x = C(c); x.Curves.Clear(); x.Recalc(); }
    [Icall("UnityEngine.AnimationClip::INTERNAL_get_localBounds")] static void GetBounds(AnimationClip c, ref Bounds b) => b = default;
    [Icall("UnityEngine.AnimationClip::INTERNAL_set_localBounds")] static void SetBounds(AnimationClip c, ref Bounds b) { }
    [Icall("UnityEngine.AnimationClip::AddEventInternal")] static void AddEvent(AnimationClip c, object e) => Log.Once("AnimationClip.AddEvent");
    [Icall("UnityEngine.AnimationClip::SetEventsInternal")] static void SetEvents(AnimationClip c, Array e) { }
    [Icall("UnityEngine.AnimationClip::GetEventsInternal")] static Array GetEvents(AnimationClip c) => Array.Empty<AnimationEvent>();

    [Icall("UnityEngine.AnimationClip::SetCurve")]
    static void SetCurve(AnimationClip clip, string path, Type type, string property, AnimationCurve curve) {
        var c = C(clip);
        c.Curves.RemoveAll(x => x.Path == path && x.Property == property);
        if (curve != null) {
            var src = Handles.Data<CurveData>(curve);
            var cd = new CurveData { Pre = src.Pre, Post = src.Post };
            cd.Keys.AddRange(src.Keys);
            c.Curves.Add(new ClipCurve { Path = path ?? "", Property = property, Float = cd, Target = type });
        }
        c.Recalc();
    }

    [Icall("UnityEngine.AnimationClip::SampleAnimation")]
    static void SampleAnimation(AnimationClip clip, GameObject go, float time) {
        var g = Registry.Of<NGameObject>(go);
        C(clip).Apply(g.Transform, time, new Dictionary<string, NTransform?>());
    }
}

public static class Animations {
    public static readonly List<NAnimation> All = new();

    public static void Register() {
        Components.Factories[111] = () => { var a = new NAnimation(); All.Add(a); return a; };
        SceneBuilder.ComponentReaders[111] = (c, r, b) => {
            var a = (NAnimation)c;
            r.PPtr();
            a.Enabled = r.Bool(); r.Align4();
            var (dfi, dpi) = r.PPtr();
            if (dpi != 0) a.DefaultClip = b.Resolve(b.File, dfi, dpi, typeof(AnimationClip)) as AnimationClip;
            int n = r.I32();
            for (int i = 0; i < n; i++) {
                var (fi, pi) = r.PPtr();
                if (pi != 0 && b.Resolve(b.File, fi, pi, typeof(AnimationClip)) is AnimationClip clip)
                    a.AddState(Registry.Get(clip)?.Name ?? "", clip);
            }
            a.Wrap = (WrapMode)r.I32();
            a.PlayAutomatically = r.Bool();
        };
        Cloner.RecordCopiers[typeof(NAnimation)] = c => {
            var s = (NAnimation)c;
            var n = new NAnimation { DefaultClip = s.DefaultClip, Wrap = s.Wrap, PlayAutomatically = s.PlayAutomatically };
            foreach (var st in s.States) if (st.ClipObj != null) n.AddState(st.Name, st.ClipObj);
            All.Add(n);
            return n;
        };
        Player.AnimationTick = Tick;
    }

    static NAnimation A(Animation a) => Registry.Of<NAnimation>(a);
    static NAnimState S(AnimationState s) => Handles.Get<NAnimState>(Handles.Ptr(s));

    /// Between Update and LateUpdate: advance and apply every playing Animation.
    public static void Tick(float dt) {
        All.RemoveAll(a => a.Managed == null || Registry.Get(a.Managed) != a);
        foreach (var a in All) {
            if (!a.Enabled || a.IsAsset || !a.GameObject.ActiveInHierarchy || !a.Playing) continue;
            a.Advance(dt);
            a.Sample();
        }
    }

    public static bool PlayByName(NAnimation a, string name, PlayMode mode) {
        var s = a.State(name);
        if (s == null) return false;
        foreach (var o in a.States) if (o != s && (mode == PlayMode.StopAll || o.Layer == s.Layer)) { o.Enabled = false; o.Weight = 0; }
        if (!s.Enabled) s.Time = 0;
        s.Enabled = true;
        s.Weight = 1;
        s.FadeSpeed = 0;
        return true;
    }

    [Icall("UnityEngine.Animation::get_clip")] static AnimationClip? GetClip(Animation a) => A(a).DefaultClip;
    [Icall("UnityEngine.Animation::set_clip")] static void SetClip(Animation a, AnimationClip c) => A(a).DefaultClip = c;
    [Icall("UnityEngine.Animation::get_playAutomatically")] static bool GetAuto(Animation a) => A(a).PlayAutomatically;
    [Icall("UnityEngine.Animation::set_playAutomatically")] static void SetAuto(Animation a, bool v) => A(a).PlayAutomatically = v;
    [Icall("UnityEngine.Animation::get_wrapMode")] static WrapMode GetWrap(Animation a) => A(a).Wrap;
    [Icall("UnityEngine.Animation::set_wrapMode")] static void SetWrap(Animation a, WrapMode v) => A(a).Wrap = v;
    [Icall("UnityEngine.Animation::INTERNAL_CALL_Stop")] static void Stop(Animation a) { foreach (var s in A(a).States) { s.Enabled = false; s.Time = 0; } }
    [Icall("UnityEngine.Animation::Internal_StopByName")] static void StopByName(Animation a, string n) { var s = A(a).State(n); if (s != null) { s.Enabled = false; s.Time = 0; } }
    [Icall("UnityEngine.Animation::Internal_RewindByName")] static void RewindByName(Animation a, string n) { var s = A(a).State(n); if (s != null) s.Time = 0; }
    [Icall("UnityEngine.Animation::INTERNAL_CALL_Rewind")] static void Rewind(Animation a) { foreach (var s in A(a).States) s.Time = 0; }
    [Icall("UnityEngine.Animation::INTERNAL_CALL_Sample")] static void Sample(Animation a) => A(a).Sample();
    [Icall("UnityEngine.Animation::get_isPlaying")] static bool IsPlaying(Animation a) => A(a).Playing;
    [Icall("UnityEngine.Animation::IsPlaying")] static bool IsPlayingName(Animation a, string n) => A(a).State(n)?.Enabled ?? false;
    [Icall("UnityEngine.Animation::Play")] static bool Play(Animation a, string n, PlayMode m) => PlayByName(A(a), n, m);

    [Icall("UnityEngine.Animation::PlayDefaultAnimation")]
    static bool PlayDefault(Animation a, PlayMode m) {
        var x = A(a);
        if (x.DefaultClip == null) return false;
        string name = Registry.Get(x.DefaultClip)?.Name ?? "";
        if (x.State(name) == null) x.AddState(name, x.DefaultClip);
        return PlayByName(x, name, m);
    }

    [Icall("UnityEngine.Animation::CrossFade")]
    static void CrossFade(Animation a, string n, float fade, PlayMode m) {
        var x = A(a);
        var s = x.State(n);
        if (s == null) return;
        if (fade <= 0) { PlayByName(x, n, m); return; }
        foreach (var o in x.States) if (o != s && o.Enabled && (m == PlayMode.StopAll || o.Layer == s.Layer)) { o.TargetWeight = 0; o.FadeSpeed = -o.Weight / fade; }
        if (!s.Enabled) { s.Time = 0; s.Weight = 0; }
        s.Enabled = true;
        s.TargetWeight = 1;
        s.FadeSpeed = (1 - s.Weight) / fade;
    }

    [Icall("UnityEngine.Animation::Blend")]
    static void Blend(Animation a, string n, float w, float fade) {
        var s = A(a).State(n);
        if (s == null) return;
        s.Enabled = true;
        if (fade <= 0) { s.Weight = w; s.FadeSpeed = 0; } else { s.TargetWeight = w; s.FadeSpeed = (w - s.Weight) / fade; }
    }

    [Icall("UnityEngine.Animation::CrossFadeQueued")]
    static AnimationState? CrossFadeQueued(Animation a, string n, float fade, QueueMode q, PlayMode m) => Queue(a, n, fade, q, m);
    [Icall("UnityEngine.Animation::PlayQueued")]
    static AnimationState? PlayQueued(Animation a, string n, QueueMode q, PlayMode m) => Queue(a, n, 0, q, m);

    static AnimationState? Queue(Animation a, string n, float fade, QueueMode q, PlayMode m) {
        var x = A(a);
        if (q == QueueMode.PlayNow || !x.Playing) { PlayByName(x, n, m); return x.State(n)?.Managed; }
        x.Queued.Enqueue((n, fade, m));
        return x.State(n)?.Managed;
    }

    [Icall("UnityEngine.Animation::AddClip")]
    static void AddClip(Animation a, AnimationClip clip, string name, int first, int last, bool loopFrame) => A(a).AddState(name, clip);
    [Icall("UnityEngine.Animation::RemoveClip")] static void RemoveClip(Animation a, AnimationClip clip) => A(a).States.RemoveAll(s => s.ClipObj == clip);
    [Icall("UnityEngine.Animation::RemoveClip2")] static void RemoveClip2(Animation a, string n) => A(a).States.RemoveAll(s => s.Name == n);
    [Icall("UnityEngine.Animation::GetClipCount")] static int ClipCount(Animation a) => A(a).States.Count;
    [Icall("UnityEngine.Animation::INTERNAL_CALL_SyncLayer")] static void SyncLayer(Animation a, int l) { }
    [Icall("UnityEngine.Animation::GetState")] static AnimationState? GetState(Animation a, string n) => A(a).State(n)?.Managed;
    [Icall("UnityEngine.Animation::GetStateAtIndex")] static AnimationState? GetStateAt(Animation a, int i) { var x = A(a); return i < x.States.Count ? x.States[i].Managed : null; }
    [Icall("UnityEngine.Animation::GetStateCount")] static int StateCount(Animation a) => A(a).States.Count;
    [Icall("UnityEngine.Animation::get_animatePhysics")] static bool GetPhys(Animation a) => false;
    [Icall("UnityEngine.Animation::set_animatePhysics")] static void SetPhys(Animation a, bool v) { }
    [Icall("UnityEngine.Animation::get_animateOnlyIfVisible")] static bool GetVis(Animation a) => false;
    [Icall("UnityEngine.Animation::set_animateOnlyIfVisible")] static void SetVis(Animation a, bool v) { }
    [Icall("UnityEngine.Animation::get_cullingType")] static AnimationCullingType GetCull(Animation a) => AnimationCullingType.AlwaysAnimate;
    [Icall("UnityEngine.Animation::set_cullingType")] static void SetCull(Animation a, AnimationCullingType v) { }
    [Icall("UnityEngine.Animation::INTERNAL_get_localBounds")] static void GetLB(Animation a, ref Bounds b) => b = default;
    [Icall("UnityEngine.Animation::INTERNAL_set_localBounds")] static void SetLB(Animation a, ref Bounds b) { }

    [Icall("UnityEngine.AnimationState::get_enabled")] static bool GetEnabled(AnimationState s) => S(s).Enabled;
    [Icall("UnityEngine.AnimationState::set_enabled")] static void SetEnabled(AnimationState s, bool v) { var x = S(s); x.Enabled = v; if (v && x.Weight == 0) x.Weight = 1; }
    [Icall("UnityEngine.AnimationState::get_weight")] static float GetWeight(AnimationState s) => S(s).Weight;
    [Icall("UnityEngine.AnimationState::set_weight")] static void SetWeight(AnimationState s, float v) => S(s).Weight = v;
    [Icall("UnityEngine.AnimationState::get_wrapMode")] static WrapMode GetSWrap(AnimationState s) => S(s).Wrap;
    [Icall("UnityEngine.AnimationState::set_wrapMode")] static void SetSWrap(AnimationState s, WrapMode v) => S(s).Wrap = v;
    [Icall("UnityEngine.AnimationState::get_time")] static float GetTime(AnimationState s) => S(s).Time;
    [Icall("UnityEngine.AnimationState::set_time")] static void SetTime(AnimationState s, float v) => S(s).Time = v;
    [Icall("UnityEngine.AnimationState::get_normalizedTime")] static float GetNTime(AnimationState s) { var x = S(s); return x.Length > 0 ? x.Time / x.Length : 0; }
    [Icall("UnityEngine.AnimationState::set_normalizedTime")] static void SetNTime(AnimationState s, float v) { var x = S(s); x.Time = v * x.Length; }
    [Icall("UnityEngine.AnimationState::get_speed")] static float GetSpeed(AnimationState s) => S(s).Speed;
    [Icall("UnityEngine.AnimationState::set_speed")] static void SetSpeed(AnimationState s, float v) => S(s).Speed = v;
    [Icall("UnityEngine.AnimationState::get_normalizedSpeed")] static float GetNSpeed(AnimationState s) { var x = S(s); return x.Length > 0 ? x.Speed / x.Length : 0; }
    [Icall("UnityEngine.AnimationState::set_normalizedSpeed")] static void SetNSpeed(AnimationState s, float v) { var x = S(s); x.Speed = v * x.Length; }
    [Icall("UnityEngine.AnimationState::get_length")] static float GetLength(AnimationState s) => S(s).Length;
    [Icall("UnityEngine.AnimationState::get_layer")] static int GetLayer(AnimationState s) => S(s).Layer;
    [Icall("UnityEngine.AnimationState::set_layer")] static void SetLayer(AnimationState s, int v) => S(s).Layer = v;
    [Icall("UnityEngine.AnimationState::get_clip")] static AnimationClip? GetSClip(AnimationState s) => S(s).ClipObj;
    [Icall("UnityEngine.AnimationState::get_name")] static string GetName(AnimationState s) => S(s).Name;
    [Icall("UnityEngine.AnimationState::set_name")] static void SetName(AnimationState s, string v) => S(s).Name = v;
    [Icall("UnityEngine.AnimationState::AddMixingTransform")] static void AddMix(AnimationState s, Transform t, bool r) { }
    [Icall("UnityEngine.AnimationState::RemoveMixingTransform")] static void RemoveMix(AnimationState s, Transform t) { }
    [Icall("UnityEngine.AnimationState::get_blendMode")] static AnimationBlendMode GetBlend(AnimationState s) => AnimationBlendMode.Blend;
    [Icall("UnityEngine.AnimationState::set_blendMode")] static void SetBlend(AnimationState s, AnimationBlendMode v) { }

    // Animator (Mecanim): FF9 only touches it in a few places
    [Icall("UnityEngine.Animator::StringToHash")] static int Hash(string s) => s.GetHashCode();
    [Icall("UnityEngine.Animator::Play")] static void AnimatorPlay(Animator a, int h, int l, float t) => Log.Once("Animator.Play");
    [Icall("UnityEngine.Animator::Update")] static void AnimatorUpdate(Animator a, float dt) { }
    [Icall("UnityEngine.Animator::GetCurrentAnimatorStateInfo")] static AnimatorStateInfo StateInfo(Animator a, int l) => default;
}
