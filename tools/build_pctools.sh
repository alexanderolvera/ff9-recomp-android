#!/bin/bash
# PC-side C# tools, run with the installed .NET 8 runtime: dotnet out/tools/<name>.dll ...
set -e
cd "$(dirname "$0")/.."
[ -f out/tools/rcsc.exe ] || bash tools/build_rcsc.sh
FW=$(ls -d "/c/Program Files/dotnet/shared/Microsoft.NETCore.App/8."* | tail -1)
rsp=out/tools/pcrefs.rsp
: > $rsp
for f in "$FW"/*.dll; do
  case "$(basename "$f")" in
    *Native*) ;;
    System.*|Microsoft.CSharp.dll|Microsoft.VisualBasic*|Microsoft.Win32.*|mscorlib.dll|netstandard.dll|WindowsBase.dll)
      echo "-r:\"$(cygpath -w "$f")\"" >> $rsp;;
  esac
done
for t in ${*:-ildump}; do
  out/tools/rcsc.exe -nologo -nostdlib -noconfig -langversion:latest -nullable:enable -optimize \
    -out:out/tools/$t.dll @$rsp tools/$t/*.cs
  printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}' \
    > out/tools/$t.runtimeconfig.json
done
