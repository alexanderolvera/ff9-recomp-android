// OpenType/TrueType font files: cmap, metrics, and glyph outlines (TrueType 'glyf' quadratic or
// CFF Type 2 charstrings, including CID-keyed CJK fonts), rasterized with signed-area coverage.
using System;
using System.Collections.Generic;
using System.Text;

namespace FF9Engine.Text;

public sealed class FontFile {
    readonly byte[] d;
    readonly Dictionary<string, int> tables = new();
    public int UnitsPerEm = 1000, Ascender, Descender, LineGap, NumGlyphs;
    int numHMetrics, locaFormat;
    readonly Dictionary<int, int> cmap = new();
    // CFF
    int cffBase, charStringsOff, gsubrsOff, subrsOff = -1, fdSelectOff = -1;
    readonly List<int> fdSubrs = new();
    int fdSelectFormat;
    bool isCff;

    public FontFile(byte[] data) {
        d = data;
        int n = U16(4);
        for (int i = 0; i < n; i++) {
            int r = 12 + i * 16;
            string tag = Encoding.ASCII.GetString(d, r, 4);
            tables[tag] = (int)U32(r + 8);
        }
        int head = tables["head"];
        UnitsPerEm = U16(head + 18);
        locaFormat = S16(head + 50);
        int hhea = tables["hhea"];
        Ascender = S16(hhea + 4); Descender = S16(hhea + 6); LineGap = S16(hhea + 8);
        numHMetrics = U16(hhea + 34);
        NumGlyphs = U16(tables["maxp"] + 4);
        if (tables.TryGetValue("OS/2", out int os2) && U16(os2) >= 0) {
            // typo metrics are what most layout uses for CJK fonts; keep hhea unless zero
            if (Ascender == 0) { Ascender = S16(os2 + 68); Descender = S16(os2 + 70); }
        }
        ReadCmap();
        if (tables.TryGetValue("CFF ", out int cff)) { isCff = true; ReadCff(cff); }
    }

    int U16(int o) => d[o] << 8 | d[o + 1];
    int S16(int o) => (short)(d[o] << 8 | d[o + 1]);
    uint U32(int o) => (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);

    void ReadCmap() {
        int c = tables["cmap"];
        int n = U16(c + 2);
        int best = -1, bestFmt = 0;
        for (int i = 0; i < n; i++) {
            int pid = U16(c + 4 + i * 8), eid = U16(c + 6 + i * 8);
            int off = c + (int)U32(c + 8 + i * 8);
            int fmt = U16(off);
            if (pid == 3 && eid == 10 && fmt == 12 || pid == 0 && fmt == 12) { best = off; bestFmt = 12; }
            else if ((pid == 3 && eid == 1 || pid == 0) && fmt == 4 && bestFmt != 12) { best = off; bestFmt = 4; }
        }
        if (best < 0) return;
        if (bestFmt == 4) {
            int segX2 = U16(best + 6);
            int ends = best + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
            for (int s = 0; s < segX2 / 2; s++) {
                int end = U16(ends + s * 2), start = U16(starts + s * 2), delta = S16(deltas + s * 2), ro = U16(ranges + s * 2);
                for (int ch = start; ch <= end && ch != 0xFFFF; ch++) {
                    int g;
                    if (ro == 0) g = (ch + delta) & 0xFFFF;
                    else {
                        int gi = ranges + s * 2 + ro + (ch - start) * 2;
                        g = U16(gi);
                        if (g != 0) g = (g + delta) & 0xFFFF;
                    }
                    if (g != 0) cmap[ch] = g;
                }
            }
        } else {
            int groups = (int)U32(best + 12);
            for (int i = 0; i < groups; i++) {
                int o = best + 16 + i * 12;
                uint s = U32(o), e = U32(o + 4), g = U32(o + 8);
                for (uint ch = s; ch <= e; ch++) cmap[(int)ch] = (int)(g + ch - s);
            }
        }
    }

    public int Glyph(int ch) => cmap.TryGetValue(ch, out int g) ? g : 0;
    public bool Has(int ch) => cmap.ContainsKey(ch);

    public int Advance(int g) {
        int h = tables["hmtx"];
        return U16(h + Math.Min(g, numHMetrics - 1) * 4);
    }

    // ---- outlines: flattened to line segments in font units ----
    public sealed class Outline {
        public readonly List<(float x0, float y0, float x1, float y1)> Lines = new();
        public float MinX = float.MaxValue, MinY = float.MaxValue, MaxX = float.MinValue, MaxY = float.MinValue;
        float cx, cy, sx, sy;
        bool open;

        public void Move(float x, float y) { Close(); cx = sx = x; cy = sy = y; open = true; Ext(x, y); }
        public void Line(float x, float y) { if (cx != x || cy != y) Lines.Add((cx, cy, x, y)); cx = x; cy = y; Ext(x, y); }
        public void Quad(float x1, float y1, float x, float y) {
            const int N = 8;
            float x0 = cx, y0 = cy;
            for (int i = 1; i <= N; i++) { float t = i / (float)N, u = 1 - t; Line(u * u * x0 + 2 * u * t * x1 + t * t * x, u * u * y0 + 2 * u * t * y1 + t * t * y); }
        }
        public void Cubic(float x1, float y1, float x2, float y2, float x, float y) {
            const int N = 10;
            float x0 = cx, y0 = cy;
            for (int i = 1; i <= N; i++) {
                float t = i / (float)N, u = 1 - t;
                Line(u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x, u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y);
            }
        }
        public void Close() { if (open && (cx != sx || cy != sy)) Lines.Add((cx, cy, sx, sy)); open = false; cx = sx; cy = sy; }
        void Ext(float x, float y) { MinX = Math.Min(MinX, x); MinY = Math.Min(MinY, y); MaxX = Math.Max(MaxX, x); MaxY = Math.Max(MaxY, y); }
    }

    public Outline GetOutline(int g) {
        var o = new Outline();
        if (isCff) Cff(g, o);
        else Glyf(g, o, 1, 0, 0, 1, 0, 0, 0);
        o.Close();
        return o;
    }

    void Glyf(int g, Outline o, float a, float b, float c, float dd, float e, float f, int depth) {
        if (depth > 8 || !tables.ContainsKey("glyf")) return;
        int loca = tables["loca"], glyf = tables["glyf"];
        int start, end;
        if (locaFormat == 0) { start = U16(loca + g * 2) * 2; end = U16(loca + g * 2 + 2) * 2; }
        else { start = (int)U32(loca + g * 4); end = (int)U32(loca + g * 4 + 4); }
        if (end <= start) return;
        int p = glyf + start;
        int nc = S16(p);
        if (nc >= 0) {
            var ends = new int[nc];
            for (int i = 0; i < nc; i++) ends[i] = U16(p + 10 + i * 2);
            int np = nc > 0 ? ends[nc - 1] + 1 : 0;
            int ilen = U16(p + 10 + nc * 2);
            int q = p + 12 + nc * 2 + ilen;
            var flags = new byte[np];
            for (int i = 0; i < np;) {
                byte fl = d[q++];
                flags[i++] = fl;
                if ((fl & 8) != 0) { int r = d[q++]; while (r-- > 0 && i < np) flags[i++] = fl; }
            }
            var xs = new int[np]; var ys = new int[np];
            int v = 0;
            for (int i = 0; i < np; i++) {
                byte fl = flags[i];
                if ((fl & 2) != 0) { int dx = d[q++]; v += (fl & 16) != 0 ? dx : -dx; }
                else if ((fl & 16) == 0) { v += S16(q); q += 2; }
                xs[i] = v;
            }
            v = 0;
            for (int i = 0; i < np; i++) {
                byte fl = flags[i];
                if ((fl & 4) != 0) { int dy = d[q++]; v += (fl & 32) != 0 ? dy : -dy; }
                else if ((fl & 32) == 0) { v += S16(q); q += 2; }
                ys[i] = v;
            }
            (float, float) T(int i) => (a * xs[i] + c * ys[i] + e, b * xs[i] + dd * ys[i] + f);
            int s = 0;
            for (int ci = 0; ci < nc; ci++) {
                int en = ends[ci];
                int count = en - s + 1;
                if (count <= 0) { s = en + 1; continue; }
                // find a start point that is on-curve, or synthesize one
                int first = -1;
                for (int k = 0; k < count; k++) if ((flags[s + k] & 1) != 0) { first = k; break; }
                float startX, startY;
                if (first >= 0) (startX, startY) = T(s + first);
                else { var (x0, y0) = T(s); var (x1, y1) = T(s + 1 < s + count ? s + 1 : s); startX = (x0 + x1) / 2; startY = (y0 + y1) / 2; first = 0; }
                o.Move(startX, startY);
                bool haveCtl = false; float ctlX = 0, ctlY = 0;
                for (int k = 1; k <= count; k++) {
                    int idx = s + (first + k) % count;
                    var (x, y) = T(idx);
                    bool on = (flags[idx] & 1) != 0;
                    if (on) {
                        if (haveCtl) o.Quad(ctlX, ctlY, x, y); else o.Line(x, y);
                        haveCtl = false;
                    } else {
                        if (haveCtl) { float mx = (ctlX + x) / 2, my = (ctlY + y) / 2; o.Quad(ctlX, ctlY, mx, my); }
                        ctlX = x; ctlY = y; haveCtl = true;
                    }
                }
                if (haveCtl) o.Quad(ctlX, ctlY, startX, startY);
                o.Close();
                s = en + 1;
            }
        } else {
            int q = p + 10;
            while (true) {
                int fl = U16(q), gi = U16(q + 2);
                q += 4;
                float dx, dy;
                if ((fl & 1) != 0) { dx = S16(q); dy = S16(q + 2); q += 4; } else { dx = (sbyte)d[q]; dy = (sbyte)d[q + 1]; q += 2; }
                float na = 1, nb = 0, nc2 = 0, nd = 1;
                if ((fl & 8) != 0) { na = nd = S16(q) / 16384f; q += 2; }
                else if ((fl & 0x40) != 0) { na = S16(q) / 16384f; nd = S16(q + 2) / 16384f; q += 4; }
                else if ((fl & 0x80) != 0) { na = S16(q) / 16384f; nb = S16(q + 2) / 16384f; nc2 = S16(q + 4) / 16384f; nd = S16(q + 6) / 16384f; q += 8; }
                // compose: outer(inner)
                Glyf(gi, o, a * na + c * nb, b * na + dd * nb, a * nc2 + c * nd, b * nc2 + dd * nd, a * dx + c * dy + e, b * dx + dd * dy + f, depth + 1);
                if ((fl & 0x20) == 0) break;
            }
        }
    }

    // ---- CFF ----
    (int count, int offSize, int dataStart, int end) Index(int p) {
        int count = U16(p);
        if (count == 0) return (0, 0, p + 2, p + 2);
        int offSize = d[p + 2];
        int offs = p + 3;
        int dataStart = offs + (count + 1) * offSize - 1;
        int last = Off(offs + count * offSize, offSize);
        return (count, offSize, dataStart, dataStart + last);
    }

    int Off(int p, int size) { int v = 0; for (int i = 0; i < size; i++) v = v << 8 | d[p + i]; return v; }

    (int start, int end) IndexItem(int p, int i) {
        var (count, os, ds, _) = Index(p);
        if (i < 0 || i >= count) return (0, 0);
        int offs = p + 3;
        return (ds + Off(offs + i * os, os), ds + Off(offs + (i + 1) * os, os));
    }

    Dictionary<int, List<double>> Dict(int s, int e) {
        var r = new Dictionary<int, List<double>>();
        var ops = new List<double>();
        int p = s;
        while (p < e) {
            int b0 = d[p];
            if (b0 <= 21) {
                int op = b0; p++;
                if (b0 == 12) { op = 1200 + d[p]; p++; }
                r[op] = ops; ops = new();
            } else if (b0 == 28) { ops.Add((short)(d[p + 1] << 8 | d[p + 2])); p += 3; }
            else if (b0 == 29) { ops.Add((int)U32(p + 1)); p += 5; }
            else if (b0 == 30) {
                p++;
                var sb = new StringBuilder();
                bool done = false;
                while (!done) {
                    int bb = d[p++];
                    foreach (int nib in new[] { bb >> 4, bb & 15 }) {
                        if (nib == 15) { done = true; break; }
                        sb.Append(nib switch { 10 => ".", 11 => "E", 12 => "E-", 14 => "-", 13 => "", _ => nib.ToString() });
                    }
                }
                ops.Add(double.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv) ? dv : 0);
            }
            else if (b0 >= 32 && b0 <= 246) { ops.Add(b0 - 139); p++; }
            else if (b0 >= 247 && b0 <= 250) { ops.Add((b0 - 247) * 256 + d[p + 1] + 108); p += 2; }
            else if (b0 >= 251 && b0 <= 254) { ops.Add(-(b0 - 251) * 256 - d[p + 1] - 108); p += 2; }
            else p++;
        }
        return r;
    }

    void ReadCff(int c) {
        cffBase = c;
        int hdrSize = d[c + 2];
        int nameIdx = c + hdrSize;
        int topIdx = Index(nameIdx).end;
        var (ts, te) = IndexItem(topIdx, 0);
        var top = Dict(ts, te);
        int strIdx = Index(topIdx).end;
        int gsub = Index(strIdx).end;
        gsubrsOff = gsub;
        charStringsOff = c + (int)top[17][0];
        if (top.TryGetValue(18, out var priv)) subrsOff = PrivateSubrs(c, (int)priv[0], (int)priv[1]);
        if (top.ContainsKey(1230)) {       // ROS: CID-keyed
            int fdArray = c + (int)top[1236][0];
            fdSelectOff = c + (int)top[1237][0];
            fdSelectFormat = d[fdSelectOff];
            var (count, _, _, _) = Index(fdArray);
            for (int i = 0; i < count; i++) {
                var (fs, fe) = IndexItem(fdArray, i);
                var fd = Dict(fs, fe);
                fdSubrs.Add(fd.TryGetValue(18, out var fp) ? PrivateSubrs(c, (int)fp[0], (int)fp[1]) : -1);
            }
        }
    }

    int PrivateSubrs(int c, int size, int off) {
        var pd = Dict(c + off, c + off + size);
        return pd.TryGetValue(19, out var so) ? c + off + (int)so[0] : -1;
    }

    int FdOf(int g) {
        if (fdSelectOff < 0) return -1;
        if (fdSelectFormat == 0) return d[fdSelectOff + 1 + g];
        int n = U16(fdSelectOff + 1);
        for (int i = 0; i < n; i++) {
            int r = fdSelectOff + 3 + i * 3;
            int first = U16(r), next = U16(r + 3);
            if (g >= first && g < next) return d[r + 2];
        }
        return 0;
    }

    static int Bias(int count) => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

    void Cff(int g, Outline o) {
        var (cs, ce) = IndexItem(charStringsOff, g);
        if (ce <= cs) return;
        int local = subrsOff;
        if (fdSelectOff >= 0) { int fd = FdOf(g); local = fd >= 0 && fd < fdSubrs.Count ? fdSubrs[fd] : -1; }
        var st = new List<float>();
        int nStems = 0;
        bool widthDone = false, open = false;
        float x = 0, y = 0;
        int depth = 0;
        void Run(int s, int e) {
            if (++depth > 12) return;
            int p = s;
            while (p < e) {
                int b = d[p];
                if (b >= 32 || b == 28) {
                    if (b == 28) { st.Add((short)(d[p + 1] << 8 | d[p + 2])); p += 3; }
                    else if (b <= 246) { st.Add(b - 139); p++; }
                    else if (b <= 250) { st.Add((b - 247) * 256 + d[p + 1] + 108); p += 2; }
                    else if (b <= 254) { st.Add(-(b - 251) * 256 - d[p + 1] - 108); p += 2; }
                    else { st.Add((int)U32(p + 1) / 65536f); p += 5; }
                    continue;
                }
                p++;
                void Width(int expectEven) { if (!widthDone) { widthDone = true; if (st.Count % 2 != expectEven) st.RemoveAt(0); } }
                switch (b) {
                    case 1: case 3: case 18: case 23: Width(0); nStems += st.Count / 2; st.Clear(); break;
                    case 19: case 20:
                        Width(0); nStems += st.Count / 2; st.Clear();
                        p += (nStems + 7) / 8;
                        break;
                    case 21: if (!widthDone) { widthDone = true; if (st.Count > 2) st.RemoveAt(0); } x += st[0]; y += st[1]; o.Move(x, y); open = true; st.Clear(); break;
                    case 22: if (!widthDone) { widthDone = true; if (st.Count > 1) st.RemoveAt(0); } x += st[0]; o.Move(x, y); open = true; st.Clear(); break;
                    case 4: if (!widthDone) { widthDone = true; if (st.Count > 1) st.RemoveAt(0); } y += st[0]; o.Move(x, y); open = true; st.Clear(); break;
                    case 5: for (int i = 0; i + 1 < st.Count; i += 2) { x += st[i]; y += st[i + 1]; o.Line(x, y); } st.Clear(); break;
                    case 6: case 7: {
                        bool horiz = b == 6;
                        foreach (var v in st) { if (horiz) x += v; else y += v; o.Line(x, y); horiz = !horiz; }
                        st.Clear(); break;
                    }
                    case 8: for (int i = 0; i + 5 < st.Count; i += 6) Curve(st[i], st[i + 1], st[i + 2], st[i + 3], st[i + 4], st[i + 5]); st.Clear(); break;
                    case 24: {
                        int i = 0;
                        for (; i + 5 < st.Count - 2; i += 6) Curve(st[i], st[i + 1], st[i + 2], st[i + 3], st[i + 4], st[i + 5]);
                        x += st[i]; y += st[i + 1]; o.Line(x, y); st.Clear(); break;
                    }
                    case 25: {
                        int i = 0;
                        for (; i + 1 < st.Count - 6; i += 2) { x += st[i]; y += st[i + 1]; o.Line(x, y); }
                        Curve(st[i], st[i + 1], st[i + 2], st[i + 3], st[i + 4], st[i + 5]); st.Clear(); break;
                    }
                    case 26: {
                        int i = 0; float dx1 = 0;
                        if (st.Count % 2 == 1) { dx1 = st[0]; i = 1; }
                        for (; i + 3 < st.Count; i += 4) { Curve(dx1, st[i], st[i + 1], st[i + 2], 0, st[i + 3]); dx1 = 0; }
                        st.Clear(); break;
                    }
                    case 27: {
                        int i = 0; float dy1 = 0;
                        if (st.Count % 2 == 1) { dy1 = st[0]; i = 1; }
                        for (; i + 3 < st.Count; i += 4) { Curve(st[i], dy1, st[i + 1], st[i + 2], st[i + 3], 0); dy1 = 0; }
                        st.Clear(); break;
                    }
                    case 30: case 31: {
                        bool hFirst = b == 31;
                        int i = 0;
                        while (i + 3 < st.Count) {
                            bool last = i + 4 >= st.Count - 1;
                            float extra = last && st.Count - i == 5 ? st[i + 4] : 0;
                            if (hFirst) Curve(st[i], 0, st[i + 1], st[i + 2], extra, st[i + 3]);
                            else Curve(0, st[i], st[i + 1], st[i + 2], st[i + 3], extra);
                            hFirst = !hFirst;
                            i += 4;
                        }
                        st.Clear(); break;
                    }
                    case 10: case 29: {
                        int idx = (int)st[^1]; st.RemoveAt(st.Count - 1);
                        int sub = b == 10 ? local : gsubrsOff;
                        if (sub < 0) break;
                        int cnt = U16(sub);
                        var (ss, se) = IndexItem(sub, idx + Bias(cnt));
                        Run(ss, se);
                        break;
                    }
                    case 11: depth--; return;
                    case 14: if (!widthDone) { widthDone = true; if (st.Count % 2 == 1) st.RemoveAt(0); } if (open) o.Close(); st.Clear(); depth = 99; return;
                    case 12: {
                        int b2 = d[p++];
                        switch (b2) {
                            case 35: Curve(st[0], st[1], st[2], st[3], st[4], st[5]); Curve(st[6], st[7], st[8], st[9], st[10], st[11]); break;
                            case 34: { float y0 = y; Curve(st[0], 0, st[1], st[2], st[3], 0); Curve(st[4], 0, st[5], y0 - y, st[6], 0); break; }
                            case 36: { float y0 = y; Curve(st[0], st[1], st[2], st[3], st[4], 0); Curve(st[5], 0, st[6], st[7], st[8], y0 - y); break; }
                            case 37: {
                                float sx = x, sy = y;
                                float dx = st[0] + st[2] + st[4] + st[6] + st[8], dy = st[1] + st[3] + st[5] + st[7] + st[9];
                                bool hz = Math.Abs(dx) > Math.Abs(dy);
                                Curve(st[0], st[1], st[2], st[3], st[4], st[5]);
                                float ex = hz ? st[10] : sx - x - st[6] - st[8], ey = hz ? sy - y - st[7] - st[9] : st[10];
                                if (hz) Curve(st[6], st[7], st[8], st[9], st[10], sy - (y + st[7] + st[9])); else Curve(st[6], st[7], st[8], st[9], sx - (x + st[6] + st[8]), st[10]);
                                break;
                            }
                        }
                        st.Clear(); break;
                    }
                    default: st.Clear(); break;
                }
                if (depth >= 99) return;
            }
            depth--;
        }
        void Curve(float dx1, float dy1, float dx2, float dy2, float dx3, float dy3) {
            float x1 = x + dx1, y1 = y + dy1, x2 = x1 + dx2, y2 = y1 + dy2;
            x = x2 + dx3; y = y2 + dy3;
            o.Cubic(x1, y1, x2, y2, x, y);
        }
        Run(cs, ce);
    }

    /// Rasterizes a glyph at a pixel size: coverage bytes (rows top to bottom), with the bitmap's
    /// left/top bearing relative to the pen position and baseline (y up).
    public byte[] Render(int g, float pixelSize, out int w, out int h, out int left, out int top) {
        var o = GetOutline(g);
        float s = pixelSize / UnitsPerEm;
        if (o.Lines.Count == 0) { w = h = left = top = 0; return Array.Empty<byte>(); }
        left = (int)MathF.Floor(o.MinX * s);
        int right = (int)MathF.Ceiling(o.MaxX * s);
        top = (int)MathF.Ceiling(o.MaxY * s);
        int bottom = (int)MathF.Floor(o.MinY * s);
        w = Math.Max(1, right - left);
        h = Math.Max(1, top - bottom);
        int aw = w + 3;
        var acc = new float[aw * h];
        foreach (var (x0, y0, x1, y1) in o.Lines)
            AccLine(acc, aw, h, x0 * s - left, top - y0 * s, x1 * s - left, top - y1 * s);
        var outp = new byte[w * h];
        for (int y = 0; y < h; y++) {
            float sum = 0;
            for (int x = 0; x < aw; x++) {
                sum += acc[y * aw + x];
                if (x < w) { float a = MathF.Abs(sum); outp[y * w + x] = (byte)(a >= 1 ? 255 : a * 255); }
            }
        }
        return outp;
    }

    /// font-rs style signed-area accumulation of one line (pixel coordinates, y down).
    static void AccLine(float[] acc, int w, int h, float x0, float y0, float x1, float y1) {
        if (y0 == y1) return;
        float dir = 1;
        if (y0 > y1) { dir = -1; (x0, x1) = (x1, x0); (y0, y1) = (y1, y0); }
        float dxdy = (x1 - x0) / (y1 - y0);
        float x = x0;
        if (y0 < 0) { x -= y0 * dxdy; y0 = 0; }
        if (y1 > h) y1 = h;
        if (y0 >= y1) return;
        int Clamp(int v) => Math.Clamp(v, 0, w - 1);
        for (int y = (int)y0; y < (int)MathF.Ceiling(y1); y++) {
            float dy = Math.Min(y + 1, y1) - Math.Max(y, y0);
            float xnext = x + dxdy * dy;
            float d = dy * dir;
            float xa = Math.Max(0, Math.Min(x, xnext)), xb = Math.Max(0, Math.Max(x, xnext));
            int ls = y * w;
            int x0i = (int)xa;
            if (xb <= x0i + 1) {
                float xm = 0.5f * (Math.Max(0, x) + Math.Max(0, xnext)) - x0i;
                acc[ls + Clamp(x0i)] += d - d * xm;
                acc[ls + Clamp(x0i + 1)] += d * xm;
            } else {
                float sc = 1 / (xb - xa);
                float x0f = xa - x0i;
                float a0 = 0.5f * sc * (1 - x0f) * (1 - x0f);
                int x1i = (int)MathF.Ceiling(xb);
                float x1f = xb - x1i + 1;
                float am = 0.5f * sc * x1f * x1f;
                acc[ls + Clamp(x0i)] += d * a0;
                if (x1i == x0i + 2) acc[ls + Clamp(x0i + 1)] += d * (1 - a0 - am);
                else {
                    float a1 = sc * (1.5f - x0f);
                    acc[ls + Clamp(x0i + 1)] += d * (a1 - a0);
                    for (int xi = x0i + 2; xi < x1i - 1; xi++) acc[ls + Clamp(xi)] += d * sc;
                    float a2 = a1 + (x1i - x0i - 3) * sc;
                    acc[ls + Clamp(x1i - 1)] += d * (1 - a2 - am);
                }
                acc[ls + Clamp(x1i)] += d * am;
            }
            x = xnext;
        }
    }
}
