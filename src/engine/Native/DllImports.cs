// Native libraries the game P/Invokes (Windows DLLs and plugins) resolve to the host
// executable, which exports stand-ins (src/host/*.c) or the Android builds of the plugins.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FF9Engine.Native;

public static class DllImports {
    static readonly HashSet<string> hostLibs = new(StringComparer.OrdinalIgnoreCase) {
        "user32", "kernel32", "steamwrapper", "XInputInterface", "JoyShockLibrary", "__Internal", "SdLib",
        "theorawrapper", "FF9SpecialEffectPlugin", "soloud", "SaXAudio", "steam_api", "steam_api64",
        "libSystem.Security.Cryptography.Native.Android",
    };

    public static void Install() {
        foreach (var name in new[] { "Assembly-CSharp", "Assembly-CSharp-firstpass", "Memoria.Prime", "XInputDotNetPure", "Memoria.Scripts" }) {
            try { NativeLibrary.SetDllImportResolver(Assembly.Load(name), Resolve); }
            catch (Exception e) { Log.Warn($"DllImport resolver for {name}: {e.Message}"); }
        }
        NativeLibrary.SetDllImportResolver(typeof(DllImports).Assembly, Resolve);   // FF9Engine's own "__Internal" calls
        // .NET's Android crypto backend needs a JVM; the host implements the parts the game uses
        NativeLibrary.SetDllImportResolver(typeof(System.Security.Cryptography.Aes).Assembly, Resolve);
        AppDomain.CurrentDomain.AssemblyLoad += (_, a) => {
            if (a.LoadedAssembly.GetName().Name == "Memoria.Scripts")
                try { NativeLibrary.SetDllImportResolver(a.LoadedAssembly, Resolve); } catch { }
        };
    }

    /// The host: libff9.so inside the APK, the executable in the headless host.
    public static readonly IntPtr Host = NativeLibrary.TryLoad("libff9.so", out var h) ? h : NativeLibrary.GetMainProgramHandle();

    static IntPtr Resolve(string lib, Assembly asm, DllImportSearchPath? path) {
        string n = lib.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? lib[..^4] : lib;
        if (hostLibs.Contains(n)) return Host;
        if (n == "libGLESv3.so" && Environment.GetEnvironmentVariable("FF9_GL_TRACE") != null) return Host;   // debug GL call counts
        if (n == "libGLESv3.so" || n == "libEGL.so") return IntPtr.Zero;
        Log.Once("dll:" + lib, $"DllImport {lib} from {asm.GetName().Name}: no stand-in");
        return IntPtr.Zero;
    }
}
