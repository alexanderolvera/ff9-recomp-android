// Colliders and ray casts (NGUI's UICamera picks UI elements with Physics.RaycastAll against
// BoxColliders). No dynamics: FF9 does not simulate rigid bodies.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using FF9Engine.Assets;
using FF9Engine.Core;
using UnityEngine;

namespace FF9Engine.Physics;

public sealed class NCollider : NComponent {
    public enum Kind { Box, Sphere, Capsule, Mesh, Box2D, Circle2D, Other }
    public Kind Shape;
    public Vector3 Center, Size = new(1, 1, 1);
    public float Radius = 0.5f, Height = 2;
    public int Direction = 1;
    public bool IsTrigger;
    public Vector2 Offset2D;
}

[StructLayout(LayoutKind.Sequential)]
public struct Hit {
    public Vector3 Point, Normal;
    public int FaceID;
    public float Distance;
    public Vector2 UV;
    public Collider? Collider;
}

public static class Colliders {
    public static readonly List<NCollider> All = new();

    public static void Register() {
        void Reg(int cid, NCollider.Kind k) => Components.Factories[cid] = () => { var c = new NCollider { Shape = k }; All.Add(c); return c; };
        Reg(65, NCollider.Kind.Box); Reg(135, NCollider.Kind.Sphere); Reg(136, NCollider.Kind.Capsule); Reg(64, NCollider.Kind.Mesh);
        Reg(61, NCollider.Kind.Box2D); Reg(58, NCollider.Kind.Circle2D);
        // Collider (5.2): gameObject, material, isTrigger, enabled, then the shape
        SceneBuilder.ComponentReaders[65] = (c, r, b) => { var x = Base((NCollider)c, r); x.Size = r.V3(); x.Center = r.V3(); };
        SceneBuilder.ComponentReaders[135] = (c, r, b) => { var x = Base((NCollider)c, r); x.Radius = r.F32(); x.Center = r.V3(); };
        SceneBuilder.ComponentReaders[136] = (c, r, b) => { var x = Base((NCollider)c, r); x.Radius = r.F32(); x.Height = r.F32(); x.Direction = r.I32(); x.Center = r.V3(); };
        SceneBuilder.ComponentReaders[64] = (c, r, b) => { Base((NCollider)c, r); };
        Cloner.RecordCopiers[typeof(NCollider)] = c => { var n = (NCollider)Cloner.CloneRecord(c); All.Add(n); return n; };
    }

    static NCollider Base(NCollider c, BinReader r) {
        r.PPtr(); r.PPtr();
        c.IsTrigger = r.Bool(); c.Enabled = r.Bool(); r.Align4();
        return c;
    }

    static NCollider C(Collider c) => Registry.Of<NCollider>(c);

    [Icall("UnityEngine.Collider::get_enabled")] static bool GetEnabled(Collider c) => C(c).Enabled;
    [Icall("UnityEngine.Collider::set_enabled")] static void SetEnabled(Collider c, bool v) => C(c).Enabled = v;
    [Icall("UnityEngine.Collider::get_isTrigger")] static bool GetTrigger(Collider c) => C(c).IsTrigger;
    [Icall("UnityEngine.Collider::set_isTrigger")] static void SetTrigger(Collider c, bool v) => C(c).IsTrigger = v;
    [Icall("UnityEngine.Collider::get_attachedRigidbody")] static Rigidbody? Rb(Collider c) => null;
    [Icall("UnityEngine.Collider::INTERNAL_get_bounds")] static void Bounds(Collider c, ref Bounds b) => b = WorldBounds(C(c));
    [Icall("UnityEngine.BoxCollider::INTERNAL_get_center")] static void GetCenter(BoxCollider c, ref Vector3 v) => v = C(c).Center;
    [Icall("UnityEngine.BoxCollider::INTERNAL_set_center")] static void SetCenter(BoxCollider c, ref Vector3 v) => C(c).Center = v;
    [Icall("UnityEngine.BoxCollider::INTERNAL_get_size")] static void GetSize(BoxCollider c, ref Vector3 v) => v = C(c).Size;
    [Icall("UnityEngine.BoxCollider::INTERNAL_set_size")] static void SetSize(BoxCollider c, ref Vector3 v) => C(c).Size = v;
    [Icall("UnityEngine.SphereCollider::INTERNAL_get_center")] static void GetSCenter(SphereCollider c, ref Vector3 v) => v = C(c).Center;
    [Icall("UnityEngine.SphereCollider::INTERNAL_set_center")] static void SetSCenter(SphereCollider c, ref Vector3 v) => C(c).Center = v;
    [Icall("UnityEngine.SphereCollider::get_radius")] static float GetRadius(SphereCollider c) => C(c).Radius;
    [Icall("UnityEngine.SphereCollider::set_radius")] static void SetRadius(SphereCollider c, float v) => C(c).Radius = v;
    [Icall("UnityEngine.Collider2D::set_isTrigger")] static void SetTrigger2D(Collider2D c, bool v) => C2(c).IsTrigger = v;
    [Icall("UnityEngine.Collider2D::INTERNAL_set_offset")] static void SetOffset2D(Collider2D c, ref Vector2 v) => C2(c).Offset2D = v;
    static NCollider C2(Collider2D c) => Registry.Of<NCollider>(c);
    [Icall("UnityEngine.Rigidbody::set_useGravity")] static void Gravity(Rigidbody r, bool v) { }
    [Icall("UnityEngine.Rigidbody::set_isKinematic")] static void Kinematic(Rigidbody r, bool v) { }
    [Icall("UnityEngine.Rigidbody::set_freezeRotation")] static void Freeze(Rigidbody r, bool v) { }

    static Bounds WorldBounds(NCollider c) {
        var m = c.GameObject.Transform.LocalToWorld;
        var ctr = M.MulPoint(m, c.Center);
        Vector3 half = c.Shape == NCollider.Kind.Sphere ? new Vector3(c.Radius, c.Radius, c.Radius) : new Vector3(c.Size.x / 2, c.Size.y / 2, c.Size.z / 2);
        var ax = M.MulVector(m, new Vector3(half.x, 0, 0)); var ay = M.MulVector(m, new Vector3(0, half.y, 0)); var az = M.MulVector(m, new Vector3(0, 0, half.z));
        var e = new Vector3(Math.Abs(ax.x) + Math.Abs(ay.x) + Math.Abs(az.x), Math.Abs(ax.y) + Math.Abs(ay.y) + Math.Abs(az.y), Math.Abs(ax.z) + Math.Abs(ay.z) + Math.Abs(az.z));
        return new Bounds(ctr, new Vector3(e.x * 2, e.y * 2, e.z * 2));
    }

    /// Ray against a collider in its local space (box: slab test; sphere: analytic).
    public static bool Raycast(NCollider c, Vector3 o, Vector3 d, float max, out Hit hit) {
        hit = default;
        var w2l = c.GameObject.Transform.WorldToLocal;
        var lo = M.MulPoint(w2l, o);
        var ld = M.MulVector(w2l, d);
        float t;
        Vector3 n;
        if (c.Shape is NCollider.Kind.Sphere or NCollider.Kind.Circle2D) {
            var oc = new Vector3(lo.x - c.Center.x, lo.y - c.Center.y, lo.z - c.Center.z);
            float a = ld.x * ld.x + ld.y * ld.y + ld.z * ld.z, bq = oc.x * ld.x + oc.y * ld.y + oc.z * ld.z, cq = oc.x * oc.x + oc.y * oc.y + oc.z * oc.z - c.Radius * c.Radius;
            float disc = bq * bq - a * cq;
            if (disc < 0) return false;
            t = (-bq - MathF.Sqrt(disc)) / a;
            if (t < 0) t = (-bq + MathF.Sqrt(disc)) / a;
            if (t < 0) return false;
            var p = new Vector3(oc.x + ld.x * t, oc.y + ld.y * t, oc.z + ld.z * t);
            n = p;
        } else {
            Vector3 h = new(c.Size.x / 2, c.Size.y / 2, Math.Max(c.Size.z / 2, c.Shape == NCollider.Kind.Box2D ? 1e-3f : 0));
            float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
            int axis = 0;
            float[] oo = { lo.x - c.Center.x, lo.y - c.Center.y, lo.z - c.Center.z }, dd = { ld.x, ld.y, ld.z }, hh = { h.x, h.y, h.z };
            for (int i = 0; i < 3; i++) {
                if (MathF.Abs(dd[i]) < 1e-12f) { if (oo[i] < -hh[i] || oo[i] > hh[i]) return false; continue; }
                float t1 = (-hh[i] - oo[i]) / dd[i], t2 = (hh[i] - oo[i]) / dd[i];
                if (t1 > t2) (t1, t2) = (t2, t1);
                if (t1 > tmin) { tmin = t1; axis = i; }
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) return false;
            }
            if (tmax < 0) return false;
            t = tmin >= 0 ? tmin : tmax;
            n = axis == 0 ? new Vector3(-MathF.Sign(dd[0]), 0, 0) : axis == 1 ? new Vector3(0, -MathF.Sign(dd[1]), 0) : new Vector3(0, 0, -MathF.Sign(dd[2]));
        }
        var lp = new Vector3(lo.x + ld.x * t, lo.y + ld.y * t, lo.z + ld.z * t);
        var wp = M.MulPoint(c.GameObject.Transform.LocalToWorld, lp);
        float dist = MathF.Sqrt((wp.x - o.x) * (wp.x - o.x) + (wp.y - o.y) * (wp.y - o.y) + (wp.z - o.z) * (wp.z - o.z));
        if (dist > max) return false;
        hit.Point = wp;
        hit.Normal = M.MulVector(c.GameObject.Transform.LocalToWorld, n);
        hit.Distance = dist;
        hit.Collider = c.Managed as Collider;
        return true;
    }

    static IEnumerable<NCollider> Live(int mask, QueryTriggerInteraction q) {
        All.RemoveAll(c => c.Managed == null || Registry.Get(c.Managed) != c);
        foreach (var c in All) {
            if (!c.Enabled || c.IsAsset || !c.GameObject.ActiveInHierarchy) continue;
            if ((mask & (1 << c.GameObject.Layer)) == 0) continue;
            if (c.IsTrigger && q == QueryTriggerInteraction.Ignore) continue;
            if (c.Shape is NCollider.Kind.Box2D or NCollider.Kind.Circle2D) continue;
            yield return c;
        }
    }

    static Vector3 Norm(Vector3 v) { float l = MathF.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z); return l < 1e-12f ? v : new Vector3(v.x / l, v.y / l, v.z / l); }

    [Icall("UnityEngine.Physics::INTERNAL_CALL_RaycastAll")]
    static Hit[] RaycastAll(ref Vector3 o, ref Vector3 d, float max, int mask, QueryTriggerInteraction q) {
        var dn = Norm(d);
        var l = new List<Hit>();
        foreach (var c in Live(mask, q)) if (Raycast(c, o, dn, max, out var h)) l.Add(h);
        return l.ToArray();
    }

    [Icall("UnityEngine.Physics::INTERNAL_CALL_Internal_RaycastTest")]
    static bool RaycastTest(ref Vector3 o, ref Vector3 d, float max, int mask, QueryTriggerInteraction q) {
        var dn = Norm(d);
        foreach (var c in Live(mask, q)) if (Raycast(c, o, dn, max, out _)) return true;
        return false;
    }

    [Icall("UnityEngine.Physics::INTERNAL_CALL_Internal_Raycast")]
    static bool RaycastOne(ref Vector3 o, ref Vector3 d, out Hit hit, float max, int mask, QueryTriggerInteraction q) {
        var dn = Norm(d);
        hit = default;
        bool any = false;
        foreach (var c in Live(mask, q))
            if (Raycast(c, o, dn, max, out var h) && (!any || h.Distance < hit.Distance)) { hit = h; any = true; }
        return any;
    }

    [Icall("UnityEngine.Collider::INTERNAL_CALL_Internal_Raycast")]
    static bool ColliderRaycast(Collider col, ref Ray ray, ref Hit hit, float max) => Raycast(C(col), ray.origin, ray.direction, max, out hit);

    [Icall("UnityEngine.Physics2D::INTERNAL_CALL_OverlapPoint")]
    static Collider2D? OverlapPoint(ref Vector2 p, int mask, float minD, float maxD) => OverlapAll(ref p, mask, minD, maxD).FirstOrDefault();

    [Icall("UnityEngine.Physics2D::INTERNAL_CALL_OverlapPointAll")]
    static Collider2D[] OverlapAll(ref Vector2 p, int mask, float minD, float maxD) {
        var l = new List<Collider2D>();
        foreach (var c in All) {
            if (!c.Enabled || c.IsAsset || !c.GameObject.ActiveInHierarchy || (mask & (1 << c.GameObject.Layer)) == 0) continue;
            if (c.Shape is not (NCollider.Kind.Box2D or NCollider.Kind.Circle2D)) continue;
            var lp = M.MulPoint(c.GameObject.Transform.WorldToLocal, new Vector3(p.x, p.y, 0));
            float x = lp.x - c.Offset2D.x, y = lp.y - c.Offset2D.y;
            bool inside = c.Shape == NCollider.Kind.Box2D ? Math.Abs(x) <= c.Size.x / 2 && Math.Abs(y) <= c.Size.y / 2 : x * x + y * y <= c.Radius * c.Radius;
            if (inside && c.Managed is Collider2D m) l.Add(m);
        }
        return l.ToArray();
    }
}
