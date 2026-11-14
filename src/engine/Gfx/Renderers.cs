// Renderer components: MeshRenderer (+MeshFilter), SpriteRenderer, SkinnedMeshRenderer.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FF9Engine.Assets;
using FF9Engine.Core;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace FF9Engine.Gfx;

public class NRenderer : NComponent {
    public Material?[] Materials = Array.Empty<Material?>();
    public int SortingLayerId, SortingOrder;
    public bool IsVisible;
    public NMaterialBlock? Block;
    public virtual NMesh? DrawMesh => null;
    public virtual Matrix4x4 DrawMatrix => GameObject.Transform.LocalToWorld;
    public bool Instanced;      // Renderer.material made per-renderer copies
}

public sealed class NMeshFilter : NComponent {
    public Mesh? Mesh;
}

public sealed class NMeshRenderer : NRenderer {
    public override NMesh? DrawMesh {
        get {
            foreach (var c in GameObject.Components) if (c is NMeshFilter f) return Meshes.Of(f.Mesh);
            return null;
        }
    }
}

public sealed class NSpriteRenderer : NRenderer {
    public Sprite? Sprite;
    public Color Color = new(1, 1, 1, 1);
    NMesh? mesh;
    NSprite? meshFor;
    Color meshColor;

    public override NMesh? DrawMesh {
        get {
            var s = Sprites.Of(Sprite);
            if (s == null) return null;
            if (mesh == null || meshFor != s || !meshColor.Equals(Color) || s.MeshCache == null) {
                mesh ??= new NMesh();
                mesh.Vertices = s.Vertices.Select(v => new Vector3(v.x, v.y, 0)).ToArray();
                mesh.UV = s.UVs();
                var c = new Color32((byte)(Color.r * 255), (byte)(Color.g * 255), (byte)(Color.b * 255), (byte)(Color.a * 255));
                mesh.Colors = Enumerable.Repeat(c, s.Vertices.Length).ToArray();
                mesh.SubMeshes = new() { new SubMeshData { Indices = s.Triangles.Select(t => (int)t).ToArray() } };
                mesh.Dirty = true;
                meshFor = s; meshColor = Color; s.MeshCache = mesh;
            }
            return mesh;
        }
    }
}

public sealed class NSkinnedMeshRenderer : NRenderer {
    public Mesh? Mesh;
    public Transform?[] Bones = Array.Empty<Transform?>();
    public Transform? RootBone;
    public Bounds LocalBounds;
    Vector3[]? skinned, skinnedN;

    public override NMesh? DrawMesh => Meshes.Of(Mesh);

    // vertices are skinned into world space; drawn with an identity matrix
    public override Matrix4x4 DrawMatrix => Bones.Length > 0 && Meshes.Of(Mesh)?.BoneWeights != null ? M.Identity : GameObject.Transform.LocalToWorld;

    public void Skin() {
        var m = Meshes.Of(Mesh);
        if (m?.BoneWeights == null || m.BindPoses == null || Bones.Length == 0) return;
        var mats = new Matrix4x4[Bones.Length];
        for (int i = 0; i < Bones.Length; i++) {
            var bt = Registry.Get(Bones[i]) as NTransform;
            var bw = bt?.LocalToWorld ?? M.Identity;
            mats[i] = i < m.BindPoses.Length ? M.Mul(bw, m.BindPoses[i]) : bw;
        }
        int n = m.Vertices.Length;
        if (skinned == null || skinned.Length != n) { skinned = new Vector3[n]; skinnedN = new Vector3[n]; }
        for (int v = 0; v < n; v++) {
            var w = v < m.BoneWeights.Length ? m.BoneWeights[v] : default;
            Vector3 p = default, nn = default;
            void Acc(int bi, float wt) {
                if (wt <= 0 || bi < 0 || bi >= mats.Length) return;
                var q = M.MulPoint(mats[bi], m.Vertices[v]);
                p = new Vector3(p.x + q.x * wt, p.y + q.y * wt, p.z + q.z * wt);
                if (m.Normals != null) { var qn = M.MulVector(mats[bi], m.Normals[v]); nn = new Vector3(nn.x + qn.x * wt, nn.y + qn.y * wt, nn.z + qn.z * wt); }
            }
            Acc(w.boneIndex0, w.weight0); Acc(w.boneIndex1, w.weight1); Acc(w.boneIndex2, w.weight2); Acc(w.boneIndex3, w.weight3);
            skinned[v] = p;
            skinnedN![v] = nn;
        }
        m.Upload(skinned, m.Normals != null ? skinnedN : null);
    }
}

public sealed class NMaterialBlock {
    public readonly Dictionary<int, Vector4> Vectors = new();
    public readonly Dictionary<int, Texture?> Textures = new();
    public readonly Dictionary<int, Matrix4x4> Matrices = new();
}

public static class RendererComponents {
    public static readonly List<NRenderer> All = new();

    public static void Register() {
        Components.Factories[ClassIds.MeshRenderer] = () => Track(new NMeshRenderer());
        Components.Factories[ClassIds.SpriteRenderer] = () => Track(new NSpriteRenderer());
        Components.Factories[137] = () => Track(new NSkinnedMeshRenderer());
        Components.Factories[ClassIds.MeshFilter] = () => new NMeshFilter();
        SceneBuilder.ComponentReaders[ClassIds.MeshRenderer] = (c, r, b) => ReadRenderer((NRenderer)c, r, b);
        SceneBuilder.ComponentReaders[ClassIds.SpriteRenderer] = (c, r, b) => {
            var s = (NSpriteRenderer)c;
            ReadRenderer(s, r, b);
            s.Sprite = Resolve<Sprite>(r, b);
            s.Color = r.Color();
        };
        SceneBuilder.ComponentReaders[137] = (c, r, b) => {
            var s = (NSkinnedMeshRenderer)c;
            ReadRenderer(s, r, b);
            r.I32(); r.Bool(); r.Align4();
            s.Mesh = Resolve<Mesh>(r, b);
            int nb = r.I32();
            s.Bones = new Transform?[nb];
            for (int i = 0; i < nb; i++) s.Bones[i] = Resolve<Transform>(r, b);
            int nw = r.I32(); r.Skip(nw * 4L);
            s.RootBone = Resolve<Transform>(r, b);
            var center = r.V3(); var ext = r.V3();
            s.LocalBounds = new Bounds(center, new Vector3(ext.x * 2, ext.y * 2, ext.z * 2));
        };
        SceneBuilder.ComponentReaders[ClassIds.MeshFilter] = (c, r, b) => { r.PPtr(); ((NMeshFilter)c).Mesh = Resolve<Mesh>(r, b); };
        Cloner.RecordCopiers[typeof(NMeshRenderer)] = c => Copy((NRenderer)c, new NMeshRenderer());
        Cloner.RecordCopiers[typeof(NSpriteRenderer)] = c => { var s = (NSpriteRenderer)c; var n = (NSpriteRenderer)Copy(s, new NSpriteRenderer()); n.Sprite = s.Sprite; n.Color = s.Color; return n; };
        Cloner.RecordCopiers[typeof(NSkinnedMeshRenderer)] = c => {
            var s = (NSkinnedMeshRenderer)c;
            var n = (NSkinnedMeshRenderer)Copy(s, new NSkinnedMeshRenderer());
            n.Mesh = s.Mesh; n.Bones = (Transform?[])s.Bones.Clone(); n.RootBone = s.RootBone; n.LocalBounds = s.LocalBounds;
            return n;
        };
        Cloner.RecordCopiers[typeof(NMeshFilter)] = c => new NMeshFilter { Mesh = ((NMeshFilter)c).Mesh };
        Cloner.Remappers.Add(Remap);
    }

    /// After Instantiate: bone references inside the copied hierarchy point at the copies.
    static void Remap(Dictionary<NObject, NObject> map) {
        var tmap = new Dictionary<UObject, UObject>(ReferenceEqualityComparer.Instance);
        foreach (var kv in map) if (kv.Key is NTransform) tmap[kv.Key.Managed!] = kv.Value.Managed!;
        foreach (var kv in map)
            if (kv.Value is NSkinnedMeshRenderer s) {
                for (int i = 0; i < s.Bones.Length; i++) if (s.Bones[i] != null && tmap.TryGetValue(s.Bones[i]!, out var nb)) s.Bones[i] = (Transform)nb;
                if (s.RootBone != null && tmap.TryGetValue(s.RootBone, out var nr)) s.RootBone = (Transform)nr;
            }
    }

    static NRenderer Copy(NRenderer s, NRenderer d) {
        d.Materials = (Material?[])s.Materials.Clone(); d.SortingLayerId = s.SortingLayerId; d.SortingOrder = s.SortingOrder;
        Track(d);
        return d;
    }

    static T Track<T>(T r) where T : NRenderer { All.Add(r); return r; }

    static T? Resolve<T>(BinReader r, SceneBuilder b) where T : UObject {
        var (fi, pi) = r.PPtr();
        return fi == 0 && pi == 0 ? null : b.Resolve(b.File, fi, pi, typeof(T)) as T;
    }

    /// Renderer (5.2): enabled, castShadows, receiveShadows, lightmap index/dynamic, tiling offsets,
    /// materials, subset indices, static batch root, useLightProbes, reflectionProbeUsage,
    /// probe anchor, sorting layer id, sorting order.
    static void ReadRenderer(NRenderer c, BinReader r, SceneBuilder b) {
        r.PPtr();
        c.Enabled = r.Bool(); r.U8(); r.U8(); r.Align4();
        r.U16(); r.U16();
        r.V4(); r.V4();
        int nm = r.I32();
        c.Materials = new Material?[nm];
        for (int i = 0; i < nm; i++) c.Materials[i] = Resolve<Material>(r, b);
        int ns = r.I32(); r.Skip(ns * 4L);
        r.PPtr();
        r.Bool(); r.Align4();
        r.I32();
        r.PPtr();
        c.SortingLayerId = (int)r.U32();
        c.SortingOrder = r.I16();
        r.Align4();
    }

    static NRenderer R(Renderer r) => Registry.Of<NRenderer>(r);

    [Icall("UnityEngine.Renderer::get_enabled")] static bool GetEnabled(Renderer r) => R(r).Enabled;
    [Icall("UnityEngine.Renderer::set_enabled")] static void SetEnabled(Renderer r, bool v) => R(r).Enabled = v;
    [Icall("UnityEngine.Renderer::get_isVisible")] static bool IsVisible(Renderer r) => R(r).IsVisible;
    [Icall("UnityEngine.Renderer::get_sortingOrder")] static int GetOrder(Renderer r) => R(r).SortingOrder;
    [Icall("UnityEngine.Renderer::set_sortingOrder")] static void SetOrder(Renderer r, int v) => R(r).SortingOrder = v;
    [Icall("UnityEngine.Renderer::get_sortingLayerID")] static int GetLayer(Renderer r) => R(r).SortingLayerId;
    [Icall("UnityEngine.Renderer::set_sortingLayerID")] static void SetLayer(Renderer r, int v) => R(r).SortingLayerId = v;
    [Icall("UnityEngine.Renderer::get_sortingLayerName")] static string GetLayerName(Renderer r) => "Default";
    [Icall("UnityEngine.Renderer::set_sortingLayerName")] static void SetLayerName(Renderer r, string v) { }
    [Icall("UnityEngine.Renderer::get_shadowCastingMode")] static UnityEngine.Rendering.ShadowCastingMode GetShadow(Renderer r) => 0;
    [Icall("UnityEngine.Renderer::set_shadowCastingMode")] static void SetShadow(Renderer r, UnityEngine.Rendering.ShadowCastingMode v) { }
    [Icall("UnityEngine.Renderer::set_receiveShadows")] static void SetReceive(Renderer r, bool v) { }
    [Icall("UnityEngine.Renderer::set_useLightProbes")] static void SetProbes(Renderer r, bool v) { }

    [Icall("UnityEngine.Renderer::get_sharedMaterial")] static Material? GetShared(Renderer r) { var x = R(r); return x.Materials.Length > 0 ? x.Materials[0] : null; }
    [Icall("UnityEngine.Renderer::set_sharedMaterial")] static void SetShared(Renderer r, Material m) { var x = R(r); if (x.Materials.Length == 0) x.Materials = new Material?[1]; x.Materials[0] = m; }
    [Icall("UnityEngine.Renderer::get_sharedMaterials")] static Material?[] GetSharedAll(Renderer r) => (Material?[])R(r).Materials.Clone();
    [Icall("UnityEngine.Renderer::set_sharedMaterials")] static void SetSharedAll(Renderer r, Material?[] m) => R(r).Materials = (Material?[])(m ?? Array.Empty<Material?>()).Clone();

    static Material? Instance(NRenderer x, int i) {
        var m = x.Materials[i];
        if (m == null) return null;
        if (x.Instanced && Materials.Of(m) is { } nm && !nm.IsAsset) return m;
        var copy = (Material)Cloner.Clone(m, null, null);
        Registry.Get(copy)!.Name = (Registry.Get(m)?.Name ?? "") + " (Instance)";
        x.Materials[i] = copy;
        return copy;
    }

    [Icall("UnityEngine.Renderer::get_material")]
    static Material? GetMaterial(Renderer r) {
        var x = R(r);
        if (x.Materials.Length == 0) return null;
        var m = Instance(x, 0);
        x.Instanced = true;
        return m;
    }

    [Icall("UnityEngine.Renderer::set_material")] static void SetMaterial(Renderer r, Material m) { var x = R(r); x.Materials = new[] { m }; x.Instanced = false; }

    [Icall("UnityEngine.Renderer::get_materials")]
    static Material?[] GetMaterials(Renderer r) {
        var x = R(r);
        for (int i = 0; i < x.Materials.Length; i++) Instance(x, i);
        x.Instanced = true;
        return (Material?[])x.Materials.Clone();
    }

    [Icall("UnityEngine.Renderer::set_materials")] static void SetMaterials(Renderer r, Material?[] m) { var x = R(r); x.Materials = (Material?[])m.Clone(); x.Instanced = false; }

    [Icall("UnityEngine.Renderer::INTERNAL_get_bounds")]
    static void GetBounds(Renderer r, ref Bounds b) {
        var x = R(r);
        var mesh = x.DrawMesh;
        if (mesh == null) { b = new Bounds(x.GameObject.Transform.Position, new Vector3()); return; }
        var lb = mesh.Bounds;
        var m = x.GameObject.Transform.LocalToWorld;
        var c = M.MulPoint(m, lb.center);
        var e = lb.extents;
        var ax = M.MulVector(m, new Vector3(e.x, 0, 0)); var ay = M.MulVector(m, new Vector3(0, e.y, 0)); var az = M.MulVector(m, new Vector3(0, 0, e.z));
        var ext = new Vector3(Math.Abs(ax.x) + Math.Abs(ay.x) + Math.Abs(az.x), Math.Abs(ax.y) + Math.Abs(ay.y) + Math.Abs(az.y), Math.Abs(ax.z) + Math.Abs(ay.z) + Math.Abs(az.z));
        b = new Bounds(c, new Vector3(ext.x * 2, ext.y * 2, ext.z * 2));
    }

    [Icall("UnityEngine.Renderer::Internal_GetPropertyBlock")]
    static void GetBlock(Renderer r, MaterialPropertyBlock dest) {
        var x = R(r);
        var d = MaterialBlocks.Of(dest);
        d.Vectors.Clear(); d.Textures.Clear(); d.Matrices.Clear();
        if (x.Block == null) return;
        foreach (var kv in x.Block.Vectors) d.Vectors[kv.Key] = kv.Value;
        foreach (var kv in x.Block.Textures) d.Textures[kv.Key] = kv.Value;
        foreach (var kv in x.Block.Matrices) d.Matrices[kv.Key] = kv.Value;
    }

    [Icall("UnityEngine.Renderer::Internal_SetPropertyBlock")]
    static void SetBlock(Renderer r, MaterialPropertyBlock? src) {
        var x = R(r);
        if (src == null) { x.Block = null; return; }
        var s = MaterialBlocks.Of(src);
        var d = x.Block = new NMaterialBlock();
        foreach (var kv in s.Vectors) d.Vectors[kv.Key] = kv.Value;
        foreach (var kv in s.Textures) d.Textures[kv.Key] = kv.Value;
        foreach (var kv in s.Matrices) d.Matrices[kv.Key] = kv.Value;
    }

    // ---- MeshFilter ----
    static NMeshFilter F(MeshFilter f) => Registry.Of<NMeshFilter>(f);
    [Icall("UnityEngine.MeshFilter::get_sharedMesh")] static Mesh? GetSharedMesh(MeshFilter f) => F(f).Mesh;
    [Icall("UnityEngine.MeshFilter::set_sharedMesh")] static void SetSharedMesh(MeshFilter f, Mesh m) => F(f).Mesh = m;
    [Icall("UnityEngine.MeshFilter::get_mesh")]
    static Mesh? GetMesh(MeshFilter f) {
        var x = F(f);
        if (x.Mesh != null && Registry.Get(x.Mesh)?.IsAsset == true) x.Mesh = (Mesh)Cloner.Clone(x.Mesh, null, null);
        return x.Mesh;
    }
    [Icall("UnityEngine.MeshFilter::set_mesh")] static void SetMesh(MeshFilter f, Mesh m) => F(f).Mesh = m;

    // ---- SpriteRenderer ----
    static NSpriteRenderer SR(SpriteRenderer s) => Registry.Of<NSpriteRenderer>(s);
    [Icall("UnityEngine.SpriteRenderer::GetSprite_INTERNAL")] static Sprite? GetSprite(SpriteRenderer s) => SR(s).Sprite;
    [Icall("UnityEngine.SpriteRenderer::SetSprite_INTERNAL")] static void SetSprite(SpriteRenderer s, Sprite sp) => SR(s).Sprite = sp;
    [Icall("UnityEngine.SpriteRenderer::INTERNAL_get_color")] static void GetColor(SpriteRenderer s, ref Color c) => c = SR(s).Color;
    [Icall("UnityEngine.SpriteRenderer::INTERNAL_set_color")] static void SetColor(SpriteRenderer s, ref Color c) => SR(s).Color = c;

    // ---- SkinnedMeshRenderer ----
    static NSkinnedMeshRenderer SK(SkinnedMeshRenderer s) => Registry.Of<NSkinnedMeshRenderer>(s);
    [Icall("UnityEngine.SkinnedMeshRenderer::get_bones")] static Transform?[] GetBones(SkinnedMeshRenderer s) => (Transform?[])SK(s).Bones.Clone();
    [Icall("UnityEngine.SkinnedMeshRenderer::set_bones")] static void SetBones(SkinnedMeshRenderer s, Transform?[] b) => SK(s).Bones = (Transform?[])b.Clone();
    [Icall("UnityEngine.SkinnedMeshRenderer::get_rootBone")] static Transform? GetRoot(SkinnedMeshRenderer s) => SK(s).RootBone;
    [Icall("UnityEngine.SkinnedMeshRenderer::set_rootBone")] static void SetRoot(SkinnedMeshRenderer s, Transform t) => SK(s).RootBone = t;
    [Icall("UnityEngine.SkinnedMeshRenderer::get_sharedMesh")] static Mesh? GetSkMesh(SkinnedMeshRenderer s) => SK(s).Mesh;
    [Icall("UnityEngine.SkinnedMeshRenderer::set_sharedMesh")] static void SetSkMesh(SkinnedMeshRenderer s, Mesh m) => SK(s).Mesh = m;
    [Icall("UnityEngine.SkinnedMeshRenderer::INTERNAL_get_localBounds")] static void GetLB(SkinnedMeshRenderer s, ref Bounds b) => b = SK(s).LocalBounds;
    [Icall("UnityEngine.SkinnedMeshRenderer::INTERNAL_set_localBounds")] static void SetLB(SkinnedMeshRenderer s, ref Bounds b) => SK(s).LocalBounds = b;
    [Icall("UnityEngine.SkinnedMeshRenderer::set_updateWhenOffscreen")] static void Offscreen(SkinnedMeshRenderer s, bool v) { }
    [Icall("UnityEngine.SkinnedMeshRenderer::set_quality")] static void Quality(SkinnedMeshRenderer s, SkinQuality q) { }
}

public static class MaterialBlocks {
    public static NMaterialBlock Of(MaterialPropertyBlock b) {
        var p = Native.Handles.Ptr(b);
        if (p == IntPtr.Zero) { var d = new NMaterialBlock(); Native.Handles.SetPtr(b, Native.Handles.New(d)); return d; }
        return Native.Handles.Get<NMaterialBlock>(p);
    }

    [Icall("UnityEngine.MaterialPropertyBlock::InitBlock")] static void Init(MaterialPropertyBlock b) => Native.Handles.SetPtr(b, Native.Handles.New(new NMaterialBlock()));
    [Icall("UnityEngine.MaterialPropertyBlock::DestroyBlock")] static void Destroy(MaterialPropertyBlock b) { }
    [Icall("UnityEngine.MaterialPropertyBlock::Clear")] static void Clear(MaterialPropertyBlock b) { var d = Of(b); d.Vectors.Clear(); d.Textures.Clear(); d.Matrices.Clear(); }
    [Icall("UnityEngine.MaterialPropertyBlock::SetFloat")] static void SetFloat(MaterialPropertyBlock b, int id, float v) => Of(b).Vectors[id] = new Vector4(v, v, v, v);
    [Icall("UnityEngine.MaterialPropertyBlock::INTERNAL_CALL_SetVector")] static void SetVector(MaterialPropertyBlock b, int id, ref Vector4 v) => Of(b).Vectors[id] = v;
    [Icall("UnityEngine.MaterialPropertyBlock::INTERNAL_CALL_SetColor")] static void SetColor(MaterialPropertyBlock b, int id, ref Color c) => Of(b).Vectors[id] = new Vector4(c.r, c.g, c.b, c.a);
    [Icall("UnityEngine.MaterialPropertyBlock::INTERNAL_CALL_SetMatrix")] static void SetMatrix(MaterialPropertyBlock b, int id, ref Matrix4x4 m) => Of(b).Matrices[id] = m;
    [Icall("UnityEngine.MaterialPropertyBlock::SetTexture")] static void SetTexture(MaterialPropertyBlock b, int id, Texture t) => Of(b).Textures[id] = t;
}
