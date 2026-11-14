// Unity's script serialization (no type trees in this build, so the layout comes from the
// managed types themselves): which fields are serialized and how each is laid out.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace FF9Engine.Assets;

/// Resolves a PPtr (fileID, pathID) read in a given file to an object.
public interface IPPtrResolver {
    UObject? Resolve(SerializedFile file, int fileId, long pathId, Type expected);
}

public static class Fields {
    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    static readonly Dictionary<Type, FieldInfo[]> cache = new();
    public static bool Trace;

    /// Serialized fields of a script class in Unity's order: base classes first.
    public static FieldInfo[] Of(Type t) {
        if (cache.TryGetValue(t, out var r)) return r;
        var chain = new List<Type>();
        for (var c = t; c != null && !IsEngineBase(c); c = c.BaseType) chain.Add(c);
        chain.Reverse();
        var list = new List<FieldInfo>();
        foreach (var c in chain)
            foreach (var f in c.GetFields(Inst))
                if (IsSerialized(f)) list.Add(f);
        r = list.ToArray();
        cache[t] = r;
        return r;
    }

    static bool IsEngineBase(Type c) =>
        c == typeof(object) || c == typeof(MonoBehaviour) || c == typeof(ScriptableObject) ||
        c == typeof(Behaviour) || c == typeof(Component) || c == typeof(UObject) || c == typeof(ValueType);

    static bool IsSerialized(FieldInfo f) {
        if (f.IsStatic || f.IsInitOnly || f.IsLiteral || f.IsNotSerialized) return false;
        if (!f.IsPublic && !f.IsDefined(typeof(SerializeField), false)) return false;
        return IsSerializableType(f.FieldType, 0);
    }

    static readonly HashSet<Type> builtinStructs = new() {
        typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Rect), typeof(Quaternion), typeof(Matrix4x4),
        typeof(Color), typeof(Color32), typeof(LayerMask), typeof(Bounds), typeof(AnimationCurve), typeof(Gradient),
        typeof(RectOffset), typeof(GUIStyle),
    };

    public static bool IsSerializableType(Type t, int depth) {
        if (t.IsPrimitive) return t != typeof(IntPtr) && t != typeof(UIntPtr);
        if (t == typeof(string)) return true;
        if (t.IsEnum) return true;
        if (typeof(UObject).IsAssignableFrom(t)) return true;
        if (builtinStructs.Contains(t)) return true;
        if (t.IsArray) return t.GetArrayRank() == 1 && IsElementType(t.GetElementType()!, depth);
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return IsElementType(t.GetGenericArguments()[0], depth);
        return IsUserSerializable(t);
    }

    static bool IsElementType(Type e, int depth) =>
        !(e.IsArray || e.IsGenericType && e.GetGenericTypeDefinition() == typeof(List<>)) && IsSerializableType(e, depth + 1);

    static bool IsUserSerializable(Type t) {
        if (t.IsGenericType || t.IsAbstract || t.IsInterface || t.IsPointer) return false;
        if ((t.Attributes & TypeAttributes.Serializable) == 0) return false;
        string ns = t.Namespace ?? "";
        if (ns == "System" || ns.StartsWith("System.")) return false;
        if (ns.StartsWith("UnityEngine") && !ns.StartsWith("UnityEngine.Events") && t.Assembly == typeof(UObject).Assembly) return false;
        return true;
    }

    // ---- reading ----

    public sealed class Ctx {
        public SerializedFile File = null!;
        public IPPtrResolver Resolver = null!;
    }

    static readonly string? traceType = Environment.GetEnvironmentVariable("FF9_TRACE_FIELDS");

    public static void Read(object target, Type t, BinReader r, Ctx ctx, int depth = 0) {
        bool trace = Trace || traceType != null && t.Name == traceType;
        long start = r.Pos;
        foreach (var f in Of(t)) {
            // fields added by Memoria are absent from the original build's data: keep defaults
            if (depth == 0 && r.Pos >= r.End) break;
            long at = r.Pos;
            object? v = ReadValue(f.FieldType, r, ctx, depth);
            if (trace) Log.Info($"    {t.Name}.{f.Name} : {f.FieldType.Name} @{at - start} +{r.Pos - at} = {v}");
            f.SetValue(target, v);
        }
        if (trace) Log.Info($"    {t.Name}: {r.Pos - start} bytes, {r.End - r.Pos} left");
    }

    public static object? ReadValue(Type t, BinReader r, Ctx ctx, int depth, bool element = false) {
        if (t.IsEnum) {
            var u = Enum.GetUnderlyingType(t);
            return Enum.ToObject(t, ReadPrimitive(u, r, !element));
        }
        if (t.IsPrimitive) return ReadPrimitive(t, r, !element);
        if (t == typeof(string)) return r.String();
        if (typeof(UObject).IsAssignableFrom(t)) {
            var (fi, pi) = r.PPtr();
            return fi == 0 && pi == 0 ? null : ctx.Resolver.Resolve(ctx.File, fi, pi, t);
        }
        if (t.IsArray) {
            var e = t.GetElementType()!;
            int n = r.I32();
            if (e == typeof(byte)) { var b = r.Bytes(n); r.Align4(); return b; }
            var a = Array.CreateInstance(e, n);
            for (int i = 0; i < n; i++) a.SetValue(ReadValue(e, r, ctx, depth + 1, true), i);
            r.Align4();
            return a;
        }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) {
            var e = t.GetGenericArguments()[0];
            int n = r.I32();
            var l = (IList)Activator.CreateInstance(t, n)!;
            for (int i = 0; i < n; i++) l.Add(ReadValue(e, r, ctx, depth + 1, true));
            r.Align4();
            return l;
        }
        if (builtinStructs.Contains(t)) return ReadBuiltin(t, r, ctx);
        // [Serializable] class or struct: fields inline; Unity stops at depth 7
        if (depth > 7) return t.IsValueType ? Activator.CreateInstance(t) : null;
        object o = t.IsValueType ? Activator.CreateInstance(t)! : New(t);
        Read(o, t, r, ctx, depth + 1);
        return o;
    }

    static object New(Type t) {
        var c = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes);
        return c != null ? c.Invoke(null) : RuntimeHelpers.GetUninitializedObject(t);
    }

    static object ReadPrimitive(Type t, BinReader r, bool align) {
        // 1- and 2-byte values are followed by alignment to 4 (Unity 5 script serialization)
        switch (Type.GetTypeCode(t)) {
            case TypeCode.Boolean: { bool v = r.Bool(); if (align) r.Align4(); return v; }
            case TypeCode.Byte: { byte v = r.U8(); if (align) r.Align4(); return v; }
            case TypeCode.SByte: { sbyte v = r.I8(); if (align) r.Align4(); return v; }
            case TypeCode.Char: { char v = (char)r.U16(); if (align) r.Align4(); return v; }
            case TypeCode.Int16: { short v = r.I16(); if (align) r.Align4(); return v; }
            case TypeCode.UInt16: { ushort v = r.U16(); if (align) r.Align4(); return v; }
            case TypeCode.Int32: return r.I32();
            case TypeCode.UInt32: return r.U32();
            case TypeCode.Int64: return r.I64();
            case TypeCode.UInt64: return r.U64();
            case TypeCode.Single: return r.F32();
            case TypeCode.Double: return r.F64();
        }
        throw new NotSupportedException(t.FullName);
    }

    static object? ReadBuiltin(Type t, BinReader r, Ctx ctx) {
        if (t == typeof(Vector2)) return r.V2();
        if (t == typeof(Vector3)) return r.V3();
        if (t == typeof(Vector4)) return r.V4();
        if (t == typeof(Quaternion)) return r.Quat();
        if (t == typeof(Color)) return r.Color();
        if (t == typeof(Rect)) return r.Rect();
        if (t == typeof(Color32)) { uint v = r.U32(); return new Color32((byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24)); }
        if (t == typeof(LayerMask)) { var m = new LayerMask(); m.value = (int)r.U32(); return m; }
        if (t == typeof(Bounds)) { var c = r.V3(); var e = r.V3(); return new Bounds(c, e * 2); }
        if (t == typeof(Matrix4x4)) {
            var m = new Matrix4x4();
            unsafe { float* p = (float*)&m; for (int i = 0; i < 16; i++) p[i] = r.F32(); }
            return m;
        }
        if (t == typeof(RectOffset)) {
            int l = r.I32(), rt = r.I32(), tp = r.I32(), b = r.I32();
            var o = Native.RectOffsets.Create(l, rt, tp, b);
            return o;
        }
        if (t == typeof(AnimationCurve)) return Native.Curves.Read(r);
        if (t == typeof(Gradient)) return Native.Gradients.Read(r);
        if (t == typeof(GUIStyle)) return Native.GuiStyles.Read(r, ctx);
        throw new NotSupportedException(t.FullName);
    }
}
