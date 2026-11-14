#!/bin/bash
# Build the host and run it on the Thor: bash tools/build_host.sh <ff9host args after the dir>
set -e
cd "$(dirname "$0")/.."
SDK=${ANDROID_SDK:-$LOCALAPPDATA/Android/Sdk}
CC=$SDK/ndk/28.2.13676358/toolchains/llvm/prebuilt/windows-x86_64/bin/clang.exe
ADB="$SDK/platform-tools/adb.exe -s ${ANDROID_SERIAL:-76dd1750}"
N=dl/mono/pkg/runtimes/android-arm64/native
"${CC%clang.exe}clang++.exe" --target=aarch64-linux-android30 -O2 -std=c++17 -c src/host/lowmem.cpp -o out/host/lowmem.o
"${CC%clang.exe}clang++.exe" --target=aarch64-linux-android30 -O2 -std=c++17 -w -DWITH_MINIAUDIO -Iout/soloud/src-copy/include -c src/host/soloud_ext.cpp -o out/host/soloud_ext.o
"$CC" --target=aarch64-linux-android30 -O1 -g -Wall -Wno-unused-function -I$N/include/mono-2.0 -Iout/ffmpeg/include \
  src/host/*.c ${GLTRACE:+src/host/debug/gltrace.c} out/host/ue_stubs.c out/host/ue_thunks.S out/host/lowmem.o out/host/soloud_ext.o -L$N -lmonosgen-2.0 -lm -Wl,--whole-archive out/soloud/libsoloud.a -Wl,--no-whole-archive -Lout/ffmpeg/lib -lavformat -lavcodec -lswscale -lswresample -lavutil -lz -lGLESv3 -lEGL -lc++_static -lc++abi -lOpenSLES -laaudio -llog -Wl,--export-dynamic -o out/host/ff9host
[ "$NO_ENGINE" ] || bash tools/build_engine.sh >/dev/null
MSYS_NO_PATHCONV=1 $ADB push out/engine/FF9Engine.dll /data/local/tmp/ff9/engine/FF9Engine.dll >/dev/null
MSYS_NO_PATHCONV=1 $ADB push out/host/ff9host /data/local/tmp/ff9/ff9host >/dev/null
MSYS_NO_PATHCONV=1 $ADB shell "cd /data/local/tmp/ff9 && chmod 755 ff9host && LD_LIBRARY_PATH=/data/local/tmp/ff9/bcl $FF9_ENV ./ff9host /data/local/tmp/ff9 $*"
