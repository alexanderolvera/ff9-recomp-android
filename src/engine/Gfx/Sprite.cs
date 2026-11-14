// Sprites (Unity 5.2 layout: no stored pivot; pivot derived from m_Offset).
using System;
using System.Runtime.CompilerServices;
using FF9Engine.Assets;
using FF9Engine.Core;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace FF9Engine.Gfx;

public sealed class NSprite : NObject {
    public Texture2D? Texture;
    public Rect Rect, TextureRect;
    public Vector2 Offset, TextureRectOffset;
    public Vector4 Border;
    public float PixelsPerUnit = 100;
    public uint Extrude;
    public Vector2[] Vertices = Array.Empty<Vector2>();   // in units, relative to the pivot
    public ushort[] Triangles = Array.Empty<ushort>();
    public uint SettingsRaw;
    public NMesh? MeshCache;

    /// Pivot in pixels from the rect's bottom-left.
    public Vector2 Pivot => new(Rect.width / 2 - Offset.x, Rect.height / 2 - Offset.y);

    public Vector2[] UVs() {
        var tex = Texture == null ? null : Textures.OfOrNull(Texture);
        float tw = tex?.Width ?? 1, th = tex?.Height ?? 1;
        var r = new Vector2[Vertices.Length];
        var piv = Pivot;
        for (int i = 0; i < Vertices.Length; i++) {
            // vertex (units) -> pixel position in the rect -> texture UV
            float px = Vertices[i].x * PixelsPerUnit + piv.x, py = Vertices[i].y * PixelsPerUnit + piv.y;
            float tx = TextureRect.x + (px - TextureRectOffset.x), ty = TextureRect.y + (py - TextureRectOffset.y);
            r[i] = new Vector2(tx / tw, ty / th);
        }
        return r;
    }

    public void BuildQuad() {
        var piv = Pivot;
        float x0 = (TextureRectOffset.x - piv.x) / PixelsPerUnit, y0 = (TextureRectOffset.y - piv.y) / PixelsPerUnit;
        float x1 = x0 + TextureRect.width / PixelsPerUnit, y1 = y0 + TextureRect.height / PixelsPerUnit;
        Vertices = new[] { new Vector2(x0, y1), new Vector2(x1, y1), new Vector2(x0, y0), new Vector2(x1, y0) };
        Triangles = new ushort[] { 0, 1, 2, 2, 1, 3 };
    }
}

public static class Sprites {
    public static void Register() => AssetDB.Readers[ClassIds.Sprite] = Read;

    public static NSprite? Of(Sprite? s) => Registry.Get(s) switch { NAsset { Data: NSprite x } => x, NSprite x => x, _ => null };
    static NSprite S(Sprite s) => Of(s) ?? throw new NullReferenceException("sprite is null");

    static void Read(NAsset a, BinReader r) {
        var s = new NSprite();
        a.Data = s;
        a.Name = s.Name = r.String();
        s.Rect = r.Rect();
        s.Offset = r.V2();
        s.Border = r.V4();
        s.PixelsPerUnit = r.F32();
        s.Extrude = r.U32();
        // m_RD
        var (tfi, tpi) = r.PPtr();
        var tf = AssetDB.External(a.File!, tfi);
        s.Texture = tf != null && tpi != 0 ? AssetDB.Load(tf, tpi) as Texture2D : null;
        r.PPtr();                       // alphaTexture
        int nv = r.I32();
        s.Vertices = new Vector2[nv];
        for (int i = 0; i < nv; i++) { var v = r.V3(); s.Vertices[i] = new Vector2(v.x, v.y); }
        int ni = r.I32();
        s.Triangles = new ushort[ni];
        for (int i = 0; i < ni; i++) s.Triangles[i] = r.U16();
        r.Align4();
        s.TextureRect = r.Rect();
        s.TextureRectOffset = r.V2();
        s.SettingsRaw = r.U32();
        r.V4();                         // uvTransform
        if (r.Pos != r.End) Log.Once("spritelayout", $"sprite '{s.Name}': {r.End - r.Pos} bytes unread");
        if (Environment.GetEnvironmentVariable("FF9_TRACE_SPRITE") is { } ts && s.Name.Contains(ts))
            Log.Info($"sprite '{s.Name}' rect={s.Rect} offset={s.Offset} border={s.Border} ppu={s.PixelsPerUnit} texRect={s.TextureRect} texOff={s.TextureRectOffset} verts={s.Vertices.Length} tris={s.Triangles.Length} tex={(s.Texture != null ? Registry.Get(s.Texture)?.Name : null)}");
        if (s.Vertices.Length == 0) s.BuildQuad();
    }

    [Icall("UnityEngine.Sprite::INTERNAL_CALL_Create")]
    static Sprite Create(Texture2D tex, ref Rect rect, ref Vector2 pivot, float ppu, uint extrude, SpriteMeshType mt, ref Vector4 border) {
        var s = new NSprite {
            Texture = tex, Rect = rect, TextureRect = rect, PixelsPerUnit = ppu <= 0 ? 100 : ppu, Extrude = extrude, Border = border,
            Offset = new Vector2(rect.width * (0.5f - pivot.x), rect.height * (0.5f - pivot.y)),
        };
        s.BuildQuad();
        var m = (Sprite)RuntimeHelpers.GetUninitializedObject(typeof(Sprite));
        Registry.Add(s, m);
        return m;
    }

    [Icall("UnityEngine.Sprite::INTERNAL_get_rect")] static void GetRect(Sprite s, ref Rect r) => r = S(s).Rect;
    [Icall("UnityEngine.Sprite::get_pixelsPerUnit")] static float Ppu(Sprite s) => S(s).PixelsPerUnit;
    [Icall("UnityEngine.Sprite::get_texture")] static Texture2D? Tex(Sprite s) => S(s).Texture;
    [Icall("UnityEngine.Sprite::get_associatedAlphaSplitTexture")] static Texture2D? Alpha(Sprite s) => null;
    [Icall("UnityEngine.Sprite::INTERNAL_get_textureRect")] static void TexRect(Sprite s, ref Rect r) => r = S(s).TextureRect;
    [Icall("UnityEngine.Sprite::Internal_GetTextureRectOffset")] static void TexRectOffset(Sprite s, ref Vector2 v) => v = S(s).TextureRectOffset;
    [Icall("UnityEngine.Sprite::Internal_GetPivot")] static void Pivot(Sprite s, ref Vector2 v) => v = S(s).Pivot;
    [Icall("UnityEngine.Sprite::INTERNAL_get_border")] static void Border(Sprite s, ref Vector4 v) => v = S(s).Border;
    [Icall("UnityEngine.Sprite::get_packed")] static bool Packed(Sprite s) => false;
    [Icall("UnityEngine.Sprite::INTERNAL_get_bounds")]
    static void Bounds(Sprite s, ref Bounds b) {
        var x = S(s);
        var piv = x.Pivot;
        var size = new Vector3(x.Rect.width / x.PixelsPerUnit, x.Rect.height / x.PixelsPerUnit, 0.2f);
        var c = new Vector3((x.Rect.width / 2 - piv.x) / x.PixelsPerUnit, (x.Rect.height / 2 - piv.y) / x.PixelsPerUnit, 0);
        b = new Bounds(c, size);
    }
    [Icall("UnityEngine.Sprite::get_vertices")] static Vector2[] Verts(Sprite s) => (Vector2[])S(s).Vertices.Clone();
    [Icall("UnityEngine.Sprite::get_triangles")] static ushort[] Tris(Sprite s) => (ushort[])S(s).Triangles.Clone();
    [Icall("UnityEngine.Sprite::get_uv")] static Vector2[] Uv(Sprite s) => S(s).UVs();
    [Icall("UnityEngine.Sprite::OverrideGeometry")] static void Override(Sprite s, Vector2[] v, ushort[] t) { var x = S(s); x.Vertices = v; x.Triangles = t; x.MeshCache = null; }

    static Vector4 Outer(NSprite s) {
        var tex = s.Texture == null ? null : Textures.OfOrNull(s.Texture);
        float w = tex?.Width ?? 1, h = tex?.Height ?? 1;
        var r = s.TextureRect;
        return new Vector4(r.x / w, r.y / h, (r.x + r.width) / w, (r.y + r.height) / h);
    }

    [Icall("UnityEngine.Sprites.DataUtility::GetOuterUV")] static Vector4 GetOuterUV(Sprite s) => Outer(S(s));

    [Icall("UnityEngine.Sprites.DataUtility::GetInnerUV")]
    static Vector4 GetInnerUV(Sprite s) {
        var x = S(s);
        var tex = x.Texture == null ? null : Textures.OfOrNull(x.Texture);
        float w = tex?.Width ?? 1, h = tex?.Height ?? 1;
        var r = x.TextureRect; var b = x.Border;
        return new Vector4((r.x + b.x) / w, (r.y + b.y) / h, (r.x + r.width - b.z) / w, (r.y + r.height - b.w) / h);
    }

    [Icall("UnityEngine.Sprites.DataUtility::GetPadding")]
    static Vector4 GetPadding(Sprite s) {
        var x = S(s);
        return new Vector4(x.TextureRectOffset.x, x.TextureRectOffset.y, x.Rect.width - x.TextureRect.width - x.TextureRectOffset.x, x.Rect.height - x.TextureRect.height - x.TextureRectOffset.y);
    }

    [Icall("UnityEngine.Sprites.DataUtility::Internal_GetMinSize")]
    static void MinSize(Sprite s, ref Vector2 v) { var b = S(s).Border; v = new Vector2(b.x + b.z, b.y + b.w); }
}
