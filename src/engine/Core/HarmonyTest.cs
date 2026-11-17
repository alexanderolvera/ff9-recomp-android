// FF9_TEST_HARMONY=path/to/0Harmony.dll (tests): once the title is up, loads that Harmony and
// patches a method of this class with a prefix, a postfix and a transpiler, logging each step.
using System;
using System.Collections.Generic;
using System.Reflection;

namespace FF9Engine.Core;

public static class HarmonyTest {
    static readonly string? path = Environment.GetEnvironmentVariable("FF9_TEST_HARMONY");
    static bool done;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static int Target(int x) => x * 2;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static int Target2(int x) => x * 2;
    public static void Postfix2(ref int __result) { __result += 1000; }
    public static IEnumerable<object> Transpiler(IEnumerable<object> instructions) {
        foreach (var ci in instructions) {
            var f = ci.GetType().GetField("opcode")!;
            if ((System.Reflection.Emit.OpCode)f.GetValue(ci)! == System.Reflection.Emit.OpCodes.Ldc_I4_2) f.SetValue(ci, System.Reflection.Emit.OpCodes.Ldc_I4_3);
            yield return ci;
        }
    }
    public static void Prefix(ref int x) { x += 1; }
    public static void Postfix(ref int __result) { __result += 100; }

    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void ff9_crash_trace();
    static bool crashTraced;

    public static void Tick() {
        // FF9_TRACE_CRASH: the host's fault trace, put in again once the title is up (Mono sets its
        // own signal handlers after the host's start-up)
        if (!crashTraced && Player.LastSceneLoaded == "Title" && Environment.GetEnvironmentVariable("FF9_TRACE_CRASH") != null) { crashTraced = true; ff9_crash_trace(); }
        if (path == null || done || Player.LastSceneLoaded != "Title") return;
        done = true;
        try {
            var asm = Assembly.LoadFrom(path);
            Log.Info("harmony test: loaded " + asm.FullName);
            var hType = asm.GetType("HarmonyLib.Harmony", true)!;
            var hm = asm.GetType("HarmonyLib.HarmonyMethod", true)!;
            Step("AccessTools", () => asm.GetType("HarmonyLib.AccessTools", true)!.GetMethod("Method", new[] { typeof(Type), typeof(string), typeof(Type[]), typeof(Type[]) })!.Invoke(null, new object?[] { typeof(HarmonyTest), "Target", null, null }));
            var h = Step("new Harmony", () => Activator.CreateInstance(hType, "ff9.test"));
            if (h == null) return;
            var target = typeof(HarmonyTest).GetMethod(nameof(Target))!;
            object Mk(string n) => Activator.CreateInstance(hm, typeof(HarmonyTest).GetMethod(n))!;
            var patch = Array.Find(hType.GetMethods(), m => m.Name == "Patch" && m.GetParameters().Length == 5)!;
            Step("Patch", () => patch.Invoke(h, new object?[] { target, Mk(nameof(Prefix)), Mk(nameof(Postfix)), null, null }));
            Log.Info($"harmony test: Target(1) = {Target(1)} (patched: 104)");
            // a transpiler (2 -> 3) on another method, a second postfix on Target, then UnpatchAll
            var t2 = typeof(HarmonyTest).GetMethod(nameof(Target2))!;
            Step("Patch transpiler", () => patch.Invoke(h, new object?[] { t2, null, null, Mk(nameof(Transpiler)), null }));
            Log.Info($"harmony test: Target2(5) = {Target2(5)} (transpiled: 15)");
            Step("Patch again", () => patch.Invoke(h, new object?[] { target, null, Mk(nameof(Postfix2)), null, null }));
            Log.Info($"harmony test: Target(1) = {Target(1)} (patched twice: 1104)");
            var unpatch = Array.Find(hType.GetMethods(), m => m.Name == "UnpatchAll" && m.GetParameters().Length == 1)!;
            Step("UnpatchAll", () => unpatch.Invoke(h, new object?[] { "ff9.test" }));
            Log.Info($"harmony test: Target(1) = {Target(1)}, Target2(5) = {Target2(5)} (unpatched: 2, 10)");
        } catch (Exception e) { Log.Warn("harmony test: " + e); }
    }

    static object? Step(string what, Func<object?> f) {
        try { var r = f(); Log.Info($"harmony test: {what} ok: {r}"); return r; }
        catch (Exception e) {
            for (var x = e; x != null; x = x.InnerException) Log.Warn($"harmony test: {what}: {x.GetType().Name}: {x.Message}\n{x.StackTrace}");
            return null;
        }
    }
}
