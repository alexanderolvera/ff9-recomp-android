using System;
using System.Linq;
using System.Collections.Generic;

namespace FF9Engine;

public static class Log {
    static readonly HashSet<string> once = new();

    public static void Info(string s) => Console.WriteLine(s);

    public static void Warn(string s) => Console.WriteLine("WARN " + s);

    /// Logs a message the first time a given key is seen (for partially implemented engine calls).
    public static void Once(string key, string? s = null) {
        if (once.Add(key)) Console.WriteLine("TODO " + (s ?? key));
    }

    static readonly System.Collections.Generic.Dictionary<string, int> repeats = new();

    public static void Exception(Exception e, string where) {
        // the same exception from the same place: the first 3, then one line per 1000
        var top = new System.Diagnostics.StackTrace(e, false).GetFrames().FirstOrDefault();
        string at = top == null ? "" : $"{top.GetMethod()?.DeclaringType?.Name}.{top.GetMethod()?.Name} IL_{top.GetILOffset():x4}";
        string key = where + "|" + e.GetType().Name + "|" + at;
        int n = repeats.GetValueOrDefault(key) + 1;
        repeats[key] = n;
        if (n > 3) {
            if (n % 1000 == 0) Console.WriteLine($"EXCEPTION in {where}: {e.GetType().Name} at {at} (x{n})");
            return;
        }
        Console.WriteLine($"EXCEPTION in {where}: {e.GetType().Name}: {e.Message} [at {at}]\n{e.StackTrace}");
        for (var inner = e.InnerException; inner != null; inner = inner.InnerException)
            Console.WriteLine($"  inner {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}");
        if (Environment.GetEnvironmentVariable("FF9_EXC_OFFSETS") != null)
            foreach (var f in new System.Diagnostics.StackTrace(e, false).GetFrames())
                Console.WriteLine($"   frame {f.GetMethod()?.DeclaringType?.Name}.{f.GetMethod()?.Name} IL_{f.GetILOffset():x4} native+{f.GetNativeOffset():x}");
        if (Environment.GetEnvironmentVariable("FF9_STOP_ON_EXCEPTION") != null) Environment.Exit(3);
    }
}

static class DebugIcalls {
    [Icall("UnityEngine.Debug::Internal_Log")]
    static void Internal_Log(int level, string msg, UnityEngine.Object ctx) =>
        Console.WriteLine(level == 0 ? $"[log] {msg}" : $"[log {(level == 1 ? "warn" : "error")}] {msg}");

    [Icall("UnityEngine.Debug::Internal_LogException")]
    static void Internal_LogException(Exception e, UnityEngine.Object ctx) => Log.Exception(e, "Debug.LogException");

    [Icall("UnityEngine.Debug::get_isDebugBuild")]
    static bool IsDebugBuild() => false;
}
