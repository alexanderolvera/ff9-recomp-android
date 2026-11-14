// The player loop: scene loading, MonoBehaviour lifecycle and frames, in Unity 5.2's order.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FF9Engine.Scripting;
using FF9Engine.Icalls;
using UnityEngine;

namespace FF9Engine.Core;

public static class Clock {
    static readonly Stopwatch sw = Stopwatch.StartNew();
    public static float Now;                // scaled game time (Time.time)
    public static float Unscaled;           // Time.unscaledTime
    public static float Delta = 1 / 60f, UnscaledDelta = 1 / 60f, SmoothDelta = 1 / 60f;
    public static float TimeScale = 1, FixedDelta = 0.02f, MaxDelta = 1 / 3f, FixedTime;
    public static float LevelLoadTime;
    public static int Frame;
    public static float Realtime => (float)sw.Elapsed.TotalSeconds;
    public static int TargetFrameRate = -1;

    static double last = -1;

    public static void Tick() {
        double t = sw.Elapsed.TotalSeconds;
        float d = last < 0 ? 1 / 60f : (float)(t - last);
        last = t;
        UnscaledDelta = d;
        Unscaled += d;
        Delta = Math.Min(d, MaxDelta) * TimeScale;
        SmoothDelta = SmoothDelta * 0.8f + Delta * 0.2f;
        Now += Delta;
        Frame++;
    }
}

public sealed class NScene {
    public int Index;
    public string Name = "";
    public string Path = "";
}

public static class Player {
    public static readonly List<NMonoBehaviour> Behaviours = new();
    static readonly List<NMonoBehaviour> pendingStart = new();
    public static NScene? ActiveScene;
    public static string[] SceneNames = Array.Empty<string>();
    public static bool Quit;
    public static Action? Render;             // set by the renderer
    public static Action? PollInput;
    public static Action<float>? AnimationTick;   // legacy Animation, between Update and LateUpdate

    sealed class LevelRequest { public int Index; public bool Additive; public AsyncOps.Op? Op; }
    static readonly Queue<LevelRequest> levelRequests = new();

    public static int Main(string[] args) {
        Native.DllImports.Install();
        Native.Compat.Install();
        PollInput = () => { Touches.WasDown = Touches.Down; Icalls.Pad.Poll(); };
        Gfx.Textures.Register(); Gfx.Shaders.Register(); Gfx.Materials.Register(); Gfx.Meshes.Register();
        Gfx.Sprites.Register(); Gfx.RendererComponents.Register(); Gfx.Cameras.Register(); Assets.TextAssets.Register(); Physics.Colliders.Register(); Gfx.Canvases.Register(); Text.Fonts.Register(); Anim.Clips.Register(); Anim.Animations.Register();
        if (Environment.GetEnvironmentVariable("FF9_NOGL") == null) {
            try { Gfx.Device.Init(); } catch (Exception e) { Log.Warn("GL init failed: " + e.Message); }
            Icalls.Display.Width = Gfx.Device.Width; Icalls.Display.Height = Gfx.Device.Height;
        }
        string? shot = Environment.GetEnvironmentVariable("FF9_SHOT");     // "frame[,frame...]:dir"
        var shotFrames = new HashSet<int>();
        string shotDir = "shots";
        if (shot != null) {
            var sp = shot.Split(':');
            foreach (var f in sp[0].Split(',')) if (int.TryParse(f, out int fi)) shotFrames.Add(fi);
            if (sp.Length > 1) shotDir = sp[1];
        }
        int noRenderFrom = int.TryParse(Environment.GetEnvironmentVariable("FF9_NO_RENDER_FROM"), out var nrf) ? nrf : int.MaxValue;
        Render = () => {
            if (Clock.Frame >= noRenderFrom) { if (Environment.GetEnvironmentVariable("FF9_NO_SWAP") == null) Gfx.Device.Present(); return; }
            Gfx.Pipeline.RenderFrame();
            if (shotFrames.Contains(Clock.Frame)) Gfx.Device.Screenshot(System.IO.Path.Combine(shotDir, $"frame{Clock.Frame:D5}.png"));
            Gfx.Device.Present();
        };
        Assets.AssetDB.Init();
        SceneNames = Assets.AssetDB.SceneNames;
        LoadLevel(0, false);
        int maxFrames = int.TryParse(Environment.GetEnvironmentVariable("FF9_FRAMES"), out var mf) ? mf : int.MaxValue;
        // FF9_FPS=60: pace frames like a display's vsync (the headless pbuffer doesn't wait)
        int fps = int.TryParse(Environment.GetEnvironmentVariable("FF9_FPS"), out var fv) ? fv : 0;
        var pace = Stopwatch.StartNew();
        long next = 0;
        while (!Quit && Clock.Frame < maxFrames) {
            Frame();
            if (fps > 0) {
                next += Stopwatch.Frequency / fps;
                long wait = next - pace.ElapsedTicks;
                if (wait > 0) System.Threading.Thread.Sleep((int)(wait * 1000 / Stopwatch.Frequency));
                else if (wait < -Stopwatch.Frequency / 4) next = pace.ElapsedTicks;
            }
        }
        foreach (var b in Behaviours.ToArray()) ScriptMethods.Call(b, b.Methods.OnApplicationQuit);
        Log.Info($"player stopped after {Clock.Frame} frames");
        return 0;
    }

    public static void RequestLevel(int index, bool additive, AsyncOps.Op? op) =>
        levelRequests.Enqueue(new LevelRequest { Index = index, Additive = additive, Op = op });

    public static int SceneIndex(string name) {
        for (int i = 0; i < SceneNames.Length; i++) {
            string n = SceneNames[i];
            string shortName = System.IO.Path.GetFileNameWithoutExtension(n);
            if (string.Equals(shortName, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    static void Frame() {
        Clock.Tick();
        PollInput?.Invoke();
        // FixedUpdate at a fixed rate
        while (Clock.FixedTime + Clock.FixedDelta <= Clock.Now) {
            Clock.FixedTime += Clock.FixedDelta;
            foreach (var b in Snapshot()) if (Runs(b)) ScriptMethods.Call(b, b.Methods.FixedUpdate);
            Coroutines.Run(Coroutines.Phase.Fixed);
        }
        RunStarts();
        foreach (var b in Snapshot()) if (Runs(b) && b.Started) ScriptMethods.Call(b, b.Methods.Update);
        Coroutines.RunInvokes();
        Coroutines.Run(Coroutines.Phase.Update);
        AsyncOps.Tick();
        AnimationTick?.Invoke(Clock.Delta);
        foreach (var b in Snapshot()) if (Runs(b) && b.Started) ScriptMethods.Call(b, b.Methods.LateUpdate);
        Render?.Invoke();
        Coroutines.Run(Coroutines.Phase.EndOfFrame);
        DebugDump.Tick();
        if (memEvery > 0 && Clock.Frame % memEvery == 0) MemStat();
        Destroyer.Flush();
        while (levelRequests.Count > 0) {
            var r = levelRequests.Dequeue();
            LoadLevel(r.Index, r.Additive);
            r.Op?.Complete();
        }
    }

    static NMonoBehaviour[] Snapshot() => Behaviours.ToArray();

    static readonly int memEvery = int.TryParse(Environment.GetEnvironmentVariable("FF9_MEMSTAT"), out var me) ? me : 0;

    /// FF9_MEMSTAT=n: every n frames, managed heap, process memory and object counts.
    static void MemStat() {
        string rss = "?", vsz = "?";
        int maps = 0;
        try {
            foreach (var l in System.IO.File.ReadAllLines("/proc/self/status")) {
                if (l.StartsWith("VmRSS:")) rss = l.Substring(6).Trim();
                else if (l.StartsWith("VmSize:")) vsz = l.Substring(7).Trim();
            }
            var ml = System.IO.File.ReadAllLines("/proc/self/maps");
            maps = ml.Length;
            if (Environment.GetEnvironmentVariable("FF9_MEMSTAT_MAPS") != null) {
                var groups = new Dictionary<string, int>();
                foreach (var l in ml) {
                    var parts = l.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
                    string key = (parts.Length > 5 ? parts[5] : "[anon]") + " " + parts[1];
                    groups[key] = groups.GetValueOrDefault(key) + 1;
                }
                foreach (var g in groups.OrderByDescending(g => g.Value).Take(6)) Log.Info($"   maps {g.Value} {g.Key}");
                var sizes = new Dictionary<long, int>();
                foreach (var l in ml) {
                    if (!l.Contains("kgsl")) continue;
                    var r = l.Split(' ')[0].Split('-');
                    long sz = Convert.ToInt64(r[1], 16) - Convert.ToInt64(r[0], 16);
                    sizes[sz] = sizes.GetValueOrDefault(sz) + 1;
                }
                Log.Info("   kgsl sizes: " + string.Join(", ", sizes.OrderByDescending(s => s.Value).Take(8).Select(s => $"{s.Key >> 10}K x{s.Value}")));
            }
        } catch { }
        Log.Info($"gpu since last: vbo {GpuStats.VboUploads}, ibo {GpuStats.IboUploads}, tex {GpuStats.TexUploads} ({GpuStats.LastTex}), rt {GpuStats.RtCreates}, programs {Gfx.Pipeline.ProgramCount}");
        GpuStats.VboUploads = GpuStats.IboUploads = GpuStats.TexUploads = GpuStats.RtCreates = 0;
        if (System.Runtime.InteropServices.NativeLibrary.TryGetExport(Native.DllImports.Host, "ff9_gl_stats", out var glStats))
            unsafe { ((delegate* unmanaged<void>)glStats)(); }
        if (Environment.GetEnvironmentVariable("FF9_MEMSTAT_TYPES") != null)
            Log.Info("   objects by type: " + string.Join(", ", Registry.All.GroupBy(o => o is Assets.NAsset a ? "NAsset:" + a.Data?.GetType().Name : o.GetType().Name + (o is NGameObject g ? ":" + g.Name : ""))
                .OrderByDescending(g => g.Count()).Take(10).Select(g => $"{g.Key} {g.Count()}")));
        Log.Info($"mem f{Clock.Frame}: gc {GC.GetTotalMemory(false) >> 20} MB (gen0 {GC.CollectionCount(0)}, gen2 {GC.CollectionCount(2)}), rss {rss}, vsz {vsz}, maps {maps}, behaviours {Behaviours.Count}, objects {Registry.Count}");
    }

    static bool Runs(NMonoBehaviour b) => b.Enabled && b.Managed != null && b.GameObject.ActiveInHierarchy && Registry.Get(b.Managed) == b;

    static void RunStarts() {
        while (pendingStart.Count > 0) {
            var list = pendingStart.ToArray();
            pendingStart.Clear();
            foreach (var b in list.OrderBy(b => b.ExecutionOrder)) {
                if (b.Started || !Runs(b)) { if (!b.Started) pendingStart.Add(b); continue; }
                b.Started = true;
                ScriptMethods.Call(b, b.Methods.Start);
            }
            if (pendingStart.All(b => !Runs(b))) break;
        }
    }

    /// Registers a newly created MonoBehaviour (scene load, AddComponent, Instantiate).
    public static void AddBehaviour(NMonoBehaviour b) {
        b.Methods = ScriptMethods.For(b.Managed!.GetType());
        Behaviours.Add(b);
    }

    /// Awake + OnEnable for a behaviour whose GameObject is active (Unity does this at creation
    /// or when the GameObject is first activated).
    public static void Awaken(NMonoBehaviour b) {
        if (!b.GameObject.ActiveInHierarchy) return;
        if (!b.Awoken) {
            b.Awoken = true;
            ScriptMethods.Call(b, b.Methods.Awake);
        }
        if (b.Enabled && !b.EnabledCalled && Registry.Get(b.Managed) == b) {
            b.EnabledCalled = true;
            ScriptMethods.Call(b, b.Methods.OnEnable);
            if (!b.Started && !pendingStart.Contains(b)) pendingStart.Add(b);
        }
    }

    public static void Disable(NMonoBehaviour b) {
        if (!b.EnabledCalled) return;
        b.EnabledCalled = false;
        ScriptMethods.Call(b, b.Methods.OnDisable);
    }

    /// Activation changes on a hierarchy: OnEnable/Awake or OnDisable for behaviours below.
    public static void HierarchyActivationChanged(NGameObject go) {
        var list = new List<NMonoBehaviour>();
        Collect(go.Transform, list);
        foreach (var b in list) {
            if (b.GameObject.ActiveInHierarchy) Awaken(b);
            else Disable(b);
        }
    }

    static void Collect(NTransform t, List<NMonoBehaviour> list) {
        foreach (var c in t.GameObject.Components) if (c is NMonoBehaviour b) list.Add(b);
        foreach (var ch in t.Children.ToArray()) Collect(ch, list);
    }

    static void LoadLevel(int index, bool additive) {
        var sw = Stopwatch.StartNew();
        if (!additive) {
            foreach (var o in Registry.All.ToArray())
                if (o is NGameObject go && !go.IsAsset && !go.DontDestroy && go.Transform.Parent == null && !IsUnderDontDestroy(go))
                    Destroyer.DestroyNow(go);
        }
        var scene = new NScene { Index = index, Path = SceneNames[index], Name = System.IO.Path.GetFileNameWithoutExtension(SceneNames[index]) };
        if (!additive) ActiveScene = scene;
        var created = Assets.SceneLoader.Load(index, scene);
        Clock.LevelLoadTime = Clock.Now;
        Log.Info($"scene {index} '{scene.Name}': {created.Count} behaviours, {sw.ElapsedMilliseconds} ms");
        foreach (var b in created.OrderBy(b => b.ExecutionOrder)) Awaken(b);
        foreach (var b in Snapshot()) if (Registry.Get(b.Managed) == b) ScriptMethods.Call(b, b.Methods.OnLevelWasLoaded, index);
    }

    static bool IsUnderDontDestroy(NGameObject go) => go.Transform.Root.GameObject.DontDestroy;
}

/// Object.Destroy: deferred to the end of the frame; DestroyImmediate: now.
public static class Destroyer {
    static readonly List<(NObject obj, float at)> pending = new();

    public static void Destroy(NObject o, float delay) => pending.Add((o, Clock.Now + delay));

    public static void Flush() {
        if (pending.Count == 0) return;
        var now = pending.Where(p => p.at <= Clock.Now).ToList();
        pending.RemoveAll(p => p.at <= Clock.Now);
        foreach (var (o, _) in now) DestroyNow(o);
    }

    public static void DestroyNow(NObject o) {
        if (Registry.ById(o.Id) != o) return;
        switch (o) {
            case NGameObject go:
                foreach (var ch in go.Transform.Children.ToArray()) DestroyNow(ch.GameObject);
                foreach (var c in go.Components.ToArray().Reverse()) {
                    if (c is NMonoBehaviour b) {
                        if (b.EnabledCalled) Player.Disable(b);
                        if (b.Awoken) ScriptMethods.Call(b, b.Methods.OnDestroy);
                        Coroutines.Stop(b);
                        Coroutines.CancelInvoke(b, null);
                        Player.Behaviours.Remove(b);
                    }
                    Registry.Remove(c);
                }
                go.Transform.Parent?.Children.Remove(go.Transform);
                Registry.Remove(go);
                break;
            case NTransform:
                break;          // a Transform is destroyed with its GameObject only
            case NComponent c:
                if (c is NMonoBehaviour mb) {
                    if (mb.EnabledCalled) Player.Disable(mb);
                    if (mb.Awoken) ScriptMethods.Call(mb, mb.Methods.OnDestroy);
                    Coroutines.Stop(mb);
                    Coroutines.CancelInvoke(mb, null);
                    Player.Behaviours.Remove(mb);
                }
                c.GameObject.Components.Remove(c);
                Registry.Remove(c);
                break;
            default:
                Registry.Remove(o);
                break;
        }
    }
}

/// Counts of GPU uploads between memory reports.
public static class GpuStats {
    public static int VboUploads, IboUploads, TexUploads, RtCreates;
    public static string LastTex = "";
}
