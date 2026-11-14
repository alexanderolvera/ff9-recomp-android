// UnityEngine.Transform / RectTransform internal calls.
using System;
using FF9Engine.Core;
using UnityEngine;

namespace FF9Engine.Icalls;

static class TransformIcalls {
    static NTransform T(Transform t) => Registry.Of<NTransform>(t);
    static Transform? M(NTransform? t) => t?.Managed as Transform;

    static void Changed(NTransform t) {
        t.MarkChanged();
        TransformEvents.Changed?.Invoke(t);
    }

    [Icall("UnityEngine.Transform::INTERNAL_get_position")] static void GetPos(Transform s, ref Vector3 v) => v = T(s).Position;
    [Icall("UnityEngine.Transform::INTERNAL_set_position")] static void SetPos(Transform s, ref Vector3 v) { var t = T(s); t.Position = v; Changed(t); }
    [Icall("UnityEngine.Transform::INTERNAL_get_localPosition")] static void GetLPos(Transform s, ref Vector3 v) => v = T(s).LocalPosition;
    [Icall("UnityEngine.Transform::INTERNAL_set_localPosition")] static void SetLPos(Transform s, ref Vector3 v) { var t = T(s); t.LocalPosition = v; Changed(t); }
    [Icall("UnityEngine.Transform::INTERNAL_get_localEulerAngles")] static void GetLEuler(Transform s, ref Vector3 v) => v = Core.M.ToEuler(T(s).LocalRotation);
    [Icall("UnityEngine.Transform::INTERNAL_set_localEulerAngles")] static void SetLEuler(Transform s, ref Vector3 v) { var t = T(s); t.LocalRotation = Core.M.Euler(v); Changed(t); }
    [Icall("UnityEngine.Transform::INTERNAL_get_rotation")] static void GetRot(Transform s, ref Quaternion v) => v = T(s).Rotation;
    [Icall("UnityEngine.Transform::INTERNAL_set_rotation")] static void SetRot(Transform s, ref Quaternion v) { var t = T(s); t.Rotation = Core.M.Normalize(v); Changed(t); }
    [Icall("UnityEngine.Transform::INTERNAL_get_localRotation")] static void GetLRot(Transform s, ref Quaternion v) => v = T(s).LocalRotation;
    [Icall("UnityEngine.Transform::INTERNAL_set_localRotation")] static void SetLRot(Transform s, ref Quaternion v) { var t = T(s); t.LocalRotation = Core.M.Normalize(v); Changed(t); }
    [Icall("UnityEngine.Transform::INTERNAL_get_localScale")] static void GetLScale(Transform s, ref Vector3 v) => v = T(s).LocalScale;
    [Icall("UnityEngine.Transform::INTERNAL_set_localScale")] static void SetLScale(Transform s, ref Vector3 v) { var t = T(s); t.LocalScale = v; Changed(t); }
    [Icall("UnityEngine.Transform::INTERNAL_get_lossyScale")] static void GetLossy(Transform s, ref Vector3 v) => v = T(s).LossyScale;
    [Icall("UnityEngine.Transform::INTERNAL_get_worldToLocalMatrix")] static void W2L(Transform s, ref Matrix4x4 v) => v = T(s).WorldToLocal;
    [Icall("UnityEngine.Transform::INTERNAL_get_localToWorldMatrix")] static void L2W(Transform s, ref Matrix4x4 v) => v = T(s).LocalToWorld;

    [Icall("UnityEngine.Transform::get_parentInternal")] static Transform? GetParent(Transform s) => M(T(s).Parent);

    [Icall("UnityEngine.Transform::set_parentInternal")]
    static void SetParentInternal(Transform s, Transform? p) => SetParent(s, p, true);

    [Icall("UnityEngine.Transform::SetParent")]
    static void SetParent(Transform s, Transform? p, bool worldPositionStays) {
        var t = T(s);
        var np = p == null ? null : T(p);
        if (np == t.Parent) return;
        bool wasActive = t.GameObject.ActiveInHierarchy;
        t.SetParent(np, worldPositionStays);
        TransformEvents.Changed?.Invoke(t);
        if (t.GameObject.ActiveInHierarchy != wasActive && !t.IsAsset) Player.HierarchyActivationChanged(t.GameObject);
        TransformEvents.ParentChanged?.Invoke(t);
    }

    [Icall("UnityEngine.Transform::get_root")] static Transform? Root(Transform s) => M(T(s).Root);
    [Icall("UnityEngine.Transform::get_childCount")]
    [Icall("UnityEngine.Transform::GetChildCount")] static int ChildCount(Transform s) => T(s).Children.Count;

    [Icall("UnityEngine.Transform::GetChild")]
    static Transform GetChild(Transform s, int i) {
        var t = T(s);
        if (i < 0 || i >= t.Children.Count) throw new UnityException("Transform child out of bounds");
        return M(t.Children[i])!;
    }

    [Icall("UnityEngine.Transform::DetachChildren")]
    static void Detach(Transform s) { foreach (var c in T(s).Children.ToArray()) c.SetParent(null, true); }

    [Icall("UnityEngine.Transform::SetAsFirstSibling")]
    static void First(Transform s) { var t = T(s); if (t.Parent is { } p) { p.Children.Remove(t); p.Children.Insert(0, t); TransformEvents.Changed?.Invoke(t); } }

    [Icall("UnityEngine.Transform::SetAsLastSibling")]
    static void Last(Transform s) { var t = T(s); if (t.Parent is { } p) { p.Children.Remove(t); p.Children.Add(t); TransformEvents.Changed?.Invoke(t); } }

    [Icall("UnityEngine.Transform::SetSiblingIndex")]
    static void SetSibling(Transform s, int i) {
        var t = T(s);
        if (t.Parent is not { } p) return;
        p.Children.Remove(t);
        p.Children.Insert(Math.Clamp(i, 0, p.Children.Count), t);
        TransformEvents.Changed?.Invoke(t);
    }

    [Icall("UnityEngine.Transform::GetSiblingIndex")]
    static int GetSibling(Transform s) { var t = T(s); return t.Parent?.Children.IndexOf(t) ?? 0; }

    [Icall("UnityEngine.Transform::Find")] static Transform? Find(Transform s, string n) => M(T(s).Find(n));
    [Icall("UnityEngine.Transform::IsChildOf")] static bool IsChildOf(Transform s, Transform p) => p != null && T(s).IsChildOf(T(p));
    [Icall("UnityEngine.Transform::get_hasChanged")] static bool HasChanged(Transform s) => T(s).HasChanged;
    [Icall("UnityEngine.Transform::set_hasChanged")] static void SetHasChanged(Transform s, bool v) => T(s).HasChanged = v;

    [Icall("UnityEngine.Transform::INTERNAL_CALL_TransformDirection")]
    static Vector3 TransformDirection(Transform s, ref Vector3 d) => Core.M.Rotate(T(s).Rotation, d);

    [Icall("UnityEngine.Transform::INTERNAL_CALL_InverseTransformDirection")]
    static Vector3 InverseTransformDirection(Transform s, ref Vector3 d) => Core.M.Rotate(Core.M.Inverse(T(s).Rotation), d);

    [Icall("UnityEngine.Transform::INTERNAL_CALL_TransformVector")]
    static Vector3 TransformVector(Transform s, ref Vector3 d) => Core.M.MulVector(T(s).LocalToWorld, d);

    [Icall("UnityEngine.Transform::INTERNAL_CALL_InverseTransformVector")]
    static Vector3 InverseTransformVector(Transform s, ref Vector3 d) => Core.M.MulVector(T(s).WorldToLocal, d);

    [Icall("UnityEngine.Transform::INTERNAL_CALL_TransformPoint")]
    static Vector3 TransformPoint(Transform s, ref Vector3 p) => Core.M.MulPoint(T(s).LocalToWorld, p);

    [Icall("UnityEngine.Transform::INTERNAL_CALL_InverseTransformPoint")]
    static Vector3 InverseTransformPoint(Transform s, ref Vector3 p) => Core.M.MulPoint(T(s).WorldToLocal, p);

    [Icall("UnityEngine.Transform::INTERNAL_CALL_RotateAroundInternal")]
    [Icall("UnityEngine.Transform::INTERNAL_CALL_RotateAround")]
    static void RotateAroundWorld(Transform s, ref Vector3 axis, float angle) {
        var t = T(s);
        t.Rotation = Core.M.Normalize(Core.M.Mul(Core.M.Axis(axis, angle), t.Rotation));
        Changed(t);
    }

    [Icall("UnityEngine.Transform::INTERNAL_CALL_RotateAroundLocal")]
    static void RotateAroundLocal(Transform s, ref Vector3 axis, float angle) {
        var t = T(s);
        t.LocalRotation = Core.M.Normalize(Core.M.Mul(t.LocalRotation, Core.M.Axis(axis, angle)));
        Changed(t);
    }

    [Icall("UnityEngine.Transform::INTERNAL_CALL_LookAt")]
    static void LookAt(Transform s, ref Vector3 target, ref Vector3 up) {
        var t = T(s);
        var p = t.Position;
        var f = new Vector3(target.x - p.x, target.y - p.y, target.z - p.z);
        t.Rotation = Maths.LookRotation(f, up);
        Changed(t);
    }

    // ---- RectTransform ----
    static NRectTransform R(RectTransform t) => Registry.Of<NRectTransform>(t);
    [Icall("UnityEngine.RectTransform::INTERNAL_get_anchorMin")] static void GAMin(RectTransform s, ref Vector2 v) => v = R(s).AnchorMin;
    [Icall("UnityEngine.RectTransform::INTERNAL_set_anchorMin")] static void SAMin(RectTransform s, ref Vector2 v) { R(s).AnchorMin = v; Changed(R(s)); }
    [Icall("UnityEngine.RectTransform::INTERNAL_get_anchorMax")] static void GAMax(RectTransform s, ref Vector2 v) => v = R(s).AnchorMax;
    [Icall("UnityEngine.RectTransform::INTERNAL_set_anchorMax")] static void SAMax(RectTransform s, ref Vector2 v) { R(s).AnchorMax = v; Changed(R(s)); }
    [Icall("UnityEngine.RectTransform::INTERNAL_get_anchoredPosition")] static void GAPos(RectTransform s, ref Vector2 v) => v = R(s).AnchoredPosition;
    [Icall("UnityEngine.RectTransform::INTERNAL_set_anchoredPosition")] static void SAPos(RectTransform s, ref Vector2 v) { R(s).AnchoredPosition = v; Changed(R(s)); }
    [Icall("UnityEngine.RectTransform::INTERNAL_get_sizeDelta")] static void GSize(RectTransform s, ref Vector2 v) => v = R(s).SizeDelta;
    [Icall("UnityEngine.RectTransform::INTERNAL_set_sizeDelta")] static void SSize(RectTransform s, ref Vector2 v) { R(s).SizeDelta = v; Changed(R(s)); }
    [Icall("UnityEngine.RectTransform::INTERNAL_get_pivot")] static void GPivot(RectTransform s, ref Vector2 v) => v = R(s).Pivot;
    [Icall("UnityEngine.RectTransform::INTERNAL_set_pivot")] static void SPivot(RectTransform s, ref Vector2 v) { R(s).Pivot = v; Changed(R(s)); }

    [Icall("UnityEngine.RectTransform::INTERNAL_get_rect")]
    static void GetRect(RectTransform s, ref Rect v) {
        var r = R(s);
        Vector2 parentSize = r.Parent is NRectTransform pr ? Size(pr) : new Vector2(0, 0);
        var size = new Vector2(parentSize.x * (r.AnchorMax.x - r.AnchorMin.x) + r.SizeDelta.x,
                               parentSize.y * (r.AnchorMax.y - r.AnchorMin.y) + r.SizeDelta.y);
        v = new Rect(-r.Pivot.x * size.x, -r.Pivot.y * size.y, size.x, size.y);
    }

    static Vector2 Size(NRectTransform r) {
        Vector2 ps = r.Parent is NRectTransform pr ? Size(pr) : new Vector2(0, 0);
        return new Vector2(ps.x * (r.AnchorMax.x - r.AnchorMin.x) + r.SizeDelta.x, ps.y * (r.AnchorMax.y - r.AnchorMin.y) + r.SizeDelta.y);
    }
}

public static class TransformEvents {
    public static Action<NTransform>? Changed;
    public static Action<NTransform>? ParentChanged;
}

public static class Maths {
    public static Quaternion LookRotation(Vector3 forward, Vector3 up) {
        float fl = MathF.Sqrt(forward.x * forward.x + forward.y * forward.y + forward.z * forward.z);
        if (fl < 1e-6f) return new Quaternion(0, 0, 0, 1);
        var f = new Vector3(forward.x / fl, forward.y / fl, forward.z / fl);
        var r = Cross(up, f);
        float rl = MathF.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z);
        if (rl < 1e-6f) r = Cross(new Vector3(1, 0, 0), f); // up parallel to forward
        rl = MathF.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z);
        r = new Vector3(r.x / rl, r.y / rl, r.z / rl);
        var u = Cross(f, r);
        // rotation matrix columns r, u, f -> quaternion
        float m00 = r.x, m01 = u.x, m02 = f.x, m10 = r.y, m11 = u.y, m12 = f.y, m20 = r.z, m21 = u.z, m22 = f.z;
        float tr = m00 + m11 + m22;
        Quaternion q;
        if (tr > 0) { float s = MathF.Sqrt(tr + 1) * 2; q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s); }
        else if (m00 > m11 && m00 > m22) { float s = MathF.Sqrt(1 + m00 - m11 - m22) * 2; q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s); }
        else if (m11 > m22) { float s = MathF.Sqrt(1 + m11 - m00 - m22) * 2; q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s); }
        else { float s = MathF.Sqrt(1 + m22 - m00 - m11) * 2; q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s); }
        return Core.M.Normalize(q);
    }

    public static Vector3 Cross(Vector3 a, Vector3 b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
}
