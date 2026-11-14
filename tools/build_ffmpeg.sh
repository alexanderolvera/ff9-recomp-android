#!/usr/bin/env bash
# FFmpeg for Android arm64 (static), only what FF9's movies need: Ogg demuxer, Theora (vp3.c)
# and Vorbis decoders, swscale. FF9's movies (StreamingAssets/ma/*.bytes) are Ogg Theora; the
# game's theorawrapper.dll is replaced by src/host/theora.c on top of these.
# Source: FF7Recomp's FFmpeg checkout (n9.0.2). Output: out/ffmpeg/{include,lib}.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="${FFMPEG_SRC:-$ROOT/../FF7Recomp/external/deps/ffmpeg}"
OUT="$ROOT/out/ffmpeg"
SDK="${ANDROID_SDK:-$LOCALAPPDATA/Android/Sdk}"
SDK="$(cygpath -u "$SDK")"
NDK="$SDK/ndk/28.2.13676358"
TC="$NDK/toolchains/llvm/prebuilt/windows-x86_64/bin"
MAKE="$NDK/prebuilt/windows-x86_64/bin/make.exe"
HOST_CC="${HOST_CC:-/c/Program Files/LLVM/bin/clang.exe}"
API=30
mkdir -p "$OUT/build" "$OUT/tmp"
export TMPDIR="$OUT/tmp" TMP="$OUT/tmp" TEMP="$OUT/tmp"
printf '#!/bin/sh\nexec "%s" "$@"\n' "$HOST_CC" > "$OUT/hostcc.sh"
chmod +x "$OUT/hostcc.sh"
# keep this PC's folder names out of the binaries (they end up in the APK)
PREFIX_MAP="-ffile-prefix-map=$(cygpath -m "$SRC")=ffmpeg -ffile-prefix-map=$(cygpath -m "$OUT")=build"
cd "$OUT/build"
"$SRC/configure" \
  --prefix="$(cygpath -m "$OUT")" \
  --target-os=android --arch=aarch64 --cpu=armv8-a --enable-cross-compile \
  --cc="$TC/aarch64-linux-android$API-clang.cmd" \
  --cxx="$TC/aarch64-linux-android$API-clang++.cmd" \
  --ar="$TC/llvm-ar.exe" --nm="$TC/llvm-nm.exe" --ranlib="$TC/llvm-ranlib.exe" --strip="$TC/llvm-strip.exe" \
  --pkg-config=false --host-cc="$OUT/hostcc.sh" \
  --enable-static --disable-shared --enable-pic \
  --disable-programs --disable-doc --disable-debug --disable-avdevice --disable-avfilter \
  --disable-everything \
  --enable-avcodec --enable-avformat --enable-avutil --enable-swscale --enable-swresample \
  --enable-protocol=file \
  --enable-demuxer=ogg \
  --enable-decoder=theora,vp3,vorbis \
  --enable-parser=vp3,vorbis \
  --enable-neon --disable-vulkan --disable-mediacodec --disable-jni \
  --extra-cflags="-O2 -fPIC $PREFIX_MAP" > "$OUT/configure.log" 2>&1 || { tail -30 "$OUT/configure.log"; exit 1; }
sed -i 's|/\([a-z]\)/Users/|\U\1\E:/Users/|g' Makefile ffbuild/config.mak
sed -i 's|^#define FFMPEG_CONFIGURATION .*|#define FFMPEG_CONFIGURATION "FF9Recomp tools/build_ffmpeg.sh"|' config.h
echo "configured; building"
"$MAKE" -j"$(nproc)" > "$OUT/make.log" 2>&1 || { tail -30 "$OUT/make.log"; exit 1; }
"$MAKE" install > "$OUT/install.log" 2>&1 || true
for l in libavcodec libavformat libavutil libswresample libswscale; do
  mkdir -p "$OUT/include/$l"; cp "$SRC/$l"/*.h "$OUT/include/$l/"
done
cp libavutil/avconfig.h libavutil/ffversion.h "$OUT/include/libavutil/"
ls -la "$OUT/lib"
