// Mods that patch the game while it runs bring Harmony 2.2.x's net35 build (0Harmony.dll), made for
// Unity's old Mono (GitHub issue 14, The Lost Chapters). On .NET 8 some of it fails:
//  - .NET 3.5's MethodBody properties were not virtual, so its IL calls them with `call`: on .NET 8
//    that reaches the base class's stubs (null IL, ArgumentNullException). Assembly.LoadFrom of a
//    0Harmony.dll loads a copy whose calls to MethodBody members are `callvirt` (same length).
//  - AppDomain.DefineDynamicAssembly is gone: the reference is pointed at the static
//    AssemblyBuilder.DefineDynamicAssembly (same arguments), its calls made static, and the
//    AppDomain.CurrentDomain pushed before each call taken out.
//  - AccessTools' static constructor wants Exception.PrepForRemoting (gone): MethodInvoker.GetHandler
//    takes a null method and calls through reflection otherwise.
// Methods are replaced at their compiled entry (Compat.Detour, the host's ARM64 jump).
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace FF9Engine.Native;

public static class HarmonyCompat {
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type? fastInvoke;
    static readonly Dictionary<string, Assembly> loaded = new(StringComparer.OrdinalIgnoreCase);

    public static void Watch() {
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) => {
            if (e.LoadedAssembly.GetName().Name == "0Harmony") Install(e.LoadedAssembly);
        };
        Compat.Detour(typeof(Assembly).GetMethod(nameof(Assembly.LoadFrom), new[] { typeof(string) })!, typeof(HarmonyCompat).GetMethod(nameof(LoadFrom), All)!);
    }

    /// Assembly.LoadFrom: a 0Harmony.dll is loaded as rewritten for .NET 8 (once: the mod loads it
    /// from several places), anything else as .NET 8 does.
    static Assembly LoadFrom(string path) {
        string full = Path.GetFullPath(path);
        if (!Path.GetFileName(full).Equals("0Harmony.dll", StringComparison.OrdinalIgnoreCase))
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(full);
        lock (loaded) {
            if (loaded.TryGetValue(full, out var a)) return a;
            foreach (var other in AppDomain.CurrentDomain.GetAssemblies())
                if (other.GetName().Name == "0Harmony") { loaded[full] = other; return other; }
            byte[] bytes = File.ReadAllBytes(full);
            int n = Rewrite(bytes);
            Log.Info($"harmony: {full}: {n} calls rewritten for .NET 8");
            a = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes));
            loaded[full] = a;
            return a;
        }
    }

    /// Turns `call` into `callvirt` where it calls a member of System.Reflection.MethodBody, and
    /// AppDomain.DefineDynamicAssembly into AssemblyBuilder.DefineDynamicAssembly.
    static int Rewrite(byte[] image) {
        using var pe = new PEReader(new MemoryStream(image, false));
        var md = pe.GetMetadataReader();
        var targets = new HashSet<int>();
        int defineDynamic = 0, currentDomain = 0;
        TypeReferenceHandle assemblyBuilder = default;
        foreach (var th in md.TypeReferences) {
            var tr = md.GetTypeReference(th);
            if (md.GetString(tr.Namespace) == "System.Reflection.Emit" && md.GetString(tr.Name) == "AssemblyBuilder") assemblyBuilder = th;
        }
        foreach (var h in md.MemberReferences) {
            var mr = md.GetMemberReference(h);
            if (mr.Parent.Kind != HandleKind.TypeReference) continue;
            var tr = md.GetTypeReference((TypeReferenceHandle)mr.Parent);
            string ns = md.GetString(tr.Namespace), tn = md.GetString(tr.Name), name = md.GetString(mr.Name);
            if (ns == "System.Reflection" && tn == "MethodBody") targets.Add(MetadataTokens.GetToken(h));
            if (ns == "System" && tn == "AppDomain" && name == "get_CurrentDomain") currentDomain = MetadataTokens.GetToken(h);
            if (ns == "System" && tn == "AppDomain" && name == "DefineDynamicAssembly" && !assemblyBuilder.IsNil) {
                var sig = md.GetBlobReader(mr.Signature);
                if (sig.ReadByte() != 0x20 || sig.ReadCompressedInteger() != 2) continue;       // instance, (AssemblyName, AssemblyBuilderAccess)
                defineDynamic = MetadataTokens.GetToken(h);
                int mdStart = pe.PEHeaders.MetadataStartOffset;
                // the member's parent: AssemblyBuilder (MemberRefParent coded index, TypeRef tag 1)
                int row = MetadataTokens.GetRowNumber(h);
                int at = mdStart + md.GetTableMetadataOffset(TableIndex.MemberRef) + (row - 1) * md.GetTableRowSize(TableIndex.MemberRef);
                int parent = (MetadataTokens.GetRowNumber(assemblyBuilder) << 3) | 1;
                bool wide = new[] { TableIndex.TypeDef, TableIndex.TypeRef, TableIndex.ModuleRef, TableIndex.MethodDef, TableIndex.TypeSpec }
                    .Any(t => md.GetTableRowCount(t) >= 1 << 13);
                if (wide) BitConverter.GetBytes(parent).CopyTo(image, at); else BitConverter.GetBytes((ushort)parent).CopyTo(image, at);
                // its signature: static (the blob's first byte after its length)
                int blob = mdStart + md.GetHeapMetadataOffset(HeapIndex.Blob) + MetadataTokens.GetHeapOffset(mr.Signature);
                int len = image[blob] < 0x80 ? 1 : image[blob] < 0xC0 ? 2 : 4;
                image[blob + len] = 0x00;
            }
        }
        if (targets.Count == 0 && defineDynamic == 0) return 0;
        var ops = new Dictionary<short, OpCode>();
        foreach (var f in typeof(OpCodes).GetFields()) { var op = (OpCode)f.GetValue(null)!; ops[op.Value] = op; }
        var sections = pe.PEHeaders.SectionHeaders;
        int count = 0;
        foreach (var mh in md.MethodDefinitions) {
            int rva = md.GetMethodDefinition(mh).RelativeVirtualAddress;
            if (rva == 0) continue;
            int at = -1;
            foreach (var s in sections)
                if (rva >= s.VirtualAddress && rva < s.VirtualAddress + Math.Max(s.VirtualSize, s.SizeOfRawData)) { at = rva - s.VirtualAddress + s.PointerToRawData; break; }
            if (at < 0) continue;
            int size, start;
            if ((image[at] & 3) == 2) { size = image[at] >> 2; start = at + 1; }      // tiny header
            else { size = BitConverter.ToInt32(image, at + 4); start = at + 4 * (image[at + 1] >> 4); }
            int i = start, end = start + size, lastDomain = -1;
            while (i < end) {
                int opAt = i;
                short v = image[i++];
                if (v == 0xFE) v = (short)(0xFE00 | image[i++]);
                if (!ops.TryGetValue(v, out var op)) break;
                int sz = op.OperandType switch {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(image, i),
                    _ => 4,
                };
                if (v == 0x28 && targets.Contains(BitConverter.ToInt32(image, i))) { image[opAt] = 0x6F; count++; }
                if (v == 0x28 && currentDomain != 0 && BitConverter.ToInt32(image, i) == currentDomain) lastDomain = opAt;
                if ((v == 0x28 || v == 0x6F) && defineDynamic != 0 && BitConverter.ToInt32(image, i) == defineDynamic && lastDomain >= 0) {
                    image[opAt] = 0x28;                                              // call (static)
                    for (int k = 0; k < 5; k++) image[lastDomain + k] = 0x00;         // nop out AppDomain.CurrentDomain
                    lastDomain = -1;
                    count++;
                }
                i += sz;
            }
        }
        return count;
    }

    static void Install(Assembly h) {
        try {
            Log.Info($"harmony: {h.GetName().Version} loaded, fitting it to this runtime");
            fastInvoke = h.GetType("HarmonyLib.FastInvokeHandler", true);
            var getHandler = h.GetType("HarmonyLib.MethodInvoker", true)!.GetMethod("GetHandler", All, null, new[] { typeof(MethodInfo), typeof(bool) }, null)!;
            Compat.Detour(getHandler, typeof(HarmonyCompat).GetMethod(nameof(GetHandler), All)!);
            // Harmony keeps each method's patch list serialized with BinaryFormatter, which .NET 8
            // doesn't support on Android: the lists are kept as objects, the bytes are their key
            var ser = h.GetType("HarmonyLib.PatchInfoSerialization", true)!;
            Compat.Detour(ser.GetMethod("Serialize", All)!, typeof(HarmonyCompat).GetMethod(nameof(Serialize), All)!);
            Compat.Detour(ser.GetMethod("Deserialize", All)!, typeof(HarmonyCompat).GetMethod(nameof(Deserialize), All)!);
        } catch (Exception e) { Log.Warn("harmony: " + e); }
    }

    /// MethodInvoker.GetHandler: AccessTools' static constructor asks for one of
    /// Exception.PrepForRemoting, which .NET 8 doesn't have (null); others call through reflection.
    static Delegate? GetHandler(MethodInfo? method, bool directBoxValueAccess) =>
        method == null ? null : Delegate.CreateDelegate(fastInvoke!, new Invoker(method), typeof(Invoker).GetMethod(nameof(Invoker.Call))!);

    static readonly List<object> patchInfos = new();

    static byte[] Serialize(object patchInfo) {
        lock (patchInfos) {
            int i = patchInfos.IndexOf(patchInfo);
            if (i < 0) { i = patchInfos.Count; patchInfos.Add(patchInfo); }
            return BitConverter.GetBytes(i);
        }
    }

    static object Deserialize(byte[] bytes) { lock (patchInfos) return patchInfos[BitConverter.ToInt32(bytes, 0)]; }

    sealed class Invoker {
        readonly MethodInfo m;
        public Invoker(MethodInfo m) { this.m = m; }
        public object? Call(object? target, object?[] args) => m.Invoke(target, args);
    }
}
