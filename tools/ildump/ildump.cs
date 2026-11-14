// IL/metadata dumper for the game's managed DLLs (PC tool, runs on the .NET 8 runtime).
//   ildump type   <dll> <Type> [method]   fields, methods with signatures (and IL with a method or IL=1)
//   ildump icalls <dll>                   every InternalCall with a C# signature for FF9Engine
//   ildump find   <dll> <text>            methods whose IL calls a member whose name contains text
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Emit = System.Reflection.Emit;

sealed class Names : ISignatureTypeProvider<string, object?> {
    public string GetPrimitiveType(PrimitiveTypeCode c) => c switch {
        PrimitiveTypeCode.Boolean => "bool", PrimitiveTypeCode.Byte => "byte", PrimitiveTypeCode.SByte => "sbyte",
        PrimitiveTypeCode.Char => "char", PrimitiveTypeCode.Int16 => "short", PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.Int32 => "int", PrimitiveTypeCode.UInt32 => "uint", PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.UInt64 => "ulong", PrimitiveTypeCode.Single => "float", PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.IntPtr => "IntPtr", PrimitiveTypeCode.UIntPtr => "UIntPtr", PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.String => "string", PrimitiveTypeCode.Void => "void", _ => c.ToString() };
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => Ildump.TypeName(r, h);
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => Ildump.TypeRefName(r, h);
    public string GetTypeFromSpecification(MetadataReader r, object? c, TypeSpecificationHandle h, byte k) =>
        r.GetTypeSpecification(h).DecodeSignature(this, c);
    public string GetSZArrayType(string e) => e + "[]";
    public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string e) => "ref " + e;
    public string GetPointerType(string e) => e + "*";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) =>
        (g.Contains('`') ? g[..g.IndexOf('`')] : g) + "<" + string.Join(", ", a) + ">";
    public string GetGenericTypeParameter(object? c, int i) => "T" + i;
    public string GetGenericMethodParameter(object? c, int i) => "M" + i;
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetModifiedType(string m, string u, bool req) => u;
    public string GetPinnedType(string e) => e;
}

static class Ildump {
    public static string TypeName(MetadataReader r, TypeDefinitionHandle h) {
        var t = r.GetTypeDefinition(h);
        string n = r.GetString(t.Name);
        if (t.IsNested) return TypeName(r, t.GetDeclaringType()) + "/" + n;
        string ns = r.GetString(t.Namespace);
        return ns.Length > 0 ? ns + "." + n : n;
    }

    public static string TypeRefName(MetadataReader r, TypeReferenceHandle h) {
        var t = r.GetTypeReference(h);
        string n = r.GetString(t.Name);
        if (t.ResolutionScope.Kind == HandleKind.TypeReference)
            return TypeRefName(r, (TypeReferenceHandle)t.ResolutionScope) + "/" + n;
        string ns = r.GetString(t.Namespace);
        return ns.Length > 0 ? ns + "." + n : n;
    }

    static MetadataReader md = null!;
    static readonly Names prov = new();

    static string Parent(EntityHandle p) => p.Kind switch {
        HandleKind.TypeReference => TypeRefName(md, (TypeReferenceHandle)p),
        HandleKind.TypeDefinition => TypeName(md, (TypeDefinitionHandle)p),
        HandleKind.TypeSpecification => md.GetTypeSpecification((TypeSpecificationHandle)p).DecodeSignature(prov, null),
        _ => "?" };

    static string MemberName(EntityHandle h) {
        switch (h.Kind) {
            case HandleKind.MethodDefinition: {
                var m = md.GetMethodDefinition((MethodDefinitionHandle)h);
                var s = m.DecodeSignature(prov, null);
                return $"{s.ReturnType} {TypeName(md, m.GetDeclaringType())}::{md.GetString(m.Name)}({string.Join(", ", s.ParameterTypes)})";
            }
            case HandleKind.MemberReference: {
                var m = md.GetMemberReference((MemberReferenceHandle)h);
                if (m.GetKind() == MemberReferenceKind.Field)
                    return $"{m.DecodeFieldSignature(prov, null)} {Parent(m.Parent)}::{md.GetString(m.Name)}";
                var s = m.DecodeMethodSignature(prov, null);
                return $"{s.ReturnType} {Parent(m.Parent)}::{md.GetString(m.Name)}({string.Join(", ", s.ParameterTypes)})";
            }
            case HandleKind.FieldDefinition: {
                var f = md.GetFieldDefinition((FieldDefinitionHandle)h);
                return $"{f.DecodeSignature(prov, null)} {TypeName(md, f.GetDeclaringType())}::{md.GetString(f.Name)}";
            }
            case HandleKind.MethodSpecification: {
                var ms = md.GetMethodSpecification((MethodSpecificationHandle)h);
                return MemberName(ms.Method) + "<" + string.Join(", ", ms.DecodeSignature(prov, null)) + ">";
            }
            case HandleKind.TypeDefinition: case HandleKind.TypeReference: case HandleKind.TypeSpecification:
                return Parent(h);
        }
        return h.Kind.ToString();
    }

    static readonly Dictionary<ushort, Emit.OpCode> ops = typeof(Emit.OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (Emit.OpCode)f.GetValue(null)!)
        .ToDictionary(o => (ushort)o.Value);

    static void DumpIL(PEReader pe, MethodDefinition m) {
        if (m.RelativeVirtualAddress == 0) return;
        var il = pe.GetMethodBody(m.RelativeVirtualAddress).GetILReader();
        while (il.RemainingBytes > 0) {
            int at = il.Offset;
            ushort v = il.ReadByte();
            if (v == 0xFE) v = (ushort)(0xFE00 | il.ReadByte());
            var op = ops[v];
            string arg = "";
            switch (op.OperandType) {
                case Emit.OperandType.InlineNone: break;
                case Emit.OperandType.ShortInlineBrTarget: { int d = il.ReadSByte(); arg = $"IL_{il.Offset + d:x4}"; break; }
                case Emit.OperandType.InlineBrTarget: { int d = il.ReadInt32(); arg = $"IL_{il.Offset + d:x4}"; break; }
                case Emit.OperandType.ShortInlineI: case Emit.OperandType.ShortInlineVar: arg = il.ReadByte().ToString(); break;
                case Emit.OperandType.InlineVar: arg = il.ReadUInt16().ToString(); break;
                case Emit.OperandType.InlineI: arg = il.ReadInt32().ToString(); break;
                case Emit.OperandType.InlineI8: arg = il.ReadInt64().ToString(); break;
                case Emit.OperandType.ShortInlineR: arg = il.ReadSingle().ToString(); break;
                case Emit.OperandType.InlineR: arg = il.ReadDouble().ToString(); break;
                case Emit.OperandType.InlineString:
                    arg = "\"" + md.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32() & 0xFFFFFF)) + "\""; break;
                case Emit.OperandType.InlineSwitch: {
                    int n = il.ReadInt32(); var t = new int[n];
                    for (int i = 0; i < n; i++) t[i] = il.ReadInt32();
                    int b = il.Offset;
                    arg = string.Join(",", t.Select(d => $"IL_{b + d:x4}")); break;
                }
                default: arg = MemberName(MetadataTokens.EntityHandle(il.ReadInt32())); break;
            }
            Console.WriteLine($"    IL_{at:x4}: {op.Name} {arg}");
        }
    }

    static string Args(MethodDefinition m, MethodSignature<string> s) {
        var pn = new List<string>();
        foreach (var ph in m.GetParameters()) {
            var p = md.GetParameter(ph);
            if (p.SequenceNumber > 0) pn.Add(md.GetString(p.Name));
        }
        var parts = new List<string>();
        for (int i = 0; i < s.ParameterTypes.Length; i++) {
            string t = s.ParameterTypes[i];
            string n = i < pn.Count ? pn[i] : "a" + i;
            parts.Add($"{t} {n}");
        }
        return string.Join(", ", parts);
    }

    static int Main(string[] a) {
        using var pe = new PEReader(File.OpenRead(a[1]));
        md = pe.GetMetadataReader();
        if (a[0] == "type") {
            foreach (var th in md.TypeDefinitions) {
                if (TypeName(md, th) != a[2]) continue;
                var t = md.GetTypeDefinition(th);
                Console.WriteLine($"type {a[2]} : {(t.BaseType.IsNil ? "" : MemberName(t.BaseType))} [{t.Attributes}]");
                foreach (var fh in t.GetFields()) {
                    var f = md.GetFieldDefinition(fh);
                    Console.WriteLine($"  field [{f.Attributes}] {f.DecodeSignature(prov, null)} {md.GetString(f.Name)}");
                }
                foreach (var mh in t.GetMethods()) {
                    var m = md.GetMethodDefinition(mh);
                    string name = md.GetString(m.Name);
                    if (a.Length > 3 && name != a[3]) continue;
                    var s = m.DecodeSignature(prov, null);
                    bool ic = (m.ImplAttributes & MethodImplAttributes.InternalCall) != 0;
                    bool st = (m.Attributes & MethodAttributes.Static) != 0;
                    Console.WriteLine($"  {(ic ? "[icall] " : "")}{(st ? "static " : "")}{s.ReturnType} {name}({Args(m, s)})");
                    if (a.Length > 3 || Environment.GetEnvironmentVariable("IL") != null) DumpIL(pe, m);
                }
            }
        } else if (a[0] == "icalls") {
            foreach (var th in md.TypeDefinitions) {
                string tn = TypeName(md, th);
                foreach (var mh in md.GetTypeDefinition(th).GetMethods()) {
                    var m = md.GetMethodDefinition(mh);
                    if ((m.ImplAttributes & MethodImplAttributes.InternalCall) == 0) continue;
                    var s = m.DecodeSignature(prov, null);
                    string args = Args(m, s);
                    if ((m.Attributes & MethodAttributes.Static) == 0)
                        args = $"{tn.Replace('/', '.')} self" + (args.Length > 0 ? ", " + args : "");
                    Console.WriteLine($"{tn}::{md.GetString(m.Name)}\t{s.ReturnType}\t{args}");
                }
            }
        } else if (a[0] == "pinvokes") {
            foreach (var th in md.TypeDefinitions) {
                string tn = TypeName(md, th);
                foreach (var mh in md.GetTypeDefinition(th).GetMethods()) {
                    var m = md.GetMethodDefinition(mh);
                    var imp = m.GetImport();
                    if (imp.Module.IsNil) continue;
                    string mod = md.GetString(md.GetModuleReference(imp.Module).Name);
                    if (a.Length > 2 && !mod.Contains(a[2], StringComparison.OrdinalIgnoreCase)) continue;
                    var s = m.DecodeSignature(prov, null);
                    Console.WriteLine($"{mod}\t{md.GetString(imp.Name)}\t[{imp.Attributes}]\t{s.ReturnType} {tn}::{md.GetString(m.Name)}({Args(m, s)})");
                }
            }
        } else if (a[0] == "find") {
            foreach (var th in md.TypeDefinitions)
                foreach (var mh in md.GetTypeDefinition(th).GetMethods()) {
                    var m = md.GetMethodDefinition(mh);
                    if (m.RelativeVirtualAddress == 0) continue;
                    var il = pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes()!;
                    for (int i = 0; i + 4 < il.Length; i++) {
                        if (il[i] != 0x28 && il[i] != 0x6F && il[i] != 0x73) continue;
                        int tok = BitConverter.ToInt32(il, i + 1);
                        if ((tok >> 24) is not (0x06 or 0x0A or 0x2B)) continue;
                        try {
                            if (MemberName(MetadataTokens.EntityHandle(tok)).Contains(a[2])) {
                                Console.WriteLine($"{TypeName(md, th)}::{md.GetString(m.Name)}");
                                break;
                            }
                        } catch { }
                    }
                }
        }
        return 0;
    }
}
