// .NET Framework (Unity 5.2's Mono) behaviour the game relies on that .NET 8 changed: the methods
// are patched at their compiled entry to jump to replacements here.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace FF9Engine.Native;

public static class Compat {
    [DllImport("__Internal")] static extern int ff9_jump_patch(IntPtr from, IntPtr to);

    public static void Detour(MethodBase target, MethodInfo replacement) {
        RuntimeHelpers.PrepareMethod(target.MethodHandle);
        RuntimeHelpers.PrepareMethod(replacement.MethodHandle);
        IntPtr from = target.MethodHandle.GetFunctionPointer(), to = replacement.MethodHandle.GetFunctionPointer();
        if (ff9_jump_patch(from, to) != 0) Log.Warn($"detour of {target.DeclaringType?.Name}.{target.Name} failed");
    }

    static MethodInfo M(string name) => typeof(Compat).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    public static void Install() {
        Detour(typeof(Assembly).GetMethod(nameof(Assembly.LoadFile), new[] { typeof(string) })!, M(nameof(LoadFile)));
    }

    /// .NET Framework accepted relative paths (Memoria loads "StreamingAssets/Scripts/Memoria.Scripts.dll").
    static Assembly LoadFile(string path) => AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
}
