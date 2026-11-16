// Engine calls the game reaches rarely: lights, AudioSource (FF9's audio goes through Memoria's
// SoLoud backend), reflection probes, compute buffers, text components, misc helpers.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FF9Engine.Assets;
using FF9Engine.Core;
using FF9Engine.Gfx;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace FF9Engine.Icalls;

static class SmallIcalls {
    sealed class LightData { public Color Color = new(1, 1, 1, 1); public LightType Type = LightType.Directional; }
    sealed class AudioData { public float Volume = 1, Pitch = 1; public int Priority = 128; }
    sealed class TextData { public string Text = ""; public Color Color = new(1, 1, 1, 1); }

    static readonly ConditionalWeakTable<UObject, object> data = new();
    static T D<T>(UObject o) where T : class, new() => (T)data.GetValue(o, _ => new T());

    [Icall("UnityEngine.Light::INTERNAL_get_color")] static void GetLC(Light l, ref Color c) => c = D<LightData>(l).Color;
    [Icall("UnityEngine.Light::INTERNAL_set_color")] static void SetLC(Light l, ref Color c) => D<LightData>(l).Color = c;
    [Icall("UnityEngine.Light::set_type")] static void SetLT(Light l, LightType t) => D<LightData>(l).Type = t;
    [Icall("UnityEngine.Light::get_type")] static LightType GetLT(Light l) => D<LightData>(l).Type;

    [Icall("UnityEngine.AudioSource::PlayOneShot")] static void PlayOneShot(AudioSource s, AudioClip c, float v) => Log.Once("AudioSource.PlayOneShot", "AudioSource.PlayOneShot: Unity audio is not used (Memoria SoLoud backend)");
    [Icall("UnityEngine.AudioSource::get_volume")] static float GetVol(AudioSource s) => D<AudioData>(s).Volume;
    [Icall("UnityEngine.AudioSource::set_volume")] static void SetVol(AudioSource s, float v) => D<AudioData>(s).Volume = v;
    [Icall("UnityEngine.AudioSource::set_pitch")] static void SetPitch(AudioSource s, float v) => D<AudioData>(s).Pitch = v;
    [Icall("UnityEngine.AudioSource::set_priority")] static void SetPri(AudioSource s, int v) => D<AudioData>(s).Priority = v;

    [Icall("UnityEngine.Bounds::INTERNAL_CALL_Internal_Contains")]
    static bool Contains(ref Bounds b, ref Vector3 p) {
        var mn = b.min; var mx = b.max;
        return p.x >= mn.x && p.x <= mx.x && p.y >= mn.y && p.y <= mx.y && p.z >= mn.z && p.z <= mx.z;
    }

    [Icall("UnityEngine.BoxCollider2D::INTERNAL_set_size")] static void SetSize2D(BoxCollider2D c, ref Vector2 v) { if (Registry.Get(c) is Physics.NCollider n) n.Size = new Vector3(v.x, v.y, 0); }
    [Icall("UnityEngine.BoxCollider2D::INTERNAL_get_size")] static void GetSize2D(BoxCollider2D c, ref Vector2 v) { if (Registry.Get(c) is Physics.NCollider n) v = new Vector2(n.Size.x, n.Size.y); }
    [Icall("UnityEngine.MeshCollider::set_sharedMesh")] static void MeshCol(MeshCollider c, Mesh m) { }

    [Icall("UnityEngine.ComputeBuffer::InitBuffer")] static void InitBuffer(ComputeBuffer b, int count, int stride, ComputeBufferType t) => Log.Once("ComputeBuffer", "ComputeBuffer: compute shaders are not supported");
    [Icall("UnityEngine.ComputeBuffer::DestroyBuffer")] static void DestroyBuffer(ComputeBuffer b) { }
    [Icall("UnityEngine.ComputeBuffer::InternalSetData")] static void SetData(ComputeBuffer b, Array d, int s) { }
    [Icall("UnityEngine.ComputeBuffer::CopyCount")] static void CopyCount(ComputeBuffer a, ComputeBuffer b, int o) { }

    [Icall("UnityEngine.Cursor::set_lockState")] static void Lock(CursorLockMode m) { }
    [Icall("UnityEngine.DynamicGI::UpdateEnvironment")] static void UpdateEnv() { }
    [Icall("UnityEngine.Mathf::GammaToLinearSpace")] static float G2L(float v) => v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
    [Icall("UnityEngine.Mathf::LinearToGammaSpace")] static float L2G(float v) => v <= 0.0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1 / 2.4f) - 0.055f;

    [Icall("UnityEngine.LayerMask::NameToLayer")]
    static int NameToLayer(string name) {
        string[] builtin = { "Default", "TransparentFX", "Ignore Raycast", "", "Water", "UI" };
        for (int i = 0; i < builtin.Length; i++) if (builtin[i] == name) return i;
        int k = AssetDB.Layers.IndexOf(name);
        return k;
    }

    [Icall("UnityEngine.LayerMask::LayerToName")]
    static string LayerToName(int layer) {
        string[] builtin = { "Default", "TransparentFX", "Ignore Raycast", "", "Water", "UI" };
        if (layer >= 0 && layer < builtin.Length) return builtin[layer];
        return layer >= 0 && layer < AssetDB.Layers.Count ? AssetDB.Layers[layer] : "";
    }

    [Icall("UnityEngine.GameObject::CreatePrimitive")]
    static GameObject CreatePrimitive(PrimitiveType t) {
        var go = new GameObject(t.ToString());
        var mf = go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>();
        var m = new Mesh();
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mf.sharedMesh = m;
        Log.Once("CreatePrimitive", $"GameObject.CreatePrimitive({t}): a quad stands in");
        return go;
    }

    [Icall("UnityEngine.ReflectionProbe::INTERNAL_set_backgroundColor")] static void RpBg(ReflectionProbe p, ref Color c) { }
    [Icall("UnityEngine.ReflectionProbe::INTERNAL_set_size")] static void RpSize(ReflectionProbe p, ref Vector3 v) { }
    [Icall("UnityEngine.ReflectionProbe::RenderProbe")] static int RpRender(ReflectionProbe p, RenderTexture t) => 0;
    // No probe is rendered: a white one stands in. Memoria's battle shaders (Toon, Realism) wait in
    // HonoluluBattleMain.UpdateAmbientLight for the probe's texture and read its name: null threw
    // there and the battle never started (GitHub issue 13).
    [Icall("UnityEngine.ReflectionProbe::get_texture")] static Texture RpTex(ReflectionProbe p) => Textures.ManagedWhite;
    [Icall("UnityEngine.ReflectionProbe::set_clearFlags")] static void RpClear(ReflectionProbe p, UnityEngine.Rendering.ReflectionProbeClearFlags f) { }
    [Icall("UnityEngine.ReflectionProbe::set_cullingMask")] static void RpMask(ReflectionProbe p, int m) { }
    [Icall("UnityEngine.ReflectionProbe::set_mode")] static void RpMode(ReflectionProbe p, UnityEngine.Rendering.ReflectionProbeMode m) { }
    [Icall("UnityEngine.ReflectionProbe::set_refreshMode")] static void RpRefresh(ReflectionProbe p, UnityEngine.Rendering.ReflectionProbeRefreshMode m) { }
    [Icall("UnityEngine.ReflectionProbe::set_resolution")] static void RpRes(ReflectionProbe p, int r) { }

    [Icall("UnityEngine.TextMesh::set_text")] static void TmText(TextMesh t, string s) => D<TextData>(t).Text = s ?? "";
    [Icall("UnityEngine.TextMesh::get_text")] static string TmGetText(TextMesh t) => D<TextData>(t).Text;
    [Icall("UnityEngine.TextMesh::INTERNAL_set_color")] static void TmColor(TextMesh t, ref Color c) => D<TextData>(t).Color = c;
    [Icall("UnityEngine.GUIText::set_text")] static void GtText(GUIText t, string s) => D<TextData>(t).Text = s ?? "";
    [Icall("UnityEngine.GUIText::INTERNAL_set_color")] static void GtColor(GUIText t, ref Color c) => D<TextData>(t).Color = c;
}

/// uGUI's TextGenerator: a simple left-to-right layout with the dynamic font (no rich text).
static class TextGenIcalls {
    sealed class Gen { public List<UIVertex> Verts = new(); public List<UICharInfo> Chars = new(); public List<UILineInfo> Lines = new(); public Rect Extents; public int BestFit; }

    static Gen G(TextGenerator t) => Native.Handles.Data<Gen>(t);

    [Icall("UnityEngine.TextGenerator::Init")] static void Init(TextGenerator t) => Native.Handles.SetPtr(t, Native.Handles.New(new Gen()));
    [Icall("UnityEngine.TextGenerator::Dispose_cpp")] static void Dispose(TextGenerator t) { }
    [Icall("UnityEngine.TextGenerator::get_vertexCount")] static int VertexCount(TextGenerator t) => G(t).Verts.Count;
    [Icall("UnityEngine.TextGenerator::get_characterCount")] static int CharCount(TextGenerator t) => G(t).Chars.Count;
    [Icall("UnityEngine.TextGenerator::get_lineCount")] static int LineCount(TextGenerator t) => G(t).Lines.Count;
    [Icall("UnityEngine.TextGenerator::get_fontSizeUsedForBestFit")] static int BestFit(TextGenerator t) => G(t).BestFit;
    [Icall("UnityEngine.TextGenerator::INTERNAL_get_rectExtents")] static void Extents(TextGenerator t, ref Rect r) => r = G(t).Extents;
    [Icall("UnityEngine.TextGenerator::GetVerticesInternal")] static void Verts(TextGenerator t, object l) { var x = (List<UIVertex>)l; x.Clear(); x.AddRange(G(t).Verts); }
    [Icall("UnityEngine.TextGenerator::GetCharactersInternal")] static void Chars(TextGenerator t, object l) { var x = (List<UICharInfo>)l; x.Clear(); x.AddRange(G(t).Chars); }
    [Icall("UnityEngine.TextGenerator::GetLinesInternal")] static void Lines(TextGenerator t, object l) { var x = (List<UILineInfo>)l; x.Clear(); x.AddRange(G(t).Lines); }

    [Icall("UnityEngine.TextGenerator::INTERNAL_CALL_Populate_Internal_cpp")]
    static bool Populate(TextGenerator t, string str, Font font, ref Color color, int fontSize, float scale, float lineSpacing, FontStyle style,
                         bool rich, bool bestFit, int minSize, int maxSize, int vOverflow, int hOverflow, bool updateBounds,
                         TextAnchor anchor, float ex, float ey, float px, float py, bool outOfBounds) {
        var g = G(t);
        g.Verts.Clear(); g.Chars.Clear(); g.Lines.Clear();
        var f = Text.Fonts.Of(font);
        if (f == null || str == null) return false;
        int size = Math.Max(1, (int)(fontSize * (scale <= 0 ? 1 : scale)));
        g.BestFit = size;
        foreach (char c in str) f.Add(c, size, style, out _);
        int lineH = f.File != null ? (int)MathF.Round((f.File.Ascender - f.File.Descender + f.File.LineGap) * size / (float)f.File.UnitsPerEm * lineSpacing) : size;
        int ascent = f.File != null ? (int)MathF.Round(f.File.Ascender * size / (float)f.File.UnitsPerEm) : size;
        float x = 0, y = -ascent, maxX = 0;
        var c32 = (Color32)color;
        g.Lines.Add(new UILineInfo { startCharIdx = 0, height = lineH });
        for (int i = 0; i < str.Length; i++) {
            char ch = str[i];
            if (ch == '\n') { x = 0; y -= lineH; g.Lines.Add(new UILineInfo { startCharIdx = i + 1, height = lineH }); g.Chars.Add(new UICharInfo { cursorPos = new Vector2(x, y) }); continue; }
            if (!f.Glyphs.TryGetValue((ch, size, style), out var ci)) continue;
            g.Chars.Add(new UICharInfo { cursorPos = new Vector2(x, y), charWidth = ci.width });
            float x0 = x + ci.minX, x1 = x + ci.maxX, y0 = y + ci.minY, y1 = y + ci.maxY;
            void V(float vx, float vy, Vector2 uv) { var v = new UIVertex(); v.position = new Vector3(vx, vy, 0); v.color = c32; v.uv0 = uv; g.Verts.Add(v); }
            V(x0, y1, ci.uvTopLeft); V(x1, y1, ci.uvTopRight); V(x1, y0, ci.uvBottomRight); V(x0, y0, ci.uvBottomLeft);
            x += ci.advance;
            maxX = Math.Max(maxX, x);
        }
        // anchor: offset the block inside the extents
        float w = maxX, h = -y + (lineH - ascent);
        float ox = -ex * px + (anchor is TextAnchor.UpperCenter or TextAnchor.MiddleCenter or TextAnchor.LowerCenter ? (ex - w) / 2 : anchor is TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight ? ex - w : 0);
        float oy = ey * (1 - py) - (anchor is TextAnchor.MiddleLeft or TextAnchor.MiddleCenter or TextAnchor.MiddleRight ? (ey - h) / 2 : anchor is TextAnchor.LowerLeft or TextAnchor.LowerCenter or TextAnchor.LowerRight ? ey - h : 0);
        for (int i = 0; i < g.Verts.Count; i++) { var v = g.Verts[i]; v.position = new Vector3(v.position.x + ox, v.position.y + oy, 0); g.Verts[i] = v; }
        g.Extents = new Rect(ox, oy - h, w, h);
        return true;
    }
}
