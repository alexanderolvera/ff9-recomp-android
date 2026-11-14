// Debug: FF9_DUMP_FRAME=n prints the active scene hierarchy (components, enabled/started) at frame n.
using System;
using System.Linq;
using System.Text;

namespace FF9Engine.Core;

public static class DebugDump {
    static readonly int[] frames = (Environment.GetEnvironmentVariable("FF9_DUMP_FRAME") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
    static readonly string? goFilter = Environment.GetEnvironmentVariable("FF9_INSPECT_GO")?.Replace('_', ' ');

    static readonly bool probe = Environment.GetEnvironmentVariable("FF9_PROBE") != null;

    static void Try(string what, Func<object?> f) {
        try { Console.WriteLine($"probe {what} = {f() ?? "null"}"); }
        catch (Exception e) { Console.WriteLine($"probe {what} THROWS {e.GetType().Name}: {e.Message}"); }
    }

    /// FF9_PROBE: evaluates the calls FieldMapActorController.MovePC makes, one by one.
    static void Probe() {
        Try("Field", () => FF9StateSystem.Field);
        Try("Field.isDebug", () => FF9StateSystem.Field.isDebug);
        Try("IsMovementControl", () => EventInput.IsMovementControl);
        Try("HonoInputManager", () => PersistenSingleton<HonoInputManager>.Instance);
        Try("GetAxis", () => PersistenSingleton<HonoInputManager>.Instance.GetAxis());
        Try("UIManager.Input", () => UIManager.Input);
        Try("XInputManager", () => PersistenSingleton<UnityXInput.XInputManager>.Instance);
        Try("CurrentState", () => { var x = PersistenSingleton<UnityXInput.XInputManager>.Instance; var st = x.GetType().GetProperty("CurrentState")!.GetValue(x)!; var dp = st.GetType().GetProperty("DPad")!.GetValue(st)!; return dp.GetType().GetProperty("Down")!.GetValue(dp); });
        Try("GetKey", () => { var inp = UIManager.Input; var m = inp.GetType().GetMethod("GetKey")!; return m.Invoke(inp, new[] { Enum.ToObject(m.GetParameters()[0].ParameterType, 0) }); });
        Try("EventEngine", () => PersistenSingleton<EventEngine>.Instance);
        Try("GetUserControl", () => PersistenSingleton<EventEngine>.Instance.GetUserControl());
    }

    public static void Tick() {
        if (probe && Clock.Frame % 500 == 250) Probe();
        if (!frames.Contains(Clock.Frame)) return;
        Console.WriteLine($"==== frame {Clock.Frame} t={Clock.Now:F2}");
        if (Environment.GetEnvironmentVariable("FF9_DUMP_RT") is { } rtName)
            foreach (var o in Registry.All.ToList())
                if (o is Gfx.NRenderTexture rt && rt.Created && rt.Name.Contains(rtName)) unsafe {
                    var px = new byte[rt.Width * rt.Height * 4];
                    Gfx.GL.glBindFramebuffer(Gfx.GL.FRAMEBUFFER, rt.Fbo);
                    Gfx.GL.glPixelStorei(Gfx.GL.PACK_ALIGNMENT, 1);
                    fixed (byte* p = px) Gfx.GL.glReadPixels(0, 0, rt.Width, rt.Height, Gfx.GL.RGBA, Gfx.GL.UNSIGNED_BYTE, p);
                    Gfx.GL.glBindFramebuffer(Gfx.GL.FRAMEBUFFER, Gfx.RenderTargets.CurrentFbo);
                    Gfx.Png.Write($"/data/local/tmp/ff9/shots/rt_{rt.Id}.png", px, rt.Width, rt.Height, flipY: true);
                    Console.WriteLine($"dumped render texture #{rt.Id} '{rt.Name}'");
                }
        if (goFilter != null) {
            foreach (var mb in Player.Behaviours.ToList())
                if (mb.Managed != null && mb.GameObject.Name == goFilter && mb.GameObject.ActiveInHierarchy) Inspect(mb);
            return;
        }
        var sb = new StringBuilder();
        foreach (var o in Registry.All.OfType<NGameObject>().Where(g => !g.IsAsset && g.Transform.Parent == null).ToList())
            Dump(o.Transform, sb, 0);
        Console.WriteLine(sb.ToString());
        if (Environment.GetEnvironmentVariable("FF9_INSPECT") is { } types)
            foreach (var mb in Player.Behaviours.ToList())
                if (mb.Managed != null && types.Split(',').Contains(mb.Managed.GetType().Name)) Inspect(mb);
    }

    static void Inspect(NMonoBehaviour mb) {
        var o = mb.Managed!;
        Console.WriteLine($"inspect {o.GetType().Name} on '{mb.GameObject.Name}' enabled={mb.Enabled} started={mb.Started}");
        for (var t = o.GetType(); t != null && t != typeof(UnityEngine.MonoBehaviour); t = t.BaseType)
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)) {
                object? v;
                try { v = f.GetValue(o); } catch { v = "?"; }
                string s = v switch {
                    null => "null",
                    UnityEngine.AnimationCurve c => "curve[" + string.Join(" ", c.keys.Select(k => $"({k.time},{k.value})")) + $"] e(0.5)={c.Evaluate(0.5f)}",
                    UnityEngine.Object u => Registry.Get(u) is { } n ? $"{u.GetType().Name} '{n.Name}'" : "destroyed",
                    _ => v.ToString() ?? "",
                };
                Console.WriteLine($"   {t.Name}.{f.Name} = {s}");
            }
    }

    static void Dump(NTransform t, StringBuilder sb, int depth) {
        var g = t.GameObject;
        if (!g.ActiveInHierarchy && depth > 0) return;
        sb.Append(' ', depth * 2).Append(g.Name).Append(g.ActiveSelf ? "" : " (inactive)").Append(" L").Append(g.Layer).Append(" pos=").Append(t.LocalPosition).Append(" :");
        foreach (var c in g.Components.Skip(1)) {
            string n = c.Managed?.GetType().Name ?? "?";
            sb.Append(' ').Append(n);
            if (!c.Enabled) sb.Append("(off)");
            else if (c is NMonoBehaviour mb && !mb.Started) sb.Append("(nostart)");
        }
        sb.Append('\n');
        foreach (var ch in t.Children) Dump(ch, sb, depth + 1);
    }
}
