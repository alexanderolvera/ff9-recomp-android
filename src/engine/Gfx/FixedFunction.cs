// Fixed-function ShaderLab passes (SetTexture combiners, Color, Lighting Off) as GLSL.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FF9Engine.Gfx;

public static class FixedFunctionGlsl {
    public static string AttrName(string usage) => usage switch { "position" => "aPos", "color" => "aColor", _ => "aUV" };

    public static (string vs, string fs) Generate(FixedFunction ff) {
        int n = ff.Stages.Count;
        var vs = new StringBuilder("#version 300 es\nprecision highp float;\nin vec4 aPos;\nin vec4 aColor;\nin vec4 aUV;\nuniform mat4 uMVP;\nuniform vec4 uColor;\nuniform int uUseColor;\n");
        vs.Append("uniform vec4 uST[4];\nout vec4 vColor;\n");
        for (int i = 0; i < n; i++) vs.Append($"out vec4 vUV{i};\n");
        vs.Append("void main() {\n  gl_Position = uMVP * vec4(aPos.xyz, 1.0);\n  vColor = uUseColor != 0 ? uColor : aColor;\n");
        for (int i = 0; i < n; i++) vs.Append($"  vUV{i} = vec4(aUV.xy * uST[{Math.Min(i, 3)}].xy + uST[{Math.Min(i, 3)}].zw, 0.0, 0.0);\n");
        vs.Append("}\n");

        var fs = new StringBuilder("#version 300 es\nprecision highp float;\nin vec4 vColor;\n");
        for (int i = 0; i < n; i++) fs.Append($"in vec4 vUV{i};\nuniform sampler2D s{i};\nuniform vec4 uConst{i};\n");
        fs.Append("layout(location = 0) out vec4 oC0;\nvoid main() {\n  vec4 prim = vColor;\n  vec4 prev = prim;\n");
        for (int i = 0; i < n; i++) {
            var (_, combine, _) = ff.Stages[i];
            fs.Append($"  vec4 tex{i} = texture(s{i}, vUV{i}.xy);\n");
            string rgb = combine, alpha = "";
            int comma = combine.IndexOf(',');
            if (comma >= 0) { rgb = combine[..comma]; alpha = combine[(comma + 1)..]; }
            if (rgb.Trim().Length == 0) rgb = "previous";
            string re = Expr(rgb, i);
            string ae = alpha.Trim().Length > 0 ? Expr(alpha, i) : re;
            fs.Append($"  {{ vec4 c = {re}; vec4 a = {ae}; prev = vec4(c.rgb, a.a); }}\n");
        }
        fs.Append("  oC0 = clamp(prev, 0.0, 1.0);\n}\n");
        return (vs.ToString(), fs.ToString());
    }

    static string Operand(string t, int i) {
        t = t.Trim();
        bool alpha = t.EndsWith(".a", StringComparison.OrdinalIgnoreCase) || t.EndsWith(" alpha", StringComparison.OrdinalIgnoreCase);
        bool inv = t.StartsWith("one -", StringComparison.OrdinalIgnoreCase) || t.StartsWith("1 -");
        if (inv) t = t[(t.IndexOf('-') + 1)..].Trim();
        string w = t.Split('.', ' ')[0].ToLowerInvariant();
        string v = w switch { "texture" => $"tex{i}", "previous" => "prev", "primary" => "prim", "constant" => $"uConst{i}", _ => "vec4(1.0)" };
        if (alpha) v = $"vec4({v}.a)";
        if (inv) v = $"(vec4(1.0) - {v})";
        return v;
    }

    static string Expr(string e, int i) {
        e = e.Trim();
        float scale = 1;
        if (e.EndsWith("Double", StringComparison.OrdinalIgnoreCase)) { scale = 2; e = e[..^6].Trim(); }
        else if (e.EndsWith("Quad", StringComparison.OrdinalIgnoreCase)) { scale = 4; e = e[..^4].Trim(); }
        string r;
        int lerp = e.IndexOf("lerp", StringComparison.OrdinalIgnoreCase);
        if (lerp >= 0) {
            string a = e[..lerp];
            int open = e.IndexOf('(', lerp), close = e.IndexOf(')', open);
            string f = e[(open + 1)..close], b = e[(close + 1)..];
            r = $"mix({Operand(b, i)}, {Operand(a, i)}, {Operand(f, i)}.a)";
        } else {
            // left-to-right: A [*|+|-] B [+|- C]
            var parts = new List<string>(); var ops = new List<char>();
            int st = 0;
            for (int k = 0; k < e.Length; k++)
                if (e[k] is '*' or '+' or '-' && !(e[k] == '-' && e[..k].TrimEnd().EndsWith("one", StringComparison.OrdinalIgnoreCase))) {
                    parts.Add(e[st..k]); ops.Add(e[k]); st = k + 1;
                }
            parts.Add(e[st..]);
            r = Operand(parts[0], i);
            for (int k = 0; k < ops.Count; k++) r = $"({r} {ops[k]} {Operand(parts[k + 1], i)})";
        }
        return scale == 1 ? r : $"({r} * {scale.ToString("0.0", CultureInfo.InvariantCulture)})";
    }

    [System.Runtime.InteropServices.DllImport("libGLESv3.so")] static extern unsafe void glUniformMatrix4fv(int loc, int count, byte transpose, float* v);
    [System.Runtime.InteropServices.DllImport("libGLESv3.so")] static extern void glUniform4f(int loc, float x, float y, float z, float w);

    public static unsafe void SetUniforms(GpuProgram gp, NMaterial? mat, Matrix4x4 mvp) {
        int l = GL.UniformLocation(gp.Id, "uMVP");
        float* m = (float*)&mvp;
        glUniformMatrix4fv(l, 1, 0, m);
        var ff = gp.FF!;
        Color c = new(1, 1, 1, 1);
        bool use = false;
        if (ff.MaterialColorProp != null && mat != null && mat.Vectors.TryGetValue(PropIds.Id(ff.MaterialColorProp), out var v)) { c = new Color(v.x, v.y, v.z, v.w); use = true; }
        else if (ff.ConstColor != null) { var p = Parse(ff.ConstColor); c = new Color(p.x, p.y, p.z, p.w); use = true; }
        glUniform4f(GL.UniformLocation(gp.Id, "uColor"), c.r, c.g, c.b, c.a);
        GL.glUniform1i(GL.UniformLocation(gp.Id, "uUseColor"), use ? 1 : 0);
        for (int i = 0; i < ff.Stages.Count; i++) {
            var (tex, _, constant) = ff.Stages[i];
            var st = mat != null && mat.TexST.TryGetValue(PropIds.Id(tex), out var s) ? s : new Vector4(1, 1, 0, 0);
            if (i < 4) glUniform4f(GL.UniformLocation(gp.Id, $"uST[{i}]"), st.x, st.y, st.z, st.w);
            Vector4 cc = new(1, 1, 1, 1);
            if (constant != null) {
                if (constant.StartsWith("[")) { if (mat != null && mat.Vectors.TryGetValue(PropIds.Id(constant.Trim('[', ']')), out var cv)) cc = cv; }
                else cc = Parse(constant);
            }
            glUniform4f(GL.UniformLocation(gp.Id, $"uConst{i}"), cc.x, cc.y, cc.z, cc.w);
        }
    }

    static Vector4 Parse(string s) {
        var p = s.Trim('(', ')').Split(',').Select(x => float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 1).ToArray();
        return new Vector4(p.ElementAtOrDefault(0), p.ElementAtOrDefault(1), p.ElementAtOrDefault(2), p.Length > 3 ? p[3] : 1);
    }
}

/// IMGUI (OnGUI): run after cameras; drawing comes later with the GUI internal calls.
public static class Gui {
    public static void OnGUI() { }
}
