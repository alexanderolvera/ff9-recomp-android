#!/bin/bash
# Build the host and run it on the Thor: bash tools/build_host.sh <ff9host args after the dir>
set -e
cd "$(dirname "$0")/.."
SDK=${ANDROID_SDK:-$LOCALAPPDATA/Android/Sdk}
CC=$SDK/ndk/28.2.13676358/toolchains/llvm/prebuilt/windows-x86_64/bin/clang.exe
ADB=$SDK/platform-tools/adb.exe
N=dl/mono/pkg/runtimes/android-arm64/native
"$CC" --target=aarch64-linux-android30 -O1 -g -Wall -Wno-unused-function -I$N/include/mono-2.0 \
  src/host/*.c out/host/ue_icalls.c -L$N -lmonosgen-2.0 -lm -o out/host/ff9host
MSYS_NO_PATHCONV=1 "$ADB" push out/host/ff9host /data/local/tmp/ff9/ff9host >/dev/null
MSYS_NO_PATHCONV=1 "$ADB" shell "cd /data/local/tmp/ff9 && chmod 755 ff9host && LD_LIBRARY_PATH=/data/local/tmp/ff9/bcl $FF9_ENV ./ff9host /data/local/tmp/ff9 $*"
