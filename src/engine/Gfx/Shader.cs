// Shader assets: Unity 5.2 stores the compiled ShaderLab text (passes, render state, and per-keyword
// D3D9 programs as assembly). Parsed here; programs are translated to GLSL and linked on first use.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using FF9Engine.Assets;
using FF9Engine.Core;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace FF9Engine.Gfx;

public static class PropIds {
    static readonly Dictionary<string, int> ids = new();
    static readonly List<string> names = new() { "" };

    public static int Id(string name) {
        if (ids.TryGetValue(name, out int id)) return id;
        id = names.Count;
        names.Add(name);
        ids[name] = id;
        return id;
    }

    public static string Name(int id) => id > 0 && id < names.Count ? names[id] : "";
}

/// A value that is either a constant or a reference to a material property ("[_SrcBlend]").
public struct StateValue {
    public string? Prop;
    public string Const;
    public StateValue(string s) { if (s.StartsWith("[")) { Prop = s.Trim('[', ']'); Const = ""; } else { Prop = null; Const = s; } }
    public string Resolve(NMaterial? m) {
        if (Prop == null) return Const;
        float f = m?.GetFloat(PropIds.Id(Prop)) ?? Globals.Float(PropIds.Id(Prop));
        return f.ToString(CultureInfo.InvariantCulture);
    }
}

public sealed class PassState {
    public StateValue Cull = new("Back"), ZWrite = new("On"), ZTest = new("LEqual");
    public StateValue[]? Blend;            // src, dst, (srcA, dstA)
    public StateValue BlendOp = new("Add");
    public string ColorMask = "RGBA";
    public float OffsetFactor, OffsetUnits;
    public bool HasOffset;
    public string? AlphaTestFunc; public StateValue AlphaTestRef;
    public bool StencilEnabled;
    public StateValue StencilRef = new("0"), StencilComp = new("Always"), StencilPass = new("Keep"), StencilFail = new("Keep"), StencilZFail = new("Keep");
    public StateValue StencilReadMask = new("255"), StencilWriteMask = new("255");
    public PassState Clone() => (PassState)MemberwiseClone();
}

public sealed class SubProgram {
    public HashSet<string> Keywords = new();
    public string Asm = "";
    public readonly List<(string kind, int reg, string name, int rows)> Consts = new();   // Matrix/Vector/Float
    public readonly List<(int reg, string name, string dim)> Textures = new();
    public D3D9Program? Translated;
}

public sealed class ShaderPass {
    public string Name = "";
    public readonly Dictionary<string, string> Tags = new(StringComparer.OrdinalIgnoreCase);
    public PassState State = new();
    public readonly List<SubProgram> Vp = new(), Fp = new();
    public FixedFunction? FF;
    public bool IsGrabPass;
    public string? GrabTextureName;
}

public sealed class FixedFunction {
    public readonly List<(string tex, string combine, string? constColor)> Stages = new();
    public bool Lighting;
    public string? MaterialColorProp;     // Color [_Color] / Material { Diffuse [_Color] }
    public string? ConstColor;
}

public sealed class NShader : NObject {
    public string Source = "";
    public readonly List<ShaderPass> Passes = new();
    public readonly Dictionary<string, string> Tags = new(StringComparer.OrdinalIgnoreCase);
    public int RenderQueue = 2000;
    public string? Fallback;
    public readonly Dictionary<string, (string type, string def)> Properties = new();
    public bool Parsed;
    public bool Supported = true;
}

public static class Shaders {
    public static readonly Dictionary<string, NShader> ByName = new(StringComparer.Ordinal);

    public static void Register() {
        AssetDB.Readers[ClassIds.Shader] = (a, r) => {
            a.Name = r.String();
            string script = r.String();
            var s = new NShader { Source = script };
            a.Data = s;
            Parse(s);                 // s.Name: the ShaderLab name ("Unlit/Transparent Colored")
            ByName[s.Name] = s;
        };
    }

    public static NShader? Of(UnityEngine.Shader? sh) => Registry.Get(sh) switch {
        NAsset { Data: NShader s } => s,
        NShader s => s,
        _ => null,
    };

    /// Shader.Find: shaders in the loaded files, then the built-in resource files.
    public static NShader? Find(string name) {
        if (ByName.TryGetValue(name, out var s)) return s;
        foreach (var fname in new[] { "resources.assets", "library/unity default resources", "resources/unity_builtin_extra", "sharedassets0.assets", "sharedassets1.assets", "sharedassets2.assets", "sharedassets3.assets" }) {
            var f = AssetDB.File(fname);
            if (f == null) continue;
            foreach (var o in f.ObjectList)
                if (o.ClassId == ClassIds.Shader && AssetDB.Loaded(f, o.PathId) == null) {
                    var r = f.Reader(o);
                    string n = r.String();
                    string sc = r.String();
                    int q = sc.IndexOf('"');
                    string sname = q >= 0 ? sc[(q + 1)..sc.IndexOf('"', q + 1)] : n;
                    if (sname == name) { AssetDB.Load(f, o.PathId); if (ByName.TryGetValue(name, out s)) return s; }
                }
        }
        return null;
    }

    // ---- ShaderLab ----
    sealed class Tok {
        readonly string s; int p;
        public Tok(string s) { this.s = s; }
        public bool End { get { Skip(); return p >= s.Length; } }

        void Skip() {
            while (p < s.Length) {
                if (char.IsWhiteSpace(s[p])) { p++; continue; }
                if (s[p] == '/' && p + 1 < s.Length && s[p + 1] == '/') { while (p < s.Length && s[p] != '\n') p++; continue; }
                break;
            }
        }

        public string Peek() { int save = p; string t = Next(); p = save; return t; }

        public string Next() {
            Skip();
            if (p >= s.Length) return "";
            char c = s[p];
            if (c == '"') {
                int e = s.IndexOf('"', p + 1);
                string str = s[p..(e + 1)];
                p = e + 1;
                return str;
            }
            if (c == '[') { int e = s.IndexOf(']', p); string t = s[p..(e + 1)]; p = e + 1; return t; }
            if ("{}(),=".IndexOf(c) >= 0) { p++; return c.ToString(); }
            int st = p;
            while (p < s.Length && !char.IsWhiteSpace(s[p]) && "{}(),=\"[".IndexOf(s[p]) < 0) p++;
            return s[st..p];
        }

        public void Expect(string t) { string n = Next(); if (n != t) throw new FormatException($"ShaderLab: expected {t}, got {n}"); }

        public void SkipBlock() {
            Expect("{");
            int depth = 1;
            while (depth > 0 && !End) { string t = Next(); if (t == "{") depth++; else if (t == "}") depth--; }
        }

        public string RestOfLine() {
            int e = s.IndexOf('\n', p);
            if (e < 0) e = s.Length;
            string r = s[p..e];
            p = e;
            return r.Trim();
        }
    }

    static string Unq(string s) => s.Length >= 2 && s[0] == '"' ? s[1..^1] : s;

    public static void Parse(NShader sh) {
        try {
            var t = new Tok(sh.Source);
            t.Expect("Shader");
            sh.Name = Unq(t.Next());
            t.Expect("{");
            bool gotSub = false;
            while (!t.End) {
                string k = t.Next();
                if (k == "}") break;
                switch (k) {
                    case "Properties": ParseProperties(t, sh); break;
                    case "SubShader":
                        if (gotSub) { t.SkipBlock(); break; }
                        gotSub = true;
                        ParseSubShader(t, sh);
                        break;
                    case "Fallback": case "FallBack": sh.Fallback = Unq(t.Next()); break;
                    case "CustomEditor": case "Dependency": t.RestOfLine(); break;
                    default: t.RestOfLine(); break;
                }
            }
            sh.Parsed = true;
        } catch (Exception e) {
            Log.Warn($"shader {sh.Name}: {e.Message}");
            sh.Supported = false;
        }
    }

    static void ParseProperties(Tok t, NShader sh) {
        t.Expect("{");
        while (!t.End) {
            string n = t.Next();
            if (n == "}") break;
            while (n.StartsWith("[")) n = t.Next();     // attributes
            string line = t.RestOfLine();
            // ("Display", Type) = default
            int c1 = line.LastIndexOf(')');
            int eq = line.IndexOf('=', Math.Max(0, c1));
            string def = eq >= 0 ? line[(eq + 1)..].Trim() : "";
            int comma = line.LastIndexOf(',', c1 >= 0 ? c1 : line.Length - 1);
            string type = comma >= 0 && c1 > comma ? line[(comma + 1)..c1].Trim() : "";
            sh.Properties[n] = (type, def);
        }
    }

    static void ParseTags(Tok t, Dictionary<string, string> tags) {
        t.Expect("{");
        while (!t.End) {
            string k = t.Next();
            if (k == "}") break;
            t.Expect("=");
            tags[Unq(k)] = Unq(t.Next());
        }
    }

    static void ParseSubShader(Tok t, NShader sh) {
        t.Expect("{");
        var state = new PassState();
        while (!t.End) {
            string k = t.Next();
            if (k == "}") break;
            switch (k) {
                case "Tags": ParseTags(t, sh.Tags); break;
                case "LOD": t.Next(); break;
                case "Pass": {
                    var p = new ShaderPass { State = state.Clone() };
                    ParsePass(t, p);
                    sh.Passes.Add(p);
                    break;
                }
                case "UsePass": {
                    string path = Unq(t.Next());
                    int slash = path.LastIndexOf('/');
                    var other = Find(path[..slash]);
                    var pass = other?.Passes.FirstOrDefault(x => x.Name.Equals(path[(slash + 1)..], StringComparison.OrdinalIgnoreCase));
                    if (pass != null) sh.Passes.Add(pass);
                    else Log.Once("usepass:" + path, $"UsePass {path} not found");
                    break;
                }
                case "GrabPass": {
                    var p = new ShaderPass { IsGrabPass = true };
                    t.Expect("{");
                    while (!t.End) { string g = t.Next(); if (g == "}") break; if (g.StartsWith("\"")) p.GrabTextureName = Unq(g); else if (g == "Tags") { ParseTags(t, p.Tags); } }
                    sh.Passes.Add(p);
                    break;
                }
                default:
                    if (!StateCommand(t, k, state, null)) t.RestOfLine();
                    break;
            }
        }
        if (sh.Tags.TryGetValue("Queue", out var q)) sh.RenderQueue = QueueValue(q);
    }

    public static int QueueValue(string q) {
        int add = 0;
        int pm = q.IndexOfAny(new[] { '+', '-' });
        if (pm > 0) { add = int.Parse(q[pm..], CultureInfo.InvariantCulture); q = q[..pm]; }
        int b = q.ToLowerInvariant() switch {
            "background" => 1000, "geometry" => 2000, "alphatest" => 2450, "transparent" => 3000, "overlay" => 4000,
            _ => int.TryParse(q, out int v) ? v : 2000,
        };
        return b + add;
    }

    static bool StateCommand(Tok t, string k, PassState s, ShaderPass? pass) {
        switch (k) {
            case "Cull": s.Cull = new StateValue(t.Next()); return true;
            case "ZWrite": s.ZWrite = new StateValue(t.Next()); return true;
            case "ZTest": s.ZTest = new StateValue(t.Next()); return true;
            case "Blend": {
                string a = t.Next();
                if (a == "Off") { s.Blend = null; return true; }
                string b = t.Next();
                var list = new List<StateValue> { new(a), new(b) };
                if (t.Peek() == ",") { t.Next(); list.Add(new(t.Next())); list.Add(new(t.Next())); }
                s.Blend = list.ToArray();
                return true;
            }
            case "BlendOp": s.BlendOp = new StateValue(t.Next()); if (t.Peek() == ",") { t.Next(); t.Next(); } return true;
            case "ColorMask": s.ColorMask = t.Next(); return true;
            case "Offset": {
                string a = t.Next(); t.Expect(","); string b = t.Next();
                s.HasOffset = true;
                float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out s.OffsetFactor);
                float.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out s.OffsetUnits);
                return true;
            }
            case "AlphaTest": {
                string f = t.Next();
                if (f == "Off") { s.AlphaTestFunc = null; return true; }
                s.AlphaTestFunc = f;
                s.AlphaTestRef = new StateValue(t.Peek().StartsWith("[") || char.IsDigit(t.Peek()[0]) || t.Peek()[0] == '.' ? t.Next() : "0");
                return true;
            }
            case "Stencil": {
                s.StencilEnabled = true;
                t.Expect("{");
                while (!t.End) {
                    string sk = t.Next();
                    if (sk == "}") break;
                    var v = new StateValue(t.Next());
                    switch (sk) {
                        case "Ref": s.StencilRef = v; break;
                        case "Comp": s.StencilComp = v; break;
                        case "Pass": s.StencilPass = v; break;
                        case "Fail": s.StencilFail = v; break;
                        case "ZFail": s.StencilZFail = v; break;
                        case "ReadMask": s.StencilReadMask = v; break;
                        case "WriteMask": s.StencilWriteMask = v; break;
                    }
                }
                return true;
            }
            case "Fog": case "BindChannels": case "Material": case "SeparateSpecular": case "ColorMaterial":
                if (k == "Material" && pass != null) {
                    t.Expect("{");
                    while (!t.End) { string mk = t.Next(); if (mk == "}") break; string mv = t.Next(); if (mk == "Diffuse" || mk == "Ambient") (pass.FF ??= new FixedFunction()).MaterialColorProp = mv.Trim('[', ']'); }
                    return true;
                }
                if (t.Peek() == "{") t.SkipBlock(); else t.Next();
                return true;
            case "Lighting": { string v = t.Next(); if (pass != null && v == "On") (pass.FF ??= new FixedFunction()).Lighting = true; return true; }
            case "Color": {
                string v = t.Next();
                if (pass != null) {
                    var ff = pass.FF ??= new FixedFunction();
                    if (v.StartsWith("[")) ff.MaterialColorProp = v.Trim('[', ']');
                    else if (v == "(") { var sb = new StringBuilder("("); string x; while ((x = t.Next()) != ")") sb.Append(x); ff.ConstColor = sb.Append(')').ToString(); }
                }
                return true;
            }
            case "GpuProgramID": case "Name" when pass == null: t.Next(); return true;
        }
        return false;
    }

    static void ParsePass(Tok t, ShaderPass p) {
        t.Expect("{");
        while (!t.End) {
            string k = t.Next();
            if (k == "}") break;
            switch (k) {
                case "Name": p.Name = Unq(t.Next()); break;
                case "Tags": ParseTags(t, p.Tags); break;
                case "Program": {
                    string kind = Unq(t.Next());
                    t.Expect("{");
                    while (!t.End) {
                        string sk = t.Next();
                        if (sk == "}") break;
                        if (sk != "SubProgram") continue;
                        string api = Unq(t.Next());
                        var sp = ParseSubProgram(t);
                        if (api.Trim() == "d3d9") (kind == "vp" ? p.Vp : p.Fp).Add(sp);
                    }
                    break;
                }
                case "SetTexture": {
                    string tex = t.Next().Trim('[', ']');
                    t.Expect("{");
                    string combine = "", constant = "";
                    while (!t.End) {
                        string sk = t.Next();
                        if (sk == "}") break;
                        if (sk == "combine") combine = t.RestOfLine();
                        else if (sk == "constantColor") { var sb = new StringBuilder(); string x = t.Next(); if (x == "(") { while ((x = t.Next()) != ")") sb.Append(x); constant = "(" + sb + ")"; } else constant = x; }
                        else if (sk == "Matrix") t.Next();
                    }
                    (p.FF ??= new FixedFunction()).Stages.Add((tex, combine, constant.Length > 0 ? constant : null));
                    break;
                }
                default:
                    if (!StateCommand(t, k, p.State, p)) t.RestOfLine();
                    break;
            }
        }
        if (p.Vp.Count == 0 && p.Fp.Count == 0) p.FF ??= new FixedFunction();
    }

    static SubProgram ParseSubProgram(Tok t) {
        var sp = new SubProgram();
        t.Expect("{");
        while (!t.End) {
            string k = t.Next();
            if (k == "}") break;
            if (k.StartsWith("\"")) { sp.Asm = Unq(k); continue; }
            switch (k) {
                case "Keywords": {
                    t.Expect("{");
                    while (!t.End) { string kw = t.Next(); if (kw == "}") break; sp.Keywords.Add(Unq(kw)); }
                    break;
                }
                case "Bind": t.Next(); t.Next(); break;
                case "Matrix": case "Vector": case "Float": case "Integer": case "Bool": {
                    int reg = int.Parse(t.Next(), CultureInfo.InvariantCulture);
                    string name = t.Next().Trim('[', ']');
                    int rows = k == "Matrix" ? 4 : 1;
                    string pk = t.Peek();
                    if (pk.Length > 0 && char.IsDigit(pk[0]) && k == "Matrix") rows = int.Parse(t.Next(), CultureInfo.InvariantCulture);
                    sp.Consts.Add((k, reg, name, rows));
                    break;
                }
                case "SetTexture": {
                    int reg = int.Parse(t.Next(), CultureInfo.InvariantCulture);
                    string name = t.Next().Trim('[', ']');
                    string dim = t.Next();
                    t.Next();
                    sp.Textures.Add((reg, name, dim));
                    break;
                }
                default: break;
            }
        }
        sp.Keywords.Remove("DUMMY");
        return sp;
    }

    // ---- keyword variants ----
    public static SubProgram? Choose(List<SubProgram> list, IReadOnlyCollection<string> active) {
        SubProgram? best = null;
        int bestScore = -1;
        foreach (var sp in list) {
            int score = 0;
            bool ok = true;
            foreach (var k in sp.Keywords) {
                if (active.Contains(k)) score++;
                else if (!k.EndsWith("_OFF") && k != "DIRECTIONAL" && !k.StartsWith("LIGHTMAP_OFF") && !k.StartsWith("DIRLIGHTMAP_OFF") && !k.StartsWith("DYNAMICLIGHTMAP_OFF")) { ok = false; break; }
            }
            if (ok && score > bestScore) { best = sp; bestScore = score; }
        }
        return best ?? list.FirstOrDefault();
    }
}

static class ShaderIcalls {
    [Icall("UnityEngine.Shader::Find")]
    static UnityEngine.Shader? Find(string name) {
        var s = Shaders.Find(name);
        if (s == null) { Log.Once("shaderfind:" + name, $"Shader.Find(\"{name}\"): not found"); return null; }
        foreach (var n in Registry.All) if (n is NAsset { Data: NShader x } && x == s) return (UnityEngine.Shader)n.Managed!;
        return null;
    }

    [Icall("UnityEngine.Shader::get_isSupported")] static bool IsSupported(UnityEngine.Shader s) => Shaders.Of(s)?.Supported ?? false;
    [Icall("UnityEngine.Shader::PropertyToID")] static int PropertyToID(string name) => PropIds.Id(name);
    [Icall("UnityEngine.Shader::INTERNAL_CALL_SetGlobalColor")] static void SetGlobalColor(int id, ref Color c) => Globals.Vectors[id] = new Vector4(c.r, c.g, c.b, c.a);
    [Icall("UnityEngine.Shader::INTERNAL_CALL_SetGlobalVector")] static void SetGlobalVector(int id, ref Vector4 v) => Globals.Vectors[id] = v;
    [Icall("UnityEngine.Shader::SetGlobalFloat")] static void SetGlobalFloat(int id, float v) => Globals.Vectors[id] = new Vector4(v, v, v, v);
    [Icall("UnityEngine.Shader::SetGlobalTexture")] static void SetGlobalTexture(int id, Texture t) => Globals.Textures[id] = t;
    [Icall("UnityEngine.Shader::INTERNAL_CALL_SetGlobalMatrix")] static void SetGlobalMatrix(int id, ref Matrix4x4 m) => Globals.Matrices[id] = m;
    [Icall("UnityEngine.Shader::EnableKeyword")] static void EnableKeyword(string k) => Globals.Keywords.Add(k);
    [Icall("UnityEngine.Shader::DisableKeyword")] static void DisableKeyword(string k) => Globals.Keywords.Remove(k);
    [Icall("UnityEngine.Shader::IsKeywordEnabled")] static bool IsKeywordEnabled(string k) => Globals.Keywords.Contains(k);
    [Icall("UnityEngine.Shader::get_maximumLOD")] static int GetMaxLod(UnityEngine.Shader s) => 600;
    [Icall("UnityEngine.Shader::set_maximumLOD")] static void SetMaxLod(UnityEngine.Shader s, int v) { }
    [Icall("UnityEngine.Shader::get_globalMaximumLOD")] static int GetGMaxLod() => 600;
    [Icall("UnityEngine.Shader::set_globalMaximumLOD")] static void SetGMaxLod(int v) { }
    [Icall("UnityEngine.Shader::get_renderQueue")] static int RenderQueue(UnityEngine.Shader s) => Shaders.Of(s)?.RenderQueue ?? 2000;
    [Icall("UnityEngine.Shader::WarmupAllShaders")] static void Warmup() { }
}

public static class Globals {
    public static readonly Dictionary<int, Vector4> Vectors = new();
    public static readonly Dictionary<int, Matrix4x4> Matrices = new();
    public static readonly Dictionary<int, Texture?> Textures = new();
    public static readonly HashSet<string> Keywords = new();
    public static float Float(int id) => Vectors.TryGetValue(id, out var v) ? v.x : 0;
}
