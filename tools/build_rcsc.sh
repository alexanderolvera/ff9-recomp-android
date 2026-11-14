#!/bin/bash
# Build out/tools/rcsc.exe (C# compiler driver) from Roslyn in the user's Memoria install.
set -e
cd "$(dirname "$0")/.."
C=work/FF9-memoria/StreamingAssets/Scripts/Compiler
mkdir -p out/tools
cp $C/*.dll out/tools/
cp $C/Memoria.Compiler.exe.config out/tools/rcsc.exe.config
/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe -nologo -out:out/tools/rcsc.exe \
  -r:out/tools/Microsoft.CodeAnalysis.dll -r:out/tools/Microsoft.CodeAnalysis.CSharp.dll \
  -r:out/tools/System.Collections.Immutable.dll -r:System.Runtime.dll -r:netstandard.dll "tools\rcsc\rcsc.cs"
