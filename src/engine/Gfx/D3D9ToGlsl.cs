// Translates Unity's compiled D3D9 shader assembly (vs_2_0/vs_3_0/ps_2_0/ps_3_0 text, as stored in
// Unity 5.2 shader assets) to GLSL ES 3.00. Constants become a uniform vec4 array "cu", samplers
// "s0".."s15", vertex inputs attributes by usage, and VS outputs/PS inputs varyings by usage
// (vD0/vD1 colours, vT0..vT9 texcoords).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FF9Engine.Gfx;

public sealed class D3D9Program {
    public bool IsPixel;
    public int Major, Minor;
    public string Glsl = "";
    public int ConstCount;                                  // size of the cu[] uniform array
    public readonly Dictionary<int, string> Samplers = new();   // sampler register -> "2D"/"CUBE"/"3D"
    public readonly List<(int reg, string usage, int index)> Inputs = new();   // VS attributes
}

public static class D3D9ToGlsl {
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static D3D9Program Translate(string asm, Dictionary<int, string>? samplerDims = null) {
        var p = new D3D9Program();
        var lines = asm.Replace("\r", "").Split('\n').Select(l => { int c = l.IndexOf("//"); return (c >= 0 ? l[..c] : l).Trim(); })
            .Where(l => l.Length > 0).ToList();
        string ver = lines[0];
        p.IsPixel = ver.StartsWith("ps");
        var vparts = ver.Split('_');
        p.Major = int.Parse(vparts[1]);
        p.Minor = vparts[2] == "x" || vparts[2] == "sw" ? 0 : int.Parse(vparts[2]);
        var t = new Translator(p, samplerDims);
        for (int i = 1; i < lines.Count; i++) t.Line(lines[i]);
        p.Glsl = t.Finish();
        return p;
    }

    sealed class Translator {
        readonly D3D9Program p;
        readonly Dictionary<int, string>? samplerDims;
        readonly StringBuilder body = new();
        string CU => p.IsPixel ? "pc" : "vc";
        string SP => p.IsPixel ? "s" : "vs";
        readonly Dictionary<int, string> defs = new();          // def'd float constants
        readonly Dictionary<int, int[]> defis = new();
        readonly Dictionary<int, bool> defbs = new();
        readonly HashSet<int> temps = new();
        readonly SortedDictionary<string, string> varyIn = new(), varyOut = new();   // name -> type
        readonly Dictionary<int, string> vsOut = new();         // vs_3 o# -> varying name
        readonly Dictionary<int, string> psIn = new();          // ps_3 v# -> varying name
        int maxConst = -1;
        bool usesA0, usesAL, usesP0, usesRelative, writesDepth;
        readonly HashSet<int> colorOuts = new();
        int indent = 1;
        int loopDepth;

        public Translator(D3D9Program p, Dictionary<int, string>? dims) { this.p = p; samplerDims = dims; }

        void Emit(string s) => body.Append(new string(' ', indent * 2)).Append(s).Append('\n');

        public void Line(string line) {
            int sp = line.IndexOf(' ');
            string op = sp < 0 ? line : line[..sp];
            string rest = sp < 0 ? "" : line[(sp + 1)..];
            var args = SplitArgs(rest);
            string baseOp = op;
            bool sat = false;
            foreach (var mod in new[] { "_sat", "_pp", "_centroid", "_x2", "_x4", "_d2" }) {
                if (baseOp.Contains(mod)) { if (mod == "_sat") sat = true; baseOp = baseOp.Replace(mod, ""); }
            }
            if (baseOp.StartsWith("dcl")) { Dcl(baseOp, args); return; }
            switch (baseOp) {
                case "def": {
                    int r = int.Parse(args[0][1..]);
                    defs[r] = $"vec4({F(args[1])}, {F(args[2])}, {F(args[3])}, {F(args[4])})";
                    return;
                }
                case "defi": defis[int.Parse(args[0][1..])] = args.Skip(1).Select(a => int.Parse(a, Inv)).ToArray(); return;
                case "defb": defbs[int.Parse(args[0][1..])] = args[1] == "true"; return;
                case "nop": return;
            }
            Instr(baseOp, args, sat);
        }

        static string F(string s) {
            float v = float.Parse(s, NumberStyles.Float, Inv);
            if (float.IsPositiveInfinity(v)) return "1e38";
            if (float.IsNegativeInfinity(v)) return "-1e38";
            if (float.IsNaN(v)) return "0.0";
            string r = v.ToString("R", Inv);
            if (!r.Contains('.') && !r.Contains('E') && !r.Contains('e')) r += ".0";
            return r;
        }

        static List<string> SplitArgs(string s) {
            var l = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++) {
                if (s[i] == '[') depth++;
                else if (s[i] == ']') depth--;
                else if (s[i] == ',' && depth == 0) { l.Add(s[start..i].Trim()); start = i + 1; }
            }
            if (s.Trim().Length > 0) l.Add(s[start..].Trim());
            return l;
        }

        // ---- declarations ----
        void Dcl(string op, List<string> args) {
            string usage = op.Length > 4 ? op[4..] : "";    // dcl_position1 -> position1
            string reg = args.Count > 0 ? args[0] : "";
            string regName = reg.Split('.')[0];
            if (usage is "2d" or "cube" or "volume") {
                int s = int.Parse(regName[1..]);
                p.Samplers[s] = usage == "2d" ? "2D" : usage == "cube" ? "CUBE" : "3D";
                return;
            }
            string uname = new string(usage.TakeWhile(char.IsLetter).ToArray());
            int uidx = usage.Length > uname.Length ? int.Parse(usage[uname.Length..]) : 0;
            if (!p.IsPixel) {
                if (regName.StartsWith("v")) {
                    p.Inputs.Add((int.Parse(regName[1..]), uname, uidx));
                } else if (regName.StartsWith("o")) {          // vs_3 output
                    int o = int.Parse(regName[1..]);
                    vsOut[o] = VaryingName(uname, uidx);
                }
            } else {
                if (regName.StartsWith("v") && p.Major >= 3) psIn[int.Parse(regName[1..])] = VaryingName(uname, uidx);
                else if (regName.StartsWith("v")) varyIn["vD" + regName[1..]] = "vec4";
                else if (regName.StartsWith("t")) varyIn["vT" + regName[1..]] = "vec4";
            }
        }

        static string VaryingName(string usage, int idx) => usage switch {
            "color" => "vD" + idx,
            "texcoord" => "vT" + idx,
            "position" => "vPosOut",
            "fog" => "vFog",
            "psize" => "vPsize",
            "normal" => "vN" + idx,
            "tangent" => "vTan" + idx,
            _ => "v" + usage + idx,
        };

        // ---- operands ----
        string Reg(string name, out string? swz, out bool neg, out bool abs) {
            neg = false; abs = false; swz = null;
            string s = name;
            if (s.StartsWith("-")) { neg = true; s = s[1..]; }
            // swizzle
            int dot = s.LastIndexOf('.');
            if (dot > 0 && !s[(dot + 1)..].Any(c => !"xyzwrgba".Contains(c))) {
                swz = s[(dot + 1)..].Replace('r', 'x').Replace('g', 'y').Replace('b', 'z').Replace('a', 'w');
                s = s[..dot];
            }
            if (s.EndsWith("_abs")) { abs = true; s = s[..^4]; }
            return s;
        }

        string Src(string a) {
            string s = Reg(a, out var swz, out bool neg, out bool abs);
            string v = RegExpr(s, false);
            if (abs) v = $"abs({v})";
            if (swz != null) {
                if (swz.Length == 1) swz = new string(swz[0], 4);
                else if (swz.Length < 4) swz += new string(swz[^1], 4 - swz.Length);
                if (swz != "xyzw") v = $"{v}.{swz}";
            }
            if (neg) v = $"(-{v})";
            return v;
        }

        string RegExpr(string s, bool dest) {
            // relative constant: c12[a0.x] or c[a0.x + 12]
            int br = s.IndexOf('[');
            if (br >= 0) {
                string inner = s[(br + 1)..s.IndexOf(']')];
                string head = s[..br];
                int baseIdx = head.Length > 1 ? int.Parse(head[1..]) : 0;
                var parts = inner.Split('+').Select(x => x.Trim()).ToList();
                string idx = "";
                foreach (var part in parts) {
                    if (part.StartsWith("a0") || part.StartsWith("aL")) {
                        string comp = part.Contains('.') ? part[(part.IndexOf('.') + 1)..] : "x";
                        idx += (idx.Length > 0 ? " + " : "") + (part.StartsWith("aL") ? "aL" : $"a0.{comp}");
                        if (part.StartsWith("aL")) usesAL = true; else usesA0 = true;
                    } else baseIdx += int.Parse(part);
                }
                usesRelative = true;
                maxConst = Math.Max(maxConst, baseIdx + 1);
                return $"{CU}[{idx} + {baseIdx}]";
            }
            char k = s[0];
            string num = new string(s.Skip(1).TakeWhile(char.IsDigit).ToArray());
            int n = num.Length > 0 ? int.Parse(num) : 0;
            switch (k) {
                case 'r': temps.Add(n); return "r" + n;
                case 'c':
                    if (defs.TryGetValue(n, out _)) return "k" + n;
                    maxConst = Math.Max(maxConst, n);
                    return $"{CU}[{n}]";
                case 'v':
                    if (s == "vPos") return "vPos";
                    if (s == "vFace") return "vFace";
                    if (!p.IsPixel) return "a_v" + n;
                    if (p.Major >= 3) return psIn.TryGetValue(n, out var vn) ? vn : "vD" + n;
                    varyIn["vD" + n] = "vec4";
                    return "vD" + n;
                case 't':
                    varyIn["vT" + n] = "vec4";
                    return "vT" + n;
                case 'a':
                    if (s.StartsWith("aL")) { usesAL = true; return "vec4(aL)"; }
                    usesA0 = true;
                    return dest ? "a0" : "vec4(a0)";
                case 'p': usesP0 = true; return "p0";
                case 'b': return defbs.TryGetValue(n, out var bv) ? (bv ? "true" : "false") : "false";
                case 'i': return defis.TryGetValue(n, out var iv) ? $"ivec4({string.Join(", ", iv)})" : "ivec4(0)";
                case 'o':
                    if (s.StartsWith("oPos")) return "oPos";
                    if (s.StartsWith("oD")) { varyOut["vD" + n] = "vec4"; return "vD" + n; }
                    if (s.StartsWith("oT")) { varyOut["vT" + n] = "vec4"; return "vT" + n; }
                    if (s.StartsWith("oC")) { colorOuts.Add(n); return "oC" + n; }
                    if (s.StartsWith("oDepth")) { writesDepth = true; return "oDepth"; }
                    if (s.StartsWith("oFog")) return "oFog";
                    if (s.StartsWith("oPts")) return "oPts";
                    {
                        // vs_3 o#
                        string name = vsOut.TryGetValue(n, out var on) ? on : "vT" + n;
                        if (name == "vPosOut") return "oPos";
                        varyOut[name] = "vec4";
                        return name;
                    }
            }
            throw new NotSupportedException("register " + s);
        }

        (string expr, string mask) Dst(string a) {
            string s = a;
            string mask = "xyzw";
            int dot = s.LastIndexOf('.');
            if (dot > 0) { mask = s[(dot + 1)..].Replace('r', 'x').Replace('g', 'y').Replace('b', 'z').Replace('a', 'w'); s = s[..dot]; }
            return (RegExpr(s, true), mask);
        }

        void Assign(string dst, string vec4Expr, bool sat) {
            var (d, mask) = Dst(dst);
            string e = sat ? $"clamp({vec4Expr}, 0.0, 1.0)" : vec4Expr;
            if (d == "oDepth") { Emit($"oDepth = ({e}).x;"); return; }
            if (d == "oFog" || d == "oPts") return;
            if (d == "a0") { Emit($"a0.{mask} = ivec4(floor(({e}) + 0.5)).{mask};"); return; }
            if (mask == "xyzw") Emit($"{d} = {e};");
            else Emit($"{d}.{mask} = ({e}).{mask};");
        }

        string V(string scalar) => $"vec4({scalar})";

        // ---- instructions ----
        void Instr(string op, List<string> a, bool sat) {
            string S(int i) => Src(a[i]);
            string X(int i) => $"{Src(a[i])}.x";
            switch (op) {
                case "mov": Assign(a[0], S(1), sat); break;
                case "mova": Assign(a[0], S(1), false); break;
                case "add": Assign(a[0], $"{S(1)} + {S(2)}", sat); break;
                case "sub": Assign(a[0], $"{S(1)} - {S(2)}", sat); break;
                case "mul": Assign(a[0], $"{S(1)} * {S(2)}", sat); break;
                case "mad": Assign(a[0], $"{S(1)} * {S(2)} + {S(3)}", sat); break;
                case "dp3": Assign(a[0], V($"dot({S(1)}.xyz, {S(2)}.xyz)"), sat); break;
                case "dp4": Assign(a[0], V($"dot({S(1)}, {S(2)})"), sat); break;
                case "dp2add": Assign(a[0], V($"dot({S(1)}.xy, {S(2)}.xy) + {X(3)}"), sat); break;
                case "rcp": Assign(a[0], V($"1.0 / {X(1)}"), sat); break;
                case "rsq": Assign(a[0], V($"inversesqrt(abs({X(1)}))"), sat); break;
                case "exp": case "expp": Assign(a[0], V($"exp2({X(1)})"), sat); break;
                case "log": case "logp": Assign(a[0], V($"log2(abs({X(1)}))"), sat); break;
                case "pow": Assign(a[0], V($"pow(abs({X(1)}), {X(2)})"), sat); break;
                case "min": Assign(a[0], $"min({S(1)}, {S(2)})", sat); break;
                case "max": Assign(a[0], $"max({S(1)}, {S(2)})", sat); break;
                case "slt": Assign(a[0], $"vec4(lessThan({S(1)}, {S(2)}))", sat); break;
                case "sge": Assign(a[0], $"vec4(greaterThanEqual({S(1)}, {S(2)}))", sat); break;
                case "sgn": Assign(a[0], $"sign({S(1)})", sat); break;
                case "abs": Assign(a[0], $"abs({S(1)})", sat); break;
                case "frc": Assign(a[0], $"fract({S(1)})", sat); break;
                case "nrm": Assign(a[0], $"vec4(normalize({S(1)}.xyz), 0.0)", sat); break;   // w undefined in D3D9
                case "crs": Assign(a[0], $"vec4(cross({S(1)}.xyz, {S(2)}.xyz), 0.0)", sat); break;
                case "lrp": Assign(a[0], $"mix({S(3)}, {S(2)}, {S(1)})", sat); break;
                case "cmp": Assign(a[0], $"mix({S(3)}, {S(2)}, vec4(greaterThanEqual({S(1)}, vec4(0.0))))", sat); break;
                case "cnd": Assign(a[0], $"mix({S(3)}, {S(2)}, vec4(greaterThan({S(1)}, vec4(0.5))))", sat); break;
                case "dsx": Assign(a[0], $"dFdx({S(1)})", sat); break;
                case "dsy": Assign(a[0], $"dFdy({S(1)})", sat); break;
                case "sincos": {
                    var (d, mask) = Dst(a[0]);
                    string x = X(1);
                    if (mask.Contains('x')) Emit($"{d}.x = cos({x});");
                    if (mask.Contains('y')) Emit($"{d}.y = sin({x});");
                    break;
                }
                case "lit": {
                    string s = S(1);
                    Assign(a[0], $"vec4(1.0, max({s}.x, 0.0), ({s}.x > 0.0) ? pow(max({s}.y, 0.0), clamp({s}.w, -127.9961, 127.9961)) : 0.0, 1.0)", sat);
                    break;
                }
                case "dst": Assign(a[0], $"vec4(1.0, {S(1)}.y * {S(2)}.y, {S(1)}.z, {S(2)}.w)", sat); break;
                case "m4x4": M(a, 4, 4, sat); break;
                case "m4x3": M(a, 4, 3, sat); break;
                case "m3x4": M(a, 3, 4, sat); break;
                case "m3x3": M(a, 3, 3, sat); break;
                case "m3x2": M(a, 3, 2, sat); break;
                case "texld": case "texldp": case "texldb": case "texldl": case "texldd": Tex(op, a, sat); break;
                case "texkill": Emit($"if (any(lessThan({Src(a[0])}.xyz, vec3(0.0)))) discard;"); break;
                case "if": Emit($"if ({BoolSrc(a[0])}) {{"); indent++; break;
                case "else": indent--; Emit("} else {"); indent++; break;
                case "endif": indent--; Emit("}"); break;
                case "rep": {
                    string cnt = IntComp(a[0], 0);
                    Emit($"for (int rep{loopDepth} = 0; rep{loopDepth} < {cnt}; rep{loopDepth}++) {{");
                    loopDepth++; indent++;
                    break;
                }
                case "endrep": case "endloop": indent--; loopDepth--; Emit("}"); if (op == "endloop") Emit("aL = aLsave;"); break;
                case "loop": {
                    usesAL = true;
                    string ii = a[1];
                    string cnt = IntComp(ii, 0), start = IntComp(ii, 1), step = IntComp(ii, 2);
                    Emit($"aLsave = aL; aL = {start};");
                    Emit($"for (int rep{loopDepth} = 0; rep{loopDepth} < {cnt}; rep{loopDepth}++, aL += {step}) {{");
                    loopDepth++; indent++;
                    break;
                }
                case "break": Emit("break;"); break;
                case "breakp": Emit($"if ({PredSrc(a[0])}) break;"); break;
                case "ret": Emit(p.IsPixel ? "return;" : "gl_Position = oPos; return;"); break;
                default:
                    if (op.StartsWith("if_")) { Emit($"if ({X(0)} {Cmp(op[3..])} {X(1)}) {{"); indent++; break; }
                    if (op.StartsWith("break_")) { Emit($"if ({X(0)} {Cmp(op[6..])} {X(1)}) break;"); break; }
                    if (op.StartsWith("setp_")) {
                        usesP0 = true;
                        var (d, mask) = Dst(a[0]);
                        string c = Cmp(op[5..]);
                        foreach (char ch in mask) Emit($"p0.{ch} = {S(1)}.{ch} {c} {S(2)}.{ch};");
                        break;
                    }
                    throw new NotSupportedException("D3D9 instruction " + op);
            }
        }

        static string Cmp(string c) => c switch { "gt" => ">", "lt" => "<", "ge" => ">=", "le" => "<=", "eq" => "==", "ne" => "!=", _ => throw new NotSupportedException(c) };

        string BoolSrc(string a) {
            if (a.StartsWith("b")) return RegExpr(a, false);
            return PredSrc(a);
        }

        string PredSrc(string a) {
            usesP0 = true;
            bool neg = a.StartsWith("!");
            string s = neg ? a[1..] : a;
            string comp = s.Contains('.') ? s[(s.IndexOf('.') + 1)..][..1] : "x";
            return (neg ? "!" : "") + "p0." + comp;
        }

        string IntComp(string reg, int comp) {
            int n = int.Parse(reg[1..].Split('.')[0]);
            return defis.TryGetValue(n, out var v) ? v[comp].ToString(Inv) : "0";
        }

        void M(List<string> a, int rows, int cols, bool sat) {
            var (d, mask) = Dst(a[0]);
            string v = Src(a[1]);
            string sw = rows == 3 ? ".xyz" : "";
            var c = new List<string>();
            // the matrix operand is the first of 'cols' consecutive registers
            string m = a[2];
            var (mreg, _) = (m, 0);
            for (int i = 0; i < cols; i++) c.Add($"dot({v}{sw}, {Src(Offset(m, i))}{sw})");
            string comps = "xyzw";
            for (int i = 0; i < cols; i++)
                if (mask.Contains(comps[i])) Emit($"{d}.{comps[i]} = {(sat ? $"clamp({c[i]}, 0.0, 1.0)" : c[i])};");
        }

        static string Offset(string reg, int i) {
            if (i == 0) return reg;
            int br = reg.IndexOf('[');
            if (br >= 0) {
                string head = reg[..br];
                int n = head.Length > 1 ? int.Parse(head[1..]) : 0;
                return head[0] + (n + i).ToString(Inv) + reg[br..];
            }
            string num = new string(reg.Skip(1).TakeWhile(char.IsDigit).ToArray());
            return reg[0] + (int.Parse(num) + i).ToString(Inv) + reg[(1 + num.Length)..];
        }

        void Tex(string op, List<string> a, bool sat) {
            string coord = Src(a[1]);
            int s = int.Parse(a[2].Split('.')[0][1..]);
            string dim = p.Samplers.TryGetValue(s, out var dd) ? dd : samplerDims != null && samplerDims.TryGetValue(s, out var d2) ? d2 : "2D";
            p.Samplers[s] = dim;
            string c = dim == "2D" ? $"{coord}.xy" : $"{coord}.xyz";
            string e = op switch {
                "texldp" => dim == "2D" ? $"textureProj({SP}{s}, {coord}.xyw)" : $"texture({SP}{s}, {coord}.xyz / {coord}.w)",
                "texldb" => $"texture({SP}{s}, {c}, {coord}.w)",
                "texldl" => $"textureLod({SP}{s}, {c}, {coord}.w)",
                "texldd" => $"textureGrad({SP}{s}, {c}, {Src(a[3])}.{(dim == "2D" ? "xy" : "xyz")}, {Src(a[4])}.{(dim == "2D" ? "xy" : "xyz")})",
                _ => $"texture({SP}{s}, {c})",
            };
            Assign(a[0], e, sat);
        }

        public string Finish() {
            var sb = new StringBuilder();
            sb.Append("#version 300 es\nprecision highp float;\nprecision highp int;\n");
            int nconst = Math.Max(maxConst + 1, 1);
            if (usesRelative) nconst = p.IsPixel ? Math.Max(nconst, 32) : Math.Max(nconst, 64);
            p.ConstCount = nconst;
            sb.Append($"uniform vec4 {CU}[{nconst}];\n");
            foreach (var kv in p.Samplers.OrderBy(k => k.Key))
                sb.Append($"uniform {(kv.Value == "CUBE" ? "samplerCube" : kv.Value == "3D" ? "highp sampler3D" : "sampler2D")} {SP}{kv.Key};\n");
            if (!p.IsPixel) {
                foreach (var (reg, usage, idx) in p.Inputs) sb.Append($"in vec4 a_v{reg};\n");
                foreach (var kv in varyOut) sb.Append($"out {kv.Value} {kv.Key};\n");
            } else {
                foreach (var v in psIn.Values) varyIn[v] = "vec4";
                foreach (var kv in varyIn) sb.Append($"in {kv.Value} {kv.Key};\n");
                if (colorOuts.Count == 0) colorOuts.Add(0);
                foreach (var o in colorOuts.OrderBy(x => x)) sb.Append($"layout(location = {o}) out vec4 oC{o};\n");
            }
            sb.Append("void main() {\n");
            foreach (var kv in defs) sb.Append($"  const vec4 k{kv.Key} = {kv.Value};\n");
            foreach (var r in temps.OrderBy(x => x)) sb.Append($"  vec4 r{r} = vec4(0.0);\n");
            if (usesA0) sb.Append("  ivec4 a0 = ivec4(0);\n");
            if (usesAL) sb.Append("  int aL = 0; int aLsave = 0;\n");
            if (usesP0) sb.Append("  bvec4 p0 = bvec4(false);\n");
            if (!p.IsPixel) {
                sb.Append("  vec4 oPos = vec4(0.0);\n");
                foreach (var kv in varyOut) sb.Append($"  {kv.Key} = vec4(0.0);\n");
            } else {
                sb.Append("  vec4 vPos = vec4(gl_FragCoord.xy - 0.5, 0.0, 0.0);\n  vec4 vFace = vec4(gl_FrontFacing ? 1.0 : -1.0);\n");
                if (writesDepth) sb.Append("  float oDepth = gl_FragCoord.z;\n");
                foreach (var o in colorOuts) sb.Append($"  oC{o} = vec4(0.0);\n");
            }
            sb.Append(body);
            if (!p.IsPixel) sb.Append("  gl_Position = oPos;\n");
            else if (writesDepth) sb.Append("  gl_FragDepth = oDepth;\n");
            sb.Append("}\n");
            return sb.ToString();
        }
    }
}
