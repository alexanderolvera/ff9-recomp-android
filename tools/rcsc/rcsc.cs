// Minimal C# compiler driver: csc's command line, compiled by Roslyn taken from the user's
// Memoria install (StreamingAssets/Scripts/Compiler). Built by tools/build_rcsc.sh with the
// .NET Framework's own csc.exe (C# 5), so no .NET SDK is needed.
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

static class Rcsc {
    static int Main(string[] args) {
        var parsed = CSharpCommandLineParser.Default.Parse(args, Environment.CurrentDirectory, null, null);
        foreach (var d in parsed.Errors) Console.Error.WriteLine(d.ToString());
        if (parsed.Errors.Length > 0) return 2;
        var trees = new List<SyntaxTree>();
        foreach (var f in parsed.SourceFiles)
            trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(f.Path), parsed.ParseOptions, f.Path,
                                                 System.Text.Encoding.UTF8));
        var refs = new List<MetadataReference>();
        foreach (var r in parsed.MetadataReferences) {
            string p = r.Reference;
            if (!File.Exists(p))
                foreach (var dir in parsed.ReferencePaths)
                    if (File.Exists(Path.Combine(dir, p))) { p = Path.Combine(dir, p); break; }
            refs.Add(MetadataReference.CreateFromFile(p));
        }
        string name = parsed.CompilationName ?? Path.GetFileNameWithoutExtension(parsed.OutputFileName);
        var comp = CSharpCompilation.Create(name, trees, refs, parsed.CompilationOptions);
        string outPath = Path.Combine(parsed.OutputDirectory, parsed.OutputFileName);
        string pdb = parsed.EmitPdb ? Path.ChangeExtension(outPath, ".pdb") : null;
        Microsoft.CodeAnalysis.Emit.EmitResult res;
        using (var pe = new MemoryStream())
        using (var ps = new MemoryStream()) {
            res = comp.Emit(pe, pdb != null ? ps : null, options: parsed.EmitOptions);
            int warnings = 0;
            foreach (var d in res.Diagnostics) {
                if (d.Severity == DiagnosticSeverity.Error) Console.Error.WriteLine(d.ToString());
                else if (d.Severity == DiagnosticSeverity.Warning) {
                    warnings++;
                    if (Environment.GetEnvironmentVariable("RCSC_WARN") != null) Console.Error.WriteLine(d.ToString());
                }
            }
            if (!res.Success) return 1;
            File.WriteAllBytes(outPath, pe.ToArray());
            if (pdb != null) File.WriteAllBytes(pdb, ps.ToArray());
            Console.WriteLine(outPath + " (" + warnings + " warnings)");
        }
        return 0;
    }
}
