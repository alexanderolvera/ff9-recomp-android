// Unity UI (uGUI): Canvas and CanvasRenderer components, drawn after the cameras (overlay) or by
// their camera. FF9's own UI is NGUI (MeshRenderers); uGUI is used by a few Memoria panels.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FF9Engine.Assets;
using FF9Engine.Core;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace FF9Engine.Gfx;

public sealed class NCanvas : NComponent {
    public RenderMode Mode;
    public Camera? WorldCamera;
    public float PlaneDistance = 100, ScaleFactor = 1, ReferencePPU = 100;
    public bool PixelPerfect, OverrideSorting, OverridePixelPerfect;
    public int SortingLayerId, SortingOrder;
}

public sealed class NCanvasRenderer : NComponent {
    public Mesh? Mesh;
    public Material?[] Mats = new Material?[1];
    public Material?[] PopMats = Array.Empty<Material?>();
    public Texture? Texture;
    public Color Color = new(1, 1, 1, 1);
    public float Alpha = 1;
    public bool Cull, HasMoved;
    public Rect? Clip;
    public int MaterialCount = 1;
    public bool HasPop;
}

public static class Canvases {
    public static readonly List<NCanvas> All = new();

    public static void Register() {
        Components.Factories[223] = () => { var c = new NCanvas(); All.Add(c); return c; };
        Components.Factories[222] = () => new NCanvasRenderer();
        SceneBuilder.ComponentReaders[223] = (c, r, b) => {
            var x = (NCanvas)c;
            r.PPtr();
            x.Enabled = r.Bool(); r.Align4();
            x.Mode = (RenderMode)r.I32();
            var (cfi, cpi) = r.PPtr();
            if (cpi != 0) x.WorldCamera = b.Resolve(b.File, cfi, cpi, typeof(Camera)) as Camera;
            x.PlaneDistance = r.F32();
            x.PixelPerfect = r.Bool(); r.Bool(); x.OverrideSorting = r.Bool(); x.OverridePixelPerfect = r.Bool();
            r.Align4();
            r.F32();
            x.SortingLayerId = r.I32();
            x.SortingOrder = r.I16();
        };
        SceneBuilder.ComponentReaders[222] = (c, r, b) => r.PPtr();
        Cloner.RecordCopiers[typeof(NCanvas)] = c => { var n = (NCanvas)Cloner.CloneRecord(c); All.Add(n); return n; };
        Cloner.RecordCopiers[typeof(NCanvasRenderer)] = c => { var n = (NCanvasRenderer)Cloner.CloneRecord(c); n.Mesh = null; n.Mats = (Material?[])n.Mats.Clone(); return n; };
    }

    static NCanvas C(Canvas c) => Registry.Of<NCanvas>(c);
    static NCanvasRenderer CR(CanvasRenderer c) => Registry.Of<NCanvasRenderer>(c);

    public static NCanvas? RootOf(NTransform t) {
        NCanvas? root = null;
        for (var p = t; p != null; p = p.Parent)
            foreach (var comp in p.GameObject.Components) if (comp is NCanvas cv && cv.Enabled) root = cv;
        return root;
    }

    [Icall("UnityEngine.Canvas::get_renderMode")] static RenderMode GetMode(Canvas c) => C(c).Mode;
    [Icall("UnityEngine.Canvas::set_renderMode")] static void SetMode(Canvas c, RenderMode v) => C(c).Mode = v;
    [Icall("UnityEngine.Canvas::get_isRootCanvas")] static bool IsRoot(Canvas c) { var x = C(c); return RootOf(x.GameObject.Transform) == x || x.GameObject.Transform.Parent == null; }
    [Icall("UnityEngine.Canvas::get_worldCamera")] static Camera? GetCam(Canvas c) => C(c).WorldCamera;
    [Icall("UnityEngine.Canvas::set_worldCamera")] static void SetCam(Canvas c, Camera v) => C(c).WorldCamera = v;
    [Icall("UnityEngine.Canvas::get_scaleFactor")] static float GetScale(Canvas c) => C(c).ScaleFactor;
    [Icall("UnityEngine.Canvas::set_scaleFactor")] static void SetScale(Canvas c, float v) => C(c).ScaleFactor = v;
    [Icall("UnityEngine.Canvas::get_referencePixelsPerUnit")] static float GetPPU(Canvas c) => C(c).ReferencePPU;
    [Icall("UnityEngine.Canvas::set_referencePixelsPerUnit")] static void SetPPU(Canvas c, float v) => C(c).ReferencePPU = v;
    [Icall("UnityEngine.Canvas::get_pixelPerfect")] static bool GetPP(Canvas c) => C(c).PixelPerfect;
    [Icall("UnityEngine.Canvas::set_pixelPerfect")] static void SetPP(Canvas c, bool v) => C(c).PixelPerfect = v;
    [Icall("UnityEngine.Canvas::get_planeDistance")] static float GetPD(Canvas c) => C(c).PlaneDistance;
    [Icall("UnityEngine.Canvas::set_planeDistance")] static void SetPD(Canvas c, float v) => C(c).PlaneDistance = v;
    [Icall("UnityEngine.Canvas::get_renderOrder")] static int RenderOrder(Canvas c) => All.IndexOf(C(c));
    [Icall("UnityEngine.Canvas::get_overrideSorting")] static bool GetOS(Canvas c) => C(c).OverrideSorting;
    [Icall("UnityEngine.Canvas::set_overrideSorting")] static void SetOS(Canvas c, bool v) => C(c).OverrideSorting = v;
    [Icall("UnityEngine.Canvas::get_sortingOrder")] static int GetOrder(Canvas c) => C(c).SortingOrder;
    [Icall("UnityEngine.Canvas::set_sortingOrder")] static void SetOrder(Canvas c, int v) => C(c).SortingOrder = v;
    [Icall("UnityEngine.Canvas::get_sortingLayerID")] static int GetLayer(Canvas c) => C(c).SortingLayerId;
    [Icall("UnityEngine.Canvas::set_sortingLayerID")] static void SetLayer(Canvas c, int v) => C(c).SortingLayerId = v;
    [Icall("UnityEngine.Canvas::get_cachedSortingLayerValue")] static int CachedLayer(Canvas c) => 0;
    [Icall("UnityEngine.Canvas::get_sortingLayerName")] static string GetLayerName(Canvas c) => "Default";
    [Icall("UnityEngine.Canvas::INTERNAL_get_pixelRect")] static void PixelRect(Canvas c, ref Rect r) => r = new Rect(0, 0, Device.Width, Device.Height);
    [Icall("UnityEngine.Canvas::ForceUpdateCanvases")] static void ForceUpdate() { }

    static Material? defaultMat;
    [Icall("UnityEngine.Canvas::GetDefaultCanvasMaterial")]
    [Icall("UnityEngine.Canvas::GetDefaultCanvasTextMaterial")]
    static Material? DefaultMaterial() {
        if (defaultMat != null) return defaultMat;
        var sh = Shaders.Find("UI/Default");
        if (sh == null) return null;
        UnityEngine.Shader? sm = null;
        foreach (var n in Registry.All) if (n is NAsset { Data: NShader s } && s == sh) { sm = (UnityEngine.Shader)n.Managed!; break; }
        defaultMat = new Material(sm);
        return defaultMat;
    }

    [Icall("UnityEngine.CanvasRenderer::INTERNAL_CALL_EnableRectClipping")] static void EnableClip(CanvasRenderer c, ref Rect r) => CR(c).Clip = r;
    [Icall("UnityEngine.CanvasRenderer::DisableRectClipping")] static void DisableClip(CanvasRenderer c) => CR(c).Clip = null;
    [Icall("UnityEngine.CanvasRenderer::set_hasPopInstruction")] static void SetHasPop(CanvasRenderer c, bool v) => CR(c).HasPop = v;
    [Icall("UnityEngine.CanvasRenderer::get_hasPopInstruction")] static bool GetHasPop(CanvasRenderer c) => CR(c).HasPop;
    [Icall("UnityEngine.CanvasRenderer::set_materialCount")] static void SetMatCount(CanvasRenderer c, int v) { var x = CR(c); x.MaterialCount = v; Array.Resize(ref x.Mats, Math.Max(v, 0)); }
    [Icall("UnityEngine.CanvasRenderer::get_materialCount")] static int GetMatCount(CanvasRenderer c) => CR(c).MaterialCount;
    [Icall("UnityEngine.CanvasRenderer::SetMaterial")] static void SetMat(CanvasRenderer c, Material m, int i) { var x = CR(c); if (i >= x.Mats.Length) Array.Resize(ref x.Mats, i + 1); x.Mats[i] = m; }
    [Icall("UnityEngine.CanvasRenderer::GetMaterial")] static Material? GetMat(CanvasRenderer c, int i) { var x = CR(c); return i < x.Mats.Length ? x.Mats[i] : null; }
    [Icall("UnityEngine.CanvasRenderer::set_popMaterialCount")] static void SetPopCount(CanvasRenderer c, int v) { var x = CR(c); Array.Resize(ref x.PopMats, Math.Max(v, 0)); }
    [Icall("UnityEngine.CanvasRenderer::SetPopMaterial")] static void SetPop(CanvasRenderer c, Material m, int i) { var x = CR(c); if (i >= x.PopMats.Length) Array.Resize(ref x.PopMats, i + 1); x.PopMats[i] = m; }
    [Icall("UnityEngine.CanvasRenderer::SetTexture")] static void SetTex(CanvasRenderer c, Texture t) => CR(c).Texture = t;
    [Icall("UnityEngine.CanvasRenderer::SetMesh")] static void SetMesh(CanvasRenderer c, Mesh m) => CR(c).Mesh = m;
    [Icall("UnityEngine.CanvasRenderer::Clear")] static void Clear(CanvasRenderer c) { var x = CR(c); x.Mesh = null; }
    [Icall("UnityEngine.CanvasRenderer::INTERNAL_CALL_SetColor")] static void SetColor(CanvasRenderer c, ref Color col) => CR(c).Color = col;
    [Icall("UnityEngine.CanvasRenderer::GetColor")] static Color GetColor(CanvasRenderer c) => CR(c).Color;
    [Icall("UnityEngine.CanvasRenderer::SetAlpha")] static void SetAlpha(CanvasRenderer c, float a) => CR(c).Alpha = a;
    [Icall("UnityEngine.CanvasRenderer::GetAlpha")] static float GetAlpha(CanvasRenderer c) => CR(c).Alpha;
    [Icall("UnityEngine.CanvasRenderer::get_cull")] static bool GetCull(CanvasRenderer c) => CR(c).Cull;
    [Icall("UnityEngine.CanvasRenderer::set_cull")] static void SetCull(CanvasRenderer c, bool v) => CR(c).Cull = v;
    [Icall("UnityEngine.CanvasRenderer::get_hasMoved")] static bool HasMoved(CanvasRenderer c) => true;
    [Icall("UnityEngine.CanvasRenderer::get_absoluteDepth")] static int Depth(CanvasRenderer c) => 0;
    [Icall("UnityEngine.CanvasRenderer::get_relativeDepth")] static int RelDepth(CanvasRenderer c) => 0;

    [Icall("UnityEngine.CanvasRenderer::SplitUIVertexStreamsInternal")]
    static void Split(object verts, object positions, object colors, object uv0s, object uv1s, object normals, object tangents) {
        var v = (List<UIVertex>)verts;
        ((List<Vector3>)positions).Clear(); ((List<Color32>)colors).Clear(); ((List<Vector2>)uv0s).Clear(); ((List<Vector2>)uv1s).Clear();
        ((List<Vector3>)normals).Clear(); ((List<Vector4>)tangents).Clear();
        foreach (var x in v) {
            ((List<Vector3>)positions).Add(x.position); ((List<Color32>)colors).Add(x.color); ((List<Vector2>)uv0s).Add(x.uv0); ((List<Vector2>)uv1s).Add(x.uv1);
            ((List<Vector3>)normals).Add(x.normal); ((List<Vector4>)tangents).Add(x.tangent);
        }
    }

    [Icall("UnityEngine.CanvasRenderer::SplitIndiciesStreamsInternal")]
    static void SplitIdx(object verts, object indices) {
        var n = ((List<UIVertex>)verts).Count;
        var l = (List<int>)indices;
        l.Clear();
        for (int i = 0; i < n; i++) l.Add(i);
    }

    [Icall("UnityEngine.CanvasRenderer::CreateUIVertexStreamInternal")]
    static void Create(object verts, object positions, object colors, object uv0s, object uv1s, object normals, object tangents, object indices) {
        var v = (List<UIVertex>)verts;
        v.Clear();
        var p = (List<Vector3>)positions; var c = (List<Color32>)colors; var u0 = (List<Vector2>)uv0s; var u1 = (List<Vector2>)uv1s;
        var n = (List<Vector3>)normals; var t = (List<Vector4>)tangents;
        foreach (int i in (List<int>)indices) {
            var x = new UIVertex();
            x.position = p[i]; x.color = c[i]; x.uv0 = i < u0.Count ? u0[i] : default; x.uv1 = i < u1.Count ? u1[i] : default;
            x.normal = i < n.Count ? n[i] : default; x.tangent = i < t.Count ? t[i] : default;
            v.Add(x);
        }
    }

    [Icall("UnityEngine.RectTransformUtility::INTERNAL_CALL_PixelAdjustPoint")]
    static void PixelAdjustPoint(ref Vector2 p, Transform t, Canvas c, ref Vector2 o) => o = new Vector2(MathF.Round(p.x), MathF.Round(p.y));

    [Icall("UnityEngine.RectTransformUtility::PixelAdjustRect")]
    static Rect PixelAdjustRect(RectTransform rt, Canvas c) { Rect r = rt.rect; return r; }

    [Icall("UnityEngine.RectTransformUtility::INTERNAL_CALL_RectangleContainsScreenPoint")]
    static bool Contains(RectTransform rt, ref Vector2 sp, Camera cam) {
        var t = Registry.Of<NTransform>(rt);
        var lp = M.MulPoint(t.WorldToLocal, new Vector3(sp.x, sp.y, 0));
        var r = rt.rect;
        return lp.x >= r.x && lp.x <= r.xMax && lp.y >= r.y && lp.y <= r.yMax;
    }

    /// Draws overlay canvases (screen space): pixel projection over the whole screen.
    public static void RenderOverlay() {
        All.RemoveAll(c => c.Managed == null || Registry.Get(c.Managed) != c);
        foreach (var cv in All.Where(c => c.Enabled && !c.IsAsset && c.GameObject.ActiveInHierarchy && RootOf(c.GameObject.Transform) == c).OrderBy(c => c.SortingOrder)) {
            if (cv.Mode == RenderMode.WorldSpace) continue;
            var saved = (Pipeline.Model, Pipeline.View, Pipeline.Proj);
            if (cv.Mode == RenderMode.ScreenSpaceCamera && Registry.Get(cv.WorldCamera) is NCamera cam) {
                Pipeline.View = cam.View; Pipeline.Proj = cam.Projection;
            } else {
                Pipeline.View = M.Identity;
                Pipeline.Proj = Icalls.MathIcallsPublic.Ortho(0, Device.Width, 0, Device.Height, -1000, 1000);
            }
            Draw(cv.GameObject.Transform);
            (Pipeline.Model, Pipeline.View, Pipeline.Proj) = saved;
        }
    }

    static void Draw(NTransform t) {
        if (!t.GameObject.ActiveInHierarchy) return;
        foreach (var c in t.GameObject.Components)
            if (c is NCanvasRenderer cr && !cr.Cull && Meshes.Of(cr.Mesh) is { } mesh && cr.Mats.Length > 0 && Materials.Of(cr.Mats[0]) is { } mat) {
                if (cr.Texture != null) mat.Textures[PropIds.Id("_MainTex")] = cr.Texture;
                var col = cr.Color;
                mat.Vectors[PropIds.Id("_Color")] = new Vector4(col.r, col.g, col.b, col.a * cr.Alpha);
                var sh = mat.Shader;
                if (sh == null) continue;
                for (int i = 0; i < sh.Passes.Count; i++) { Pipeline.SetPass(mat, i); Pipeline.DrawMeshNow(mesh, t.LocalToWorld, -1); }
            }
        foreach (var ch in t.Children.ToArray()) Draw(ch);
    }
}
