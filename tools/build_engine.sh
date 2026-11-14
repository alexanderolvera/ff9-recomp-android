#!/bin/bash
# Build out/engine/FF9Engine.dll (C#, against .NET 8 and the game's own UnityEngine.dll).
set -e
cd "$(dirname "$0")/.."
[ -f out/tools/rcsc.exe ] || bash tools/build_rcsc.sh
L=dl/mono/pkg/runtimes/android-arm64/lib/net8.0
M=work/FF9-memoria/x64/FF9_Data/Managed
mkdir -p out/engine
refs=""
for f in $L/*.dll dl/mono/pkg/runtimes/android-arm64/native/System.Private.CoreLib.dll; do refs="$refs -r:$f"; done
for a in UnityEngine UnityEngine.UI Assembly-CSharp Assembly-CSharp-firstpass Memoria.Prime; do refs="$refs -r:$M/$a.dll"; done
out/tools/rcsc.exe -nologo -nostdlib -noconfig -target:library -langversion:latest -unsafe -optimize -debug:portable \
  -nowarn:1701,1702,8019 -out:out/engine/FF9Engine.dll $refs $(find src/engine -name '*.cs')
