// Managed classes that wrap a native pointer (m_Ptr as their first field: RectOffset,
// AnimationCurve, Gradient, GUIStyle, ...): m_Ptr holds an id into a table of C# objects.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FF9Engine.Assets;
using FF9Engine.Scripting;
using UnityEngine;

namespace FF9Engine.Native;

public static class Handles {
    static readonly List<object?> table = new() { null };
    static readonly Stack<int> free = new();
    static readonly object gate = new();

    public static IntPtr New(object data) {
        lock (gate) {
            int id;
            if (free.Count > 0) { id = free.Pop(); table[id] = data; }
            else { id = table.Count; table.Add(data); }
            return id;
        }
    }

    public static void Free(IntPtr p) {
        lock (gate) {
            int id = (int)p;
            if (id > 0 && id < table.Count && table[id] != null) { table[id] = null; free.Push(id); }
        }
    }

    public static T Get<T>(IntPtr p) where T : class {
        lock (gate) return (T)table[(int)p]!;
    }

    public static IntPtr Ptr(object managed) => Unsafe.As<PtrLayout>(managed).Ptr;
    public static void SetPtr(object managed, IntPtr p) => Unsafe.As<PtrLayout>(managed).Ptr = p;

    public static T Data<T>(object managed) where T : class, new() {
        var p = Ptr(managed);
        if (p == IntPtr.Zero) { var d = new T(); SetPtr(managed, New(d)); return d; }
        return Get<T>(p);
    }
}

public sealed class RectOffsetData { public int Left, Right, Top, Bottom; }

public static class RectOffsets {
    public static RectOffset Create(int l, int r, int t, int b) {
        var o = (RectOffset)RuntimeHelpers.GetUninitializedObject(typeof(RectOffset));
        Handles.SetPtr(o, Handles.New(new RectOffsetData { Left = l, Right = r, Top = t, Bottom = b }));
        return o;
    }

    static RectOffsetData D(RectOffset s) => Handles.Data<RectOffsetData>(s);
    [Icall("UnityEngine.RectOffset::Init")] static void Init(RectOffset s) => Handles.SetPtr(s, Handles.New(new RectOffsetData()));
    [Icall("UnityEngine.RectOffset::Cleanup")] static void Cleanup(RectOffset s) { }
    [Icall("UnityEngine.RectOffset::get_left")] static int L(RectOffset s) => D(s).Left;
    [Icall("UnityEngine.RectOffset::set_left")] static void SL(RectOffset s, int v) => D(s).Left = v;
    [Icall("UnityEngine.RectOffset::get_right")] static int R(RectOffset s) => D(s).Right;
    [Icall("UnityEngine.RectOffset::set_right")] static void SR(RectOffset s, int v) => D(s).Right = v;
    [Icall("UnityEngine.RectOffset::get_top")] static int T(RectOffset s) => D(s).Top;
    [Icall("UnityEngine.RectOffset::set_top")] static void ST(RectOffset s, int v) => D(s).Top = v;
    [Icall("UnityEngine.RectOffset::get_bottom")] static int B(RectOffset s) => D(s).Bottom;
    [Icall("UnityEngine.RectOffset::set_bottom")] static void SB(RectOffset s, int v) => D(s).Bottom = v;
    [Icall("UnityEngine.RectOffset::get_horizontal")] static int H(RectOffset s) => D(s).Left + D(s).Right;
    [Icall("UnityEngine.RectOffset::get_vertical")] static int V(RectOffset s) => D(s).Top + D(s).Bottom;

    [Icall("UnityEngine.RectOffset::INTERNAL_CALL_Add")]
    static Rect Add(RectOffset s, ref Rect r) { var d = D(s); return new Rect(r.x - d.Left, r.y - d.Top, r.width + d.Left + d.Right, r.height + d.Top + d.Bottom); }

    [Icall("UnityEngine.RectOffset::INTERNAL_CALL_Remove")]
    static Rect Remove(RectOffset s, ref Rect r) { var d = D(s); return new Rect(r.x + d.Left, r.y + d.Top, r.width - d.Left - d.Right, r.height - d.Top - d.Bottom); }
}

public sealed class CurveData {
    public List<Keyframe> Keys = new();
    public WrapMode Pre = WrapMode.ClampForever, Post = WrapMode.ClampForever;

    public float Evaluate(float t) {
        int n = Keys.Count;
        if (n == 0) return 0;
        if (n == 1) return Keys[0].value;
        float t0 = Keys[0].time, t1 = Keys[n - 1].time;
        t = Wrap(t, t0, t1, t < t0 ? Pre : Post);
        if (t <= t0) return Keys[0].value;
        if (t >= t1) return Keys[n - 1].value;
        int i = 1;
        while (Keys[i].time < t) i++;
        var a = Keys[i - 1]; var b = Keys[i];
        float dt = b.time - a.time;
        if (dt <= 0) return b.value;
        if (float.IsInfinity(a.outTangent) || float.IsInfinity(b.inTangent)) return a.value;
        float s = (t - a.time) / dt, s2 = s * s, s3 = s2 * s;
        float h00 = 2 * s3 - 3 * s2 + 1, h10 = s3 - 2 * s2 + s, h01 = -2 * s3 + 3 * s2, h11 = s3 - s2;
        return h00 * a.value + h10 * dt * a.outTangent + h01 * b.value + h11 * dt * b.inTangent;
    }

    static float Wrap(float t, float t0, float t1, WrapMode m) {
        float len = t1 - t0;
        if (len <= 0) return t0;
        switch (m) {
            case WrapMode.Loop: { float x = (t - t0) % len; return t0 + (x < 0 ? x + len : x); }
            case WrapMode.PingPong: {
                float x = (t - t0) % (2 * len); if (x < 0) x += 2 * len;
                return t0 + (x > len ? 2 * len - x : x);
            }
            default: return t;
        }
    }
}

public static class Curves {
    public static AnimationCurve Read(BinReader r) {
        var d = new CurveData();
        int n = r.I32();
        for (int i = 0; i < n; i++) {
            var k = new Keyframe(r.F32(), r.F32(), r.F32(), r.F32());
            d.Keys.Add(k);
        }
        d.Pre = (WrapMode)r.I32();
        d.Post = (WrapMode)r.I32();
        var c = (AnimationCurve)RuntimeHelpers.GetUninitializedObject(typeof(AnimationCurve));
        Handles.SetPtr(c, Handles.New(d));
        return c;
    }

    static CurveData D(AnimationCurve s) => Handles.Data<CurveData>(s);
    [Icall("UnityEngine.AnimationCurve::Init")]
    static void Init(AnimationCurve s, Keyframe[] keys) {
        var d = new CurveData();
        if (keys != null) d.Keys.AddRange(keys);
        Handles.SetPtr(s, Handles.New(d));
    }
    [Icall("UnityEngine.AnimationCurve::Cleanup")] static void Cleanup(AnimationCurve s) { Handles.Free(Handles.Ptr(s)); Handles.SetPtr(s, IntPtr.Zero); }
    [Icall("UnityEngine.AnimationCurve::Evaluate")] static float Evaluate(AnimationCurve s, float t) => D(s).Evaluate(t);
    [Icall("UnityEngine.AnimationCurve::get_length")] static int Length(AnimationCurve s) => D(s).Keys.Count;
    [Icall("UnityEngine.AnimationCurve::GetKeys")] static Keyframe[] GetKeys(AnimationCurve s) => D(s).Keys.ToArray();
    [Icall("UnityEngine.AnimationCurve::GetKey_Internal")] static Keyframe GetKey(AnimationCurve s, int i) => D(s).Keys[i];
    [Icall("UnityEngine.AnimationCurve::SetKeys")] static void SetKeys(AnimationCurve s, Keyframe[] k) { D(s).Keys = new List<Keyframe>(k); }
    [Icall("UnityEngine.AnimationCurve::RemoveKey")] static void RemoveKey(AnimationCurve s, int i) => D(s).Keys.RemoveAt(i);
    [Icall("UnityEngine.AnimationCurve::get_preWrapMode")] static WrapMode Pre(AnimationCurve s) => D(s).Pre;
    [Icall("UnityEngine.AnimationCurve::set_preWrapMode")] static void SPre(AnimationCurve s, WrapMode m) => D(s).Pre = m;
    [Icall("UnityEngine.AnimationCurve::get_postWrapMode")] static WrapMode Post(AnimationCurve s) => D(s).Post;
    [Icall("UnityEngine.AnimationCurve::set_postWrapMode")] static void SPost(AnimationCurve s, WrapMode m) => D(s).Post = m;

    static int Insert(CurveData d, Keyframe k) {
        int i = 0;
        while (i < d.Keys.Count && d.Keys[i].time < k.time) i++;
        if (i < d.Keys.Count && d.Keys[i].time == k.time) return -1;
        d.Keys.Insert(i, k);
        return i;
    }

    [Icall("UnityEngine.AnimationCurve::AddKey")] static int AddKey(AnimationCurve s, float t, float v) => Insert(D(s), new Keyframe(t, v));
    [Icall("UnityEngine.AnimationCurve::INTERNAL_CALL_AddKey_Internal")] static int AddKeyI(AnimationCurve s, ref Keyframe k) => Insert(D(s), k);

    [Icall("UnityEngine.AnimationCurve::INTERNAL_CALL_MoveKey")]
    static int MoveKey(AnimationCurve s, int i, ref Keyframe k) { var d = D(s); d.Keys.RemoveAt(i); return Insert(d, k); }

    [Icall("UnityEngine.AnimationCurve::SmoothTangents")] static void Smooth(AnimationCurve s, int i, float w) { }
}

public sealed class GradientData {
    public GradientColorKey[] Colors = { new(Color.white, 0), new(Color.white, 1) };
    public GradientAlphaKey[] Alphas = { new(1, 0), new(1, 1) };
}

public static class Gradients {
    public static Gradient Read(BinReader r) {
        var cols = new Color[8];
        for (int i = 0; i < 8; i++) { uint v = r.U32(); cols[i] = new Color((v & 255) / 255f, (v >> 8 & 255) / 255f, (v >> 16 & 255) / 255f, (v >> 24) / 255f); }
        var ct = new ushort[8]; var at = new ushort[8];
        for (int i = 0; i < 8; i++) ct[i] = r.U16();
        for (int i = 0; i < 8; i++) at[i] = r.U16();
        int nc = r.U8(), na = r.U8();
        r.Align4();
        var d = new GradientData { Colors = new GradientColorKey[nc], Alphas = new GradientAlphaKey[na] };
        for (int i = 0; i < nc; i++) d.Colors[i] = new GradientColorKey(cols[i], ct[i] / 65535f);
        for (int i = 0; i < na; i++) d.Alphas[i] = new GradientAlphaKey(cols[i].a, at[i] / 65535f);
        var g = (Gradient)RuntimeHelpers.GetUninitializedObject(typeof(Gradient));
        Handles.SetPtr(g, Handles.New(d));
        return g;
    }

    static GradientData D(Gradient g) => Handles.Data<GradientData>(g);
    [Icall("UnityEngine.Gradient::Init")] static void Init(Gradient g) => Handles.SetPtr(g, Handles.New(new GradientData()));
    [Icall("UnityEngine.Gradient::Cleanup")] static void Cleanup(Gradient g) { }
    [Icall("UnityEngine.Gradient::get_colorKeys")] static GradientColorKey[] GetC(Gradient g) => D(g).Colors;
    [Icall("UnityEngine.Gradient::set_colorKeys")] static void SetC(Gradient g, GradientColorKey[] k) => D(g).Colors = k;
    [Icall("UnityEngine.Gradient::get_alphaKeys")] static GradientAlphaKey[] GetA(Gradient g) => D(g).Alphas;
    [Icall("UnityEngine.Gradient::set_alphaKeys")] static void SetA(Gradient g, GradientAlphaKey[] k) => D(g).Alphas = k;
    [Icall("UnityEngine.Gradient::SetKeys")] static void SetKeys(Gradient g, GradientColorKey[] c, GradientAlphaKey[] a) { D(g).Colors = c; D(g).Alphas = a; }

    [Icall("UnityEngine.Gradient::Evaluate")]
    static Color Evaluate(Gradient g, float t) {
        var d = D(g);
        Color c = Sample(d.Colors.Length, i => d.Colors[i].time, i => d.Colors[i].color, t);
        float a = Sample(d.Alphas.Length, i => d.Alphas[i].time, i => new Color(d.Alphas[i].alpha, 0, 0, 0), t).r;
        return new Color(c.r, c.g, c.b, a);
    }

    static Color Sample(int n, Func<int, float> time, Func<int, Color> val, float t) {
        if (n == 0) return new Color(1, 1, 1, 1);
        if (t <= time(0)) return val(0);
        for (int i = 1; i < n; i++)
            if (t <= time(i)) {
                float s = (t - time(i - 1)) / Math.Max(1e-6f, time(i) - time(i - 1));
                Color a = val(i - 1), b = val(i);
                return new Color(a.r + (b.r - a.r) * s, a.g + (b.g - a.g) * s, a.b + (b.b - a.b) * s, a.a + (b.a - a.a) * s);
            }
        return val(n - 1);
    }
}

public static class GuiStyles {
    public static GUIStyle Read(BinReader r, Fields.Ctx ctx) =>
        throw new NotSupportedException("GUIStyle in serialized data (IMGUI skin): not implemented yet");
}
