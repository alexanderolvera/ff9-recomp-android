// FF9Engine: the engine underneath the game's UnityEngine.dll. Each UnityEngine internal call
// that is implemented here is a static method marked [Icall("Type::Method")] whose parameters
// match the internal call (instance calls take the object first). The host registers the
// compiled code of these methods as the internal calls.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FF9Engine;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class IcallAttribute(string name) : Attribute {
    public string Name { get; } = name;
}

public static unsafe class Boot {
    /// Called by the host before any game code runs: registers every [Icall] method.
    public static int Init(IntPtr addIcall) {
        // .NET on Android sends Console to logcat; the host wants it on stdout
        var stdout = new System.IO.FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(1, false), System.IO.FileAccess.Write);
        Console.SetOut(new System.IO.StreamWriter(stdout, new System.Text.UTF8Encoding(false), 4096) { AutoFlush = true });
        var add = (delegate* unmanaged<byte*, IntPtr, void>)addIcall;
        int n = 0;
        foreach (var t in typeof(Boot).Assembly.GetTypes())
            foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                foreach (var a in m.GetCustomAttributes<IcallAttribute>()) {
                    RuntimeHelpers.PrepareMethod(m.MethodHandle);
                    IntPtr fn = m.MethodHandle.GetFunctionPointer();
                    byte[] name = System.Text.Encoding.UTF8.GetBytes(a.Name + "\0");
                    fixed (byte* p = name) add(p, fn);
                    if (Environment.GetEnvironmentVariable("FF9_ICALL_TRACE") is { } tr && a.Name.Contains(tr)) Console.WriteLine($"icall {a.Name} -> {t.Name}.{m.Name} {fn:x}");
                    n++;
                }
        return n;
    }
}

public static class Entry {
    /// The game: scene 0 onwards, until Application.Quit (or FF9_FRAMES frames).
    public static int Play() {
        Console.WriteLine("args: " + string.Join(" ", Environment.GetCommandLineArgs()));
        typeof(Environment).GetField("s_commandLineArgs", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, EnvironmentIcalls.Args);
        try {
            return Core.Player.Main(Array.Empty<string>());
        } catch (Exception e) {
            Console.WriteLine($"FATAL {e}");
            return 1;
        }
    }
}

static class EnvironmentIcalls {
    /// The PC launcher starts the game with these; AppArgumentsCheck quits without them.
    [Icall("System.Environment::GetCommandLineArgs")]
    static string[] GetCommandLineArgs() => Args;

    /// The game's command line; FF9_ARGS adds arguments (e.g. "-memoria-battle 1 -memoria-party 0,1")
    public static string[] Args {
        get {
            var a = new List<string> { "FF9.exe", "-runbylauncher" };
            if (Environment.GetEnvironmentVariable("FF9_ARGS") is { Length: > 0 } extra)
                a.AddRange(extra.Split(new[] { ' ', '+' }, StringSplitOptions.RemoveEmptyEntries));   // '+' too: test env strings have no spaces
            return a.ToArray();
        }
    }
}
