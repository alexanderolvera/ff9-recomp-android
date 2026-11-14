// Debug: FF9_DUMP_FRAME=n prints the active scene hierarchy (components, enabled/started) at frame n.
using System;
using System.Linq;
using System.Text;

namespace FF9Engine.Core;

public static class DebugDump {
    static readonly int[] frames = (Environment.GetEnvironmentVariable("FF9_DUMP_FRAME") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
    static readonly string? goFilter = Environment.GetEnvironmentVariable("FF9_INSPECT_GO")?.Replace('_', ' ');

    public static void Tick() {
        if (!frames.Contains(Clock.Frame)) return;
        Console.WriteLine($"==== frame {Clock.Frame} t={Clock.Now:F2}");
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
