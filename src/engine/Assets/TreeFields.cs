// Script fields read through a type tree (asset bundles carry them): values are decoded by the tree
// and assigned to managed fields by name, so classes that Memoria changed still read correctly.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace FF9Engine.Assets;

public static class TreeFields {
    sealed class PPtrV { public int FileId; public long PathId; }

    static readonly HashSet<string> baseFields = new() { "m_GameObject", "m_Enabled", "m_Script", "m_Name", "m_ObjectHideFlags", "m_PrefabParentObject", "m_PrefabInternal", "m_EditorHideFlags", "m_EditorClassIdentifier" };

    /// Reads the script part of a MonoBehaviour whose header (m_GameObject .. m_Name) was consumed.
    public static void ReadScript(object target, TypeTree.Node root, BinReader r, Fields.Ctx ctx) {
        var fields = FieldMap(target.GetType());
        foreach (var child in root.Children) {
            if (baseFields.Contains(child.Name)) continue;
            object? v = Decode(child, r);
            if (fields.TryGetValue(child.Name, out var f)) {
                try { f.SetValue(target, Convert(v, f.FieldType, ctx, 0)); }
                catch (Exception e) { Log.Once("treefield:" + target.GetType().Name + "." + child.Name, $"field {target.GetType().Name}.{child.Name}: {e.Message}"); }
            }
        }
    }

    static readonly Dictionary<Type, Dictionary<string, FieldInfo>> maps = new();

    static Dictionary<string, FieldInfo> FieldMap(Type t) {
        if (maps.TryGetValue(t, out var m)) return m;
        m = new();
        for (var c = t; c != null && c != typeof(object); c = c.BaseType)
            foreach (var f in c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                m.TryAdd(f.Name, f);
        maps[t] = m;
        return m;
    }

    static bool IsArrayNode(TypeTree.Node n, out TypeTree.Node? arr) {
        arr = null;
        if (n.IsArray) { arr = n; return true; }
        if (n.Children.Count == 1 && n.Children[0].IsArray) { arr = n.Children[0]; return true; }
        return false;
    }

    /// Decodes a node: primitives, strings, PPtr, arrays (List of values), structs (name -> value).
    public static object? Decode(TypeTree.Node n, BinReader r) {
        object? v;
        switch (n.Type) {
            case "bool": v = r.U8() != 0; break;
            case "SInt8": v = r.I8(); break;
            case "UInt8": case "char": v = r.U8(); break;
            case "SInt16": case "short": v = r.I16(); break;
            case "UInt16": case "unsigned short": v = r.U16(); break;
            case "SInt32": case "int": v = r.I32(); break;
            case "UInt32": case "unsigned int": case "Type*": v = r.U32(); break;
            case "SInt64": case "long long": v = r.I64(); break;
            case "UInt64": case "unsigned long long": case "FileSize": v = r.U64(); break;
            case "float": v = r.F32(); break;
            case "double": v = r.F64(); break;
            case "string": v = r.String(); return v;
            default:
                if (n.Type.StartsWith("PPtr<")) { v = new PPtrV { FileId = r.I32(), PathId = r.I64() }; break; }
                if (n.Type == "TypelessData") { int c = r.I32(); v = r.Bytes(c); break; }
                if (IsArrayNode(n, out var arr)) {
                    int count = r.I32();
                    var elem = arr!.Children[1];
                    if (elem.Type is "UInt8" or "char") { v = r.Bytes(count); if (arr.Align) r.Align4(); break; }
                    var list = new List<object?>(count);
                    for (int i = 0; i < count; i++) list.Add(Decode(elem, r));
                    if (arr.Align) r.Align4();
                    v = list;
                    break;
                }
                var d = new Dictionary<string, object?>();
                foreach (var c2 in n.Children) d[c2.Name] = Decode(c2, r);
                d["$type"] = n.Type;
                v = d;
                break;
        }
        if (n.Align) r.Align4();
        return v;
    }

    static float F(object? o) => o switch { float f => f, double d => (float)d, int i => i, uint u => u, _ => 0 };

    /// Converts a decoded value to a managed field's type.
    public static object? Convert(object? v, Type t, Fields.Ctx ctx, int depth) {
        if (v is PPtrV p) {
            if (!typeof(UObject).IsAssignableFrom(t)) return null;
            return p.FileId == 0 && p.PathId == 0 ? null : ctx.Resolver.Resolve(ctx.File, p.FileId, p.PathId, t);
        }
        if (t.IsEnum) return v == null ? Activator.CreateInstance(t) : Enum.ToObject(t, System.Convert.ToInt64(v));
        if (t.IsPrimitive) {
            if (v == null) return Activator.CreateInstance(t);
            if (t == typeof(bool)) return v is bool b ? b : System.Convert.ToInt64(v) != 0;
            if (t == typeof(char)) return (char)System.Convert.ToUInt16(v);
            return System.Convert.ChangeType(v, t, System.Globalization.CultureInfo.InvariantCulture);
        }
        if (t == typeof(string)) return v as string ?? "";
        if (t.IsArray) {
            var e = t.GetElementType()!;
            if (v is byte[] bytes) {
                if (e == typeof(byte)) return bytes;
                var a2 = Array.CreateInstance(e, bytes.Length);
                for (int i = 0; i < bytes.Length; i++) a2.SetValue(Convert(bytes[i], e, ctx, depth + 1), i);
                return a2;
            }
            var l = v as List<object?> ?? new();
            var a = Array.CreateInstance(e, l.Count);
            for (int i = 0; i < l.Count; i++) a.SetValue(Convert(l[i], e, ctx, depth + 1), i);
            return a;
        }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) {
            var e = t.GetGenericArguments()[0];
            var res = (IList)Activator.CreateInstance(t)!;
            if (v is byte[] bytes) foreach (var b in bytes) res.Add(Convert(b, e, ctx, depth + 1));
            else if (v is List<object?> l) foreach (var x in l) res.Add(Convert(x, e, ctx, depth + 1));
            return res;
        }
        var d = v as Dictionary<string, object?>;
        if (typeof(UObject).IsAssignableFrom(t)) return null;
        if (t == typeof(Vector2)) return d == null ? default(Vector2) : new Vector2(F(d["x"]), F(d["y"]));
        if (t == typeof(Vector3)) return d == null ? default(Vector3) : new Vector3(F(d["x"]), F(d["y"]), F(d["z"]));
        if (t == typeof(Vector4)) return d == null ? default(Vector4) : new Vector4(F(d["x"]), F(d["y"]), F(d["z"]), F(d["w"]));
        if (t == typeof(Quaternion)) return d == null ? new Quaternion(0, 0, 0, 1) : new Quaternion(F(d["x"]), F(d["y"]), F(d["z"]), F(d["w"]));
        if (t == typeof(Rect)) return d == null ? default(Rect) : new Rect(F(d["x"]), F(d["y"]), F(d["width"]), F(d["height"]));
        if (t == typeof(Color)) {
            if (d == null) return default(Color);
            if (d.ContainsKey("rgba")) { uint c = (uint)d["rgba"]!; return new Color((c & 255) / 255f, (c >> 8 & 255) / 255f, (c >> 16 & 255) / 255f, (c >> 24) / 255f); }
            return new Color(F(d["r"]), F(d["g"]), F(d["b"]), F(d["a"]));
        }
        if (t == typeof(Color32)) {
            if (d == null) return default(Color32);
            if (d.ContainsKey("rgba")) { uint c = (uint)d["rgba"]!; return new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24)); }
            return new Color32((byte)(F(d["r"]) * 255), (byte)(F(d["g"]) * 255), (byte)(F(d["b"]) * 255), (byte)(F(d["a"]) * 255));
        }
        if (t == typeof(LayerMask)) { var m = new LayerMask(); if (d != null && d.TryGetValue("m_Bits", out var bits)) m.value = (int)System.Convert.ToUInt32(bits); return m; }
        if (t == typeof(Bounds)) {
            if (d == null) return default(Bounds);
            var c = (Vector3)Convert(d["m_Center"], typeof(Vector3), ctx, depth)!; var e = (Vector3)Convert(d["m_Extent"], typeof(Vector3), ctx, depth)!;
            return new Bounds(c, new Vector3(e.x * 2, e.y * 2, e.z * 2));
        }
        if (t == typeof(Matrix4x4)) {
            var mm = new Matrix4x4();
            if (d != null) for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) mm[row, col] = F(d[$"e{row}{col}"]);
            return mm;
        }
        if (t == typeof(RectOffset)) return Native.RectOffsets.Create(d == null ? 0 : (int)d["m_Left"]!, d == null ? 0 : (int)d["m_Right"]!, d == null ? 0 : (int)d["m_Top"]!, d == null ? 0 : (int)d["m_Bottom"]!);
        if (t == typeof(AnimationCurve)) {
            var cd = new Native.CurveData();
            if (d != null && d["m_Curve"] is List<object?> keys)
                foreach (Dictionary<string, object?> k in keys)
                    cd.Keys.Add(new Keyframe(F(k["time"]), F(k["value"]), F(k["inSlope"]), F(k["outSlope"])));
            if (d != null) { cd.Pre = Native.Curves.FromInternal(System.Convert.ToInt32(d["m_PreInfinity"])); cd.Post = Native.Curves.FromInternal(System.Convert.ToInt32(d["m_PostInfinity"])); }
            var ac = (AnimationCurve)RuntimeHelpers.GetUninitializedObject(typeof(AnimationCurve));
            Native.Handles.SetPtr(ac, Native.Handles.New(cd));
            return ac;
        }
        if (t == typeof(Gradient)) {
            var g = (Gradient)RuntimeHelpers.GetUninitializedObject(typeof(Gradient));
            Native.Handles.SetPtr(g, Native.Handles.New(new Native.GradientData()));
            return g;
        }
        if (t == typeof(GUIStyle)) { Log.Once("guistyle-tree", "GUIStyle fields in bundles: defaults used"); return new GUIStyle(); }
        // [Serializable] class/struct: by field name
        if (depth > 8) return t.IsValueType ? Activator.CreateInstance(t) : null;
        object o = t.IsValueType ? Activator.CreateInstance(t)! : NewObj(t);
        if (d != null) {
            var map = FieldMap(t);
            foreach (var kv in d) {
                if (kv.Key == "$type" || !map.TryGetValue(kv.Key, out var f)) continue;
                try { f.SetValue(o, Convert(kv.Value, f.FieldType, ctx, depth + 1)); } catch { }
            }
        }
        return o;
    }

    static object NewObj(Type t) {
        var c = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes);
        try { return c != null ? c.Invoke(null) : RuntimeHelpers.GetUninitializedObject(t); }
        catch { return RuntimeHelpers.GetUninitializedObject(t); }
    }
}
