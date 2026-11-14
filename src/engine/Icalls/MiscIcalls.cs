// Screen, SystemInfo, QualitySettings, RenderSettings, PlayerPrefs, Random, math helpers,
// Profiler, Caching.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FF9Engine.Core;
using UnityEngine;

namespace FF9Engine.Icalls;

public static class Display {
    public static int Width = 1920, Height = 1080;
    public static float Dpi = 401;
}

static class ScreenIcalls {
    [Icall("UnityEngine.Screen::get_width")] static int W() => Display.Width;
    [Icall("UnityEngine.Screen::get_height")] static int H() => Display.Height;
    [Icall("UnityEngine.Screen::get_dpi")] static float Dpi() => Display.Dpi;
    [Icall("UnityEngine.Screen::get_orientation")] static ScreenOrientation GetOr() => ScreenOrientation.LandscapeLeft;
    [Icall("UnityEngine.Screen::set_orientation")] static void SetOr(ScreenOrientation o) { }
    [Icall("UnityEngine.Screen::set_sleepTimeout")] static void Sleep(int v) { }
    [Icall("UnityEngine.Screen::get_sleepTimeout")] static int GetSleep() => -1;
    [Icall("UnityEngine.Screen::get_fullScreen")] static bool Full() => true;
    [Icall("UnityEngine.Screen::set_fullScreen")] static void SetFull(bool v) { }
    [Icall("UnityEngine.Screen::SetResolution")] static void SetRes(int w, int h, bool full, int hz) => Log.Once("SetResolution", $"Screen.SetResolution({w}, {h}) ignored: the display is {Display.Width}x{Display.Height}");
    [Icall("UnityEngine.Screen::get_currentResolution")] static Resolution Current() => Res();
    [Icall("UnityEngine.Screen::get_resolutions")] static Resolution[] All() => new[] { Res() };
    [Icall("UnityEngine.Screen::get_showCursor")] static bool GetCursor() => false;
    [Icall("UnityEngine.Screen::set_showCursor")] static void SetCursor(bool v) { }
    [Icall("UnityEngine.Cursor::get_visible")] static bool CursorVisible() => false;
    [Icall("UnityEngine.Cursor::set_visible")] static void SetCursorVisible(bool v) { }

    static Resolution Res() {
        var r = new Resolution();
        r.width = Display.Width; r.height = Display.Height; r.refreshRate = 60;
        return r;
    }
}

static class SystemInfoIcalls {
    [Icall("UnityEngine.SystemInfo::get_operatingSystem")] static string OS() => "Android (FF9Engine)";
    [Icall("UnityEngine.SystemInfo::get_processorType")] static string Cpu() => "ARM64";
    [Icall("UnityEngine.SystemInfo::get_processorCount")] static int Cores() => Environment.ProcessorCount;
    [Icall("UnityEngine.SystemInfo::get_systemMemorySize")] static int Mem() => 12000;
    [Icall("UnityEngine.SystemInfo::get_graphicsMemorySize")] static int GMem() => 4096;
    [Icall("UnityEngine.SystemInfo::get_graphicsDeviceName")] static string GName() => "Adreno";
    [Icall("UnityEngine.SystemInfo::get_graphicsDeviceVendor")] static string GVendor() => "Qualcomm";
    [Icall("UnityEngine.SystemInfo::get_graphicsDeviceID")] static int GId() => 0;
    [Icall("UnityEngine.SystemInfo::get_graphicsDeviceVendorID")] static int GVid() => 0x5143;
    [Icall("UnityEngine.SystemInfo::get_graphicsDeviceVersion")] static string GVer() => "Vulkan (FF9Engine)";
    [Icall("UnityEngine.SystemInfo::get_graphicsShaderLevel")] static int Level() => 30;
    [Icall("UnityEngine.SystemInfo::get_supportsShadows")] static bool Shadows() => false;
    [Icall("UnityEngine.SystemInfo::get_supportsRenderTextures")] static bool RT() => true;
    [Icall("UnityEngine.SystemInfo::get_supportsRenderToCubemap")] static bool RTC() => false;
    [Icall("UnityEngine.SystemInfo::get_supportsImageEffects")] static bool IE() => true;
    [Icall("UnityEngine.SystemInfo::get_supports3DTextures")] static bool T3() => false;
    [Icall("UnityEngine.SystemInfo::get_supportsComputeShaders")] static bool CS() => false;
    [Icall("UnityEngine.SystemInfo::get_supportsInstancing")] static bool Inst() => false;
    [Icall("UnityEngine.SystemInfo::get_supportedRenderTargetCount")] static int MRT() => 1;
    [Icall("UnityEngine.SystemInfo::get_supportsStencil")] static int Stencil() => 1;
    [Icall("UnityEngine.SystemInfo::SupportsRenderTextureFormat")] static bool RTF(RenderTextureFormat f) => f is RenderTextureFormat.ARGB32 or RenderTextureFormat.Default or RenderTextureFormat.Depth;
    [Icall("UnityEngine.SystemInfo::get_npotSupport")] static NPOTSupport Npot() => NPOTSupport.Full;
    [Icall("UnityEngine.SystemInfo::get_deviceName")] static string DevName() => "Thor";
    [Icall("UnityEngine.SystemInfo::get_deviceModel")] static string DevModel() => "AYN Thor";
    [Icall("UnityEngine.SystemInfo::get_deviceUniqueIdentifier")] static string DevId() => "ff9engine";
    [Icall("UnityEngine.SystemInfo::get_supportsAccelerometer")] static bool Acc() => false;
    [Icall("UnityEngine.SystemInfo::get_supportsGyroscope")] static bool Gyro() => false;
    [Icall("UnityEngine.SystemInfo::get_supportsLocationService")] static bool Loc() => false;
    [Icall("UnityEngine.SystemInfo::get_supportsVibration")] static bool Vib() => false;
    [Icall("UnityEngine.SystemInfo::get_deviceType")] static DeviceType DevType() => DeviceType.Desktop;

    [Icall("UnityEngine.Profiler::GetMonoHeapSize")] static uint Heap() => (uint)GC.GetTotalMemory(false);
    [Icall("UnityEngine.Profiler::GetTotalAllocatedMemory")] static uint Alloc() => (uint)GC.GetTotalMemory(false);
    [Icall("UnityEngine.Profiler::GetTotalReservedMemory")] static uint Res() => (uint)GC.GetTotalMemory(false);

    [Icall("UnityEngine.Caching::CleanCache")] static bool Clean() => true;
    [Icall("UnityEngine.Caching::INTERNAL_CALL_IsVersionCached")] static bool Cached(string url, ref Hash128 h) => false;
    [Icall("UnityEngine.Caching::get_ready")] static bool Ready() => true;
    [Icall("UnityEngine.Debug::INTERNAL_CALL_DrawLine")] static void DrawLine(ref Vector3 a, ref Vector3 b, ref Color c, float d, bool t) { }
    [Icall("UnityEngine.Debug::Break")] static void Break() { }
}

public static class RenderState {
    public static bool Fog;
    public static FogMode FogMode = FogMode.Linear;
    public static Color FogColor, AmbientLight = new(0.2f, 0.2f, 0.2f, 1);
    public static float FogDensity = 0.01f, FogStart = 0, FogEnd = 300, AmbientIntensity = 1;
    public static UnityEngine.Rendering.AmbientMode AmbientMode;
    public static Material? Skybox;
    public static int VSync = 1, AntiAliasing;
}

static class RenderSettingsIcalls {
    [Icall("UnityEngine.RenderSettings::get_fog")] static bool GetFog() => RenderState.Fog;
    [Icall("UnityEngine.RenderSettings::set_fog")] static void SetFog(bool v) => RenderState.Fog = v;
    [Icall("UnityEngine.RenderSettings::get_fogMode")] static FogMode GetFogMode() => RenderState.FogMode;
    [Icall("UnityEngine.RenderSettings::set_fogMode")] static void SetFogMode(FogMode m) => RenderState.FogMode = m;
    [Icall("UnityEngine.RenderSettings::INTERNAL_get_fogColor")] static void GetFogColor(ref Color c) => c = RenderState.FogColor;
    [Icall("UnityEngine.RenderSettings::INTERNAL_set_fogColor")] static void SetFogColor(ref Color c) => RenderState.FogColor = c;
    [Icall("UnityEngine.RenderSettings::get_fogDensity")] static float GetFogDensity() => RenderState.FogDensity;
    [Icall("UnityEngine.RenderSettings::set_fogDensity")] static void SetFogDensity(float v) => RenderState.FogDensity = v;
    [Icall("UnityEngine.RenderSettings::get_fogStartDistance")] static float GetFogStart() => RenderState.FogStart;
    [Icall("UnityEngine.RenderSettings::set_fogStartDistance")] static void SetFogStart(float v) => RenderState.FogStart = v;
    [Icall("UnityEngine.RenderSettings::get_fogEndDistance")] static float GetFogEnd() => RenderState.FogEnd;
    [Icall("UnityEngine.RenderSettings::set_fogEndDistance")] static void SetFogEnd(float v) => RenderState.FogEnd = v;
    [Icall("UnityEngine.RenderSettings::set_ambientMode")] static void SetAmbientMode(UnityEngine.Rendering.AmbientMode m) => RenderState.AmbientMode = m;
    [Icall("UnityEngine.RenderSettings::INTERNAL_set_ambientLight")] static void SetAmbient(ref Color c) => RenderState.AmbientLight = c;
    [Icall("UnityEngine.RenderSettings::INTERNAL_get_ambientLight")] static void GetAmbient(ref Color c) => c = RenderState.AmbientLight;
    [Icall("UnityEngine.RenderSettings::set_ambientIntensity")] static void SetAmbientIntensity(float v) => RenderState.AmbientIntensity = v;
    [Icall("UnityEngine.RenderSettings::set_skybox")] static void SetSkybox(Material m) => RenderState.Skybox = m;
    [Icall("UnityEngine.QualitySettings::get_vSyncCount")] static int GetVSync() => RenderState.VSync;
    [Icall("UnityEngine.QualitySettings::set_vSyncCount")] static void SetVSync(int v) => RenderState.VSync = v;
    [Icall("UnityEngine.QualitySettings::set_antiAliasing")] static void SetAA(int v) => RenderState.AntiAliasing = v;
    [Icall("UnityEngine.QualitySettings::get_antiAliasing")] static int GetAA() => RenderState.AntiAliasing;
    [Icall("UnityEngine.QualitySettings::get_activeColorSpace")] static ColorSpace ColorSpaceIcall() => ColorSpace.Gamma;
}

/// PlayerPrefs, stored as a simple text file in the persistent data folder.
static class PlayerPrefsIcalls {
    static Dictionary<string, object>? prefs;
    static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), "persistent", "playerprefs.txt");

    static Dictionary<string, object> P {
        get {
            if (prefs != null) return prefs;
            prefs = new();
            if (File.Exists(FilePath))
                foreach (var line in File.ReadAllLines(FilePath)) {
                    var p = line.Split('\t', 3);
                    if (p.Length < 3) continue;
                    string k = Unescape(p[1]);
                    prefs[k] = p[0] switch {
                        "i" => int.Parse(p[2], CultureInfo.InvariantCulture),
                        "f" => float.Parse(p[2], CultureInfo.InvariantCulture),
                        _ => Unescape(p[2]),
                    };
                }
            return prefs;
        }
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");
    static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");

    static void Save() {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var sb = new StringBuilder();
        foreach (var kv in P) {
            string t = kv.Value is int ? "i" : kv.Value is float ? "f" : "s";
            string v = kv.Value is float f ? f.ToString("R", CultureInfo.InvariantCulture) : kv.Value is int i ? i.ToString(CultureInfo.InvariantCulture) : Escape((string)kv.Value);
            sb.Append(t).Append('\t').Append(Escape(kv.Key)).Append('\t').Append(v).Append('\n');
        }
        File.WriteAllText(FilePath, sb.ToString());
    }

    [Icall("UnityEngine.PlayerPrefs::TrySetInt")] static bool SetInt(string k, int v) { P[k] = v; Save(); return true; }
    [Icall("UnityEngine.PlayerPrefs::TrySetFloat")] static bool SetFloat(string k, float v) { P[k] = v; Save(); return true; }
    [Icall("UnityEngine.PlayerPrefs::TrySetSetString")] static bool SetString(string k, string v) { P[k] = v ?? ""; Save(); return true; }
    [Icall("UnityEngine.PlayerPrefs::GetInt")] static int GetInt(string k, int d) => P.TryGetValue(k, out var v) && v is int i ? i : d;
    [Icall("UnityEngine.PlayerPrefs::GetFloat")] static float GetFloat(string k, float d) => P.TryGetValue(k, out var v) && v is float f ? f : d;
    [Icall("UnityEngine.PlayerPrefs::GetString")] static string GetString(string k, string d) => P.TryGetValue(k, out var v) && v is string s ? s : d;
    [Icall("UnityEngine.PlayerPrefs::HasKey")] static bool HasKey(string k) => P.ContainsKey(k);
    [Icall("UnityEngine.PlayerPrefs::DeleteKey")] static void DeleteKey(string k) { P.Remove(k); Save(); }
    [Icall("UnityEngine.PlayerPrefs::DeleteAll")] static void DeleteAll() { P.Clear(); Save(); }
    [Icall("UnityEngine.PlayerPrefs::Save")] static void SaveIcall() => Save();
}

static class RandomIcalls {
    static System.Random rng = new();
    static int seed;
    [Icall("UnityEngine.Random::get_seed")] static int GetSeed() => seed;
    [Icall("UnityEngine.Random::set_seed")] static void SetSeed(int v) { seed = v; rng = new System.Random(v); }
    [Icall("UnityEngine.Random::Range")] static float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    [Icall("UnityEngine.Random::RandomRangeInt")] static int RangeInt(int a, int b) => b <= a ? a : rng.Next(a, b);
    [Icall("UnityEngine.Random::get_value")] static float Value() => (float)rng.NextDouble();
    [Icall("UnityEngine.Random::INTERNAL_get_insideUnitSphere")]
    static void InSphere(ref Vector3 v) { do v = new Vector3(Value() * 2 - 1, Value() * 2 - 1, Value() * 2 - 1); while (v.x * v.x + v.y * v.y + v.z * v.z > 1); }
    [Icall("UnityEngine.Random::INTERNAL_get_insideUnitCircle")]
    static void InCircle(ref Vector2 v) { do v = new Vector2(Value() * 2 - 1, Value() * 2 - 1); while (v.x * v.x + v.y * v.y > 1); }
}

static class MathIcalls {
    const float Deg = MathF.PI / 180;

    [Icall("UnityEngine.Quaternion::INTERNAL_CALL_AngleAxis")]
    static Quaternion AngleAxis(float angle, ref Vector3 axis) => M.Axis(axis, angle * Deg);

    [Icall("UnityEngine.Quaternion::INTERNAL_CALL_FromToRotation")]
    static Quaternion FromTo(ref Vector3 a, ref Vector3 b) {
        var fa = Norm(a); var fb = Norm(b);
        float d = fa.x * fb.x + fa.y * fb.y + fa.z * fb.z;
        if (d >= 1 - 1e-6f) return new Quaternion(0, 0, 0, 1);
        if (d <= -1 + 1e-6f) {
            var ax = Maths.Cross(new Vector3(1, 0, 0), fa);
            if (ax.x * ax.x + ax.y * ax.y + ax.z * ax.z < 1e-6f) ax = Maths.Cross(new Vector3(0, 1, 0), fa);
            return M.Axis(ax, MathF.PI);
        }
        var c = Maths.Cross(fa, fb);
        return M.Normalize(new Quaternion(c.x, c.y, c.z, 1 + d));
    }

    static Vector3 Norm(Vector3 v) { float l = MathF.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z); return l < 1e-9f ? v : new Vector3(v.x / l, v.y / l, v.z / l); }

    [Icall("UnityEngine.Quaternion::INTERNAL_CALL_LookRotation")]
    static Quaternion Look(ref Vector3 f, ref Vector3 up) => Maths.LookRotation(f, up);

    [Icall("UnityEngine.Quaternion::INTERNAL_CALL_Slerp")]
    static Quaternion Slerp(ref Quaternion a, ref Quaternion b, float t) => SlerpU(a, b, Math.Clamp(t, 0, 1));

    [Icall("UnityEngine.Quaternion::INTERNAL_CALL_SlerpUnclamped")]
    static Quaternion SlerpUnclamped(ref Quaternion a, ref Quaternion b, float t) => SlerpU(a, b, t);

    static Quaternion SlerpU(Quaternion a, Quaternion b, float t) {
        float d = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        if (d < 0) { b = new Quaternion(-b.x, -b.y, -b.z, -b.w); d = -d; }
        if (d > 0.9995f) return LerpU(a, b, t);
        float th = MathF.Acos(d), s = MathF.Sin(th);
        float wa = MathF.Sin((1 - t) * th) / s, wb = MathF.Sin(t * th) / s;
        return new Quaternion(a.x * wa + b.x * wb, a.y * wa + b.y * wb, a.z * wa + b.z * wb, a.w * wa + b.w * wb);
    }

    static Quaternion LerpU(Quaternion a, Quaternion b, float t) {
        float d = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        if (d < 0) b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
        return M.Normalize(new Quaternion(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t));
    }

    [Icall("UnityEngine.Quaternion::INTERNAL_CALL_Lerp")]
    static Quaternion Lerp(ref Quaternion a, ref Quaternion b, float t) => LerpU(a, b, Math.Clamp(t, 0, 1));

    [Icall("UnityEngine.Quaternion::INTERNAL_CALL_LerpUnclamped")]
    static Quaternion LerpUnclamped(ref Quaternion a, ref Quaternion b, float t) => LerpU(a, b, t);

    [Icall("UnityEngine.Quaternion::INTERNAL_CALL_Inverse")]
    static Quaternion Inverse(ref Quaternion q) => M.Inverse(q);

    [Icall("UnityEngine.Quaternion::INTERNAL_CALL_Internal_ToEulerRad")]
    static Vector3 ToEulerRad(ref Quaternion q) { var e = M.ToEuler(q); return new Vector3(e.x * Deg, e.y * Deg, e.z * Deg); }

    [Icall("UnityEngine.Quaternion::INTERNAL_CALL_Internal_FromEulerRad")]
    static Quaternion FromEulerRad(ref Vector3 e) => M.Euler(new Vector3(e.x / Deg, e.y / Deg, e.z / Deg));

    [Icall("UnityEngine.Quaternion::INTERNAL_CALL_Internal_ToAxisAngleRad")]
    static void ToAxisAngle(ref Quaternion q, out Vector3 axis, out float angle) {
        var n = M.Normalize(q);
        angle = 2 * MathF.Acos(Math.Clamp(n.w, -1, 1));
        float s = MathF.Sqrt(1 - n.w * n.w);
        axis = s < 1e-6f ? new Vector3(1, 0, 0) : new Vector3(n.x / s, n.y / s, n.z / s);
    }

    [Icall("UnityEngine.Matrix4x4::INTERNAL_CALL_Inverse")] static Matrix4x4 MInverse(ref Matrix4x4 m) => M.Inverse(m);
    [Icall("UnityEngine.Matrix4x4::INTERNAL_CALL_TRS")] static Matrix4x4 TRS(ref Vector3 p, ref Quaternion q, ref Vector3 s) => M.TRS(p, q, s);

    [Icall("UnityEngine.Matrix4x4::INTERNAL_CALL_Transpose")]
    static Matrix4x4 Transpose(ref Matrix4x4 m) {
        var r = new Matrix4x4();
        for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) r[i, j] = m[j, i];
        return r;
    }

    [Icall("UnityEngine.Matrix4x4::Ortho")]
    public static Matrix4x4 Ortho(float l, float r, float b, float t, float n, float f) {
        var m = new Matrix4x4();
        m.m00 = 2 / (r - l); m.m11 = 2 / (t - b); m.m22 = -2 / (f - n);
        m.m03 = -(r + l) / (r - l); m.m13 = -(t + b) / (t - b); m.m23 = -(f + n) / (f - n); m.m33 = 1;
        return m;
    }

    [Icall("UnityEngine.Matrix4x4::Perspective")]
    public static Matrix4x4 Perspective(float fov, float aspect, float n, float f) {
        var m = new Matrix4x4();
        float c = 1 / MathF.Tan(fov * Deg / 2);
        m.m00 = c / aspect; m.m11 = c; m.m22 = -(f + n) / (f - n); m.m23 = -2 * f * n / (f - n); m.m32 = -1;
        return m;
    }

    [Icall("UnityEngine.Vector3::INTERNAL_CALL_Slerp")]
    static Vector3 V3Slerp(ref Vector3 a, ref Vector3 b, float t) {
        t = Math.Clamp(t, 0, 1);
        float la = MathF.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z), lb = MathF.Sqrt(b.x * b.x + b.y * b.y + b.z * b.z);
        if (la < 1e-6f || lb < 1e-6f) return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        var na = new Vector3(a.x / la, a.y / la, a.z / la); var nb = new Vector3(b.x / lb, b.y / lb, b.z / lb);
        float d = Math.Clamp(na.x * nb.x + na.y * nb.y + na.z * nb.z, -1, 1);
        float th = MathF.Acos(d) * t;
        var rel = new Vector3(nb.x - na.x * d, nb.y - na.y * d, nb.z - na.z * d);
        float rl = MathF.Sqrt(rel.x * rel.x + rel.y * rel.y + rel.z * rel.z);
        if (rl > 1e-6f) rel = new Vector3(rel.x / rl, rel.y / rl, rel.z / rl);
        float len = la + (lb - la) * t, c = MathF.Cos(th), s = MathF.Sin(th);
        return new Vector3((na.x * c + rel.x * s) * len, (na.y * c + rel.y * s) * len, (na.z * c + rel.z * s) * len);
    }

    [Icall("UnityEngine.Vector3::INTERNAL_CALL_RotateTowards")]
    static Vector3 RotateTowards(ref Vector3 cur, ref Vector3 target, float maxRad, float maxMag) {
        float lc = MathF.Sqrt(cur.x * cur.x + cur.y * cur.y + cur.z * cur.z), lt = MathF.Sqrt(target.x * target.x + target.y * target.y + target.z * target.z);
        if (lc < 1e-6f || lt < 1e-6f) return target;
        float d = Math.Clamp((cur.x * target.x + cur.y * target.y + cur.z * target.z) / (lc * lt), -1, 1);
        float angle = MathF.Acos(d);
        float t = angle < 1e-6f ? 1 : Math.Min(1, maxRad / angle);
        var dir = V3Slerp(ref cur, ref target, t);
        float dl = MathF.Sqrt(dir.x * dir.x + dir.y * dir.y + dir.z * dir.z);
        float len = lc + Math.Clamp(lt - lc, -maxMag, maxMag);
        return dl < 1e-6f ? dir : new Vector3(dir.x / dl * len, dir.y / dl * len, dir.z / dl * len);
    }
}
