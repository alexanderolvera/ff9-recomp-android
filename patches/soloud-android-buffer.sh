#!/bin/bash
# Memoria starts SoLoud with an automatic buffer size, which the miniaudio backend makes 2048
# frames: 43 ms at 48 kHz, and a sound only starts at the next buffer, so menu sounds came late
# or were skipped when scrolling quickly (GitHub issue 12). 512 frames (11 ms) on the phone.
# $1 = source copy root.
SL="$1"
c=$SL/src/core/soloud.cpp
sed -i '/#if defined(WITH_MINIAUDIO)/,/miniaudio_init(this/ s|if (aBufferSize == Soloud::AUTO) buffersize = 2048;|if (aBufferSize == Soloud::AUTO) buffersize = 512;   // FF9Recomp: menu sounds on time|' $c
grep -c "FF9Recomp: menu sounds on time" $c
