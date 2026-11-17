#!/bin/bash
# SoLoud (zlib licence) for Android arm64, with its C API: Memoria's SoLoud audio backend P/Invokes
# soloud.dll (Soloud_*, Bus_*, Queue_*, WavStream_*). Sources: FF7Recomp's copy of FFNx's SoLoud fork.
# Output: out/soloud/libsoloud.a (linked into the host, which exports the C API).
set -e
cd "$(dirname "$0")/.."
SDK=${ANDROID_SDK:-$LOCALAPPDATA/Android/Sdk}
BIN=$SDK/ndk/28.2.13676358/toolchains/llvm/prebuilt/windows-x86_64/bin
SRC=${SOLOUD_SRC:-../FF7Recomp/external/deps/soloud}
OUT=out/soloud
mkdir -p $OUT/obj
# a patched copy of the sources (patches/soloud-*.sh)
SL=$OUT/src-copy
if [ ! -f $SL/.patched ]; then
  rm -rf $SL && mkdir -p $SL && cp -r $SRC/include $SRC/src $SL/
  bash patches/soloud-memory-ogg.sh $SL/src/audiosource/wav/soloud_wavstream.cpp
  bash patches/soloud-loop-end.sh $SL
  touch $SL/.patched
fi
if [ ! -f $SL/.patched-buffer ]; then
  bash patches/soloud-android-buffer.sh $SL
  touch $SL/.patched-buffer
fi
srcs=$(ls $SL/src/core/*.cpp $SL/src/audiosource/wav/*.cpp $SL/src/audiosource/wav/*.c $SL/src/filter/*.cpp \
          $SL/src/backend/miniaudio/*.cpp $SL/src/c_api/soloud_c.cpp \
          $SL/src/audiosource/{ay,monotone,noise,sfxr,speech,tedsid,vic,vizsn}/*.cpp $SL/src/audiosource/openmpt/*.c* 2>/dev/null)
objs=""
pids=""
for s in $srcs; do
  o=$OUT/obj/$(echo "$s" | sed 's#.*/src/##; s#[/.]#_#g').o
  objs="$objs $o"
  [ -f "$o" ] && [ "$o" -nt "$s" ] && continue
  case "$s" in *.c) cc="$BIN/clang.exe";; *) cc="$BIN/clang++.exe -std=c++17";; esac
  $cc --target=aarch64-linux-android30 -O2 -fPIC -w -DWITH_MINIAUDIO -I$SL/include -I$SL/src/backend/miniaudio -c "$s" -o "$o" &
  pids="$pids $!"
done
for p in $pids; do wait $p; done
rm -f $OUT/libsoloud.a
$BIN/llvm-ar.exe rcs $OUT/libsoloud.a $objs
echo "$OUT/libsoloud.a: $(echo $objs | wc -w) objects"
