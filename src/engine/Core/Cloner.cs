// Object.Instantiate: deep copy of a GameObject hierarchy (references inside the hierarchy are
// remapped to the copies, as Unity does), or a copy of a single asset.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using FF9Engine.Assets;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace FF9Engine.Core;

public static class Cloner {
    /// Per-class hooks to copy a component record's native state (renderers, cameras, ...).
    public static readonly Dictionary<Type, Func<NComponent, NComponent>> RecordCopiers = new();

    public static UObject Clone(UObject src, Vector3? pos, Quaternion? rot) {
        var n = Registry.Get(src) ?? throw new ArgumentException("The Object you want to instantiate is null.");
        NGameObject? srcGo = n switch { NGameObject g => g, NComponent c => c.GameObject, _ => null };
        if (srcGo == null) return CloneAsset(n);
        var map = new Dictionary<NObject, NObject>();
        var newBehaviours = new List<NMonoBehaviour>();
        var root = CopyTree(srcGo.Transform, null, map, newBehaviours);
        root.Parent = null;
        if (pos is { } p) root.Position = p;
        if (rot is { } r) root.Rotation = r;
        // MonoBehaviour fields, remapping references into the copied hierarchy
        var omap = new Dictionary<UObject, UObject>(ReferenceEqualityComparer.Instance);
        foreach (var kv in map) omap[kv.Key.Managed!] = kv.Value.Managed!;
        foreach (var kv in map)
            if (kv.Key is NMonoBehaviour smb) {
                var dmb = (NMonoBehaviour)kv.Value;
                foreach (var f in Fields.Of(smb.Managed!.GetType()))
                    f.SetValue(dmb.Managed, CopyValue(f.GetValue(smb.Managed), omap, 0));
            }
        foreach (var rm in Remappers) rm(map);
        foreach (var b in newBehaviours) Player.AddBehaviour(b);
        foreach (var b in newBehaviours) Player.Awaken(b);
        return omap[src];
    }

    static NTransform CopyTree(NTransform st, NTransform? parent, Dictionary<NObject, NObject> map, List<NMonoBehaviour> mbs) {
        var sg = st.GameObject;
        var g = new NGameObject {
            Name = sg.Name + (parent == null ? "(Clone)" : ""), Layer = sg.Layer, Tag = sg.Tag, ActiveSelf = sg.ActiveSelf,
            IsStatic = sg.IsStatic, Scene = Player.ActiveScene, ClassId = 1,
        };
        Registry.Add(g, (UObject)RuntimeHelpers.GetUninitializedObject(typeof(GameObject)));
        map[sg] = g;
        foreach (var sc in sg.Components) {
            NComponent c;
            UObject m = (UObject)RuntimeHelpers.GetUninitializedObject(sc.Managed!.GetType());
            if (sc is NMonoBehaviour smb) {
                var mb = new NMonoBehaviour { ExecutionOrder = smb.ExecutionOrder, Enabled = smb.Enabled };
                c = mb;
                mbs.Add(mb);
            } else if (sc is NTransform stt) {
                NTransform t = stt is NRectTransform srt
                    ? new NRectTransform { AnchorMin = srt.AnchorMin, AnchorMax = srt.AnchorMax, AnchoredPosition = srt.AnchoredPosition, SizeDelta = srt.SizeDelta, Pivot = srt.Pivot }
                    : new NTransform();
                t.LocalPosition = stt.LocalPosition; t.LocalRotation = stt.LocalRotation; t.LocalScale = stt.LocalScale;
                t.Parent = parent;
                parent?.Children.Add(t);
                c = t;
            } else {
                c = RecordCopiers.TryGetValue(sc.GetType(), out var cp) ? cp(sc) : (NComponent)CloneRecord(sc);
            }
            c.GameObject = g;
            c.Name = g.Name;
            c.ClassId = sc.ClassId;
            c.Enabled = sc.Enabled;
            c.Raw = sc.Raw;
            c.IsAsset = false;
            Registry.Add(c, m);
            g.Components.Add(c);
            map[sc] = c;
            if (c is NMonoBehaviour) Scripts.RunConstructor(m);
        }
        var tr = g.Transform;
        foreach (var ch in st.Children.ToArray()) CopyTree(ch, tr, map, mbs);
        return tr;
    }

    static readonly MethodInfo memberwise = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static object CloneRecord(object o) => memberwise.Invoke(o, null)!;

    static object? CopyValue(object? v, Dictionary<UObject, UObject> omap, int depth) {
        switch (v) {
            case null: return null;
            case string: return v;
            case UObject u: return omap.TryGetValue(u, out var m) ? m : u;
            case Array a: {
                var t = a.GetType().GetElementType()!;
                var r = Array.CreateInstance(t, a.Length);
                if (t.IsPrimitive) Array.Copy(a, r, a.Length);
                else for (int i = 0; i < a.Length; i++) r.SetValue(CopyValue(a.GetValue(i), omap, depth + 1), i);
                return r;
            }
            case IList l when v.GetType().IsGenericType: {
                var r = (IList)Activator.CreateInstance(v.GetType(), l.Count)!;
                foreach (var e in l) r.Add(CopyValue(e, omap, depth + 1));
                return r;
            }
        }
        var ty = v.GetType();
        if (ty.IsPrimitive || ty.IsEnum) return v;
        if (ty.Assembly == typeof(UObject).Assembly && ty.IsValueType) return v;     // Vector3 etc.
        if (ty == typeof(AnimationCurve) || ty == typeof(Gradient) || ty == typeof(RectOffset) || ty == typeof(GUIStyle)) return v;
        if (depth > 7) return ty.IsValueType ? v : null;
        object copy = ty.IsValueType ? v : CloneRecord(v);
        object boxed = copy;
        foreach (var f in Fields.Of(ty)) f.SetValue(boxed, CopyValue(f.GetValue(v), omap, depth + 1));
        return boxed;
    }

    static UObject CloneAsset(NObject n) {
        var copy = (NObject)CloneRecord(n);
        copy.Name = n.Name + "(Clone)";
        copy.IsAsset = false;
        copy.ForgetGpu();
        var m = (UObject)RuntimeHelpers.GetUninitializedObject(n.Managed!.GetType());
        Registry.Add(copy, m);
        if (n is NAsset a && a.Data is ICloneable c) ((NAsset)copy).Data = c.Clone();
        if (m is MonoBehaviour || m is ScriptableObject) {
            Scripts.RunConstructor(m);
            var omap = new Dictionary<UObject, UObject>(ReferenceEqualityComparer.Instance);
            foreach (var f in Fields.Of(m.GetType())) f.SetValue(m, CopyValue(f.GetValue(n.Managed), omap, 0));
        }
        AssetCloned?.Invoke(n, copy);
        return m;
    }

    public static Action<NObject, NObject>? AssetCloned;

    /// Fix-ups after a hierarchy copy (bone lists and other references held by native records).
    public static readonly List<Action<Dictionary<NObject, NObject>>> Remappers = new();
}
