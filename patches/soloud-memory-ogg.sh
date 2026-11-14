#!/bin/bash
# FFNx's SoLoud fork opens Ogg streams with stb_vorbis_open_file(getFilePtr()), which is null for
# in-memory files (Memoria's WavStream.loadMem). Fall back to stb_vorbis_open_memory.
# Applied by tools/build_soloud.sh to its copy of the sources ($1 = wavstream source file).
f="$1"
sed -i 's|mCodec.mOgg = stb_vorbis_open_file(mFile->getFilePtr(), 0, &e, 0);|mCodec.mOgg = mFile->getFilePtr() ? stb_vorbis_open_file(mFile->getFilePtr(), 0, \&e, 0) : stb_vorbis_open_memory(mFile->getMemPtr(), (int)mFile->length(), \&e, 0);|' "$f"
sed -i 's|v = stb_vorbis_open_file(fp->getFilePtr(), 0, &e, 0);|v = fp->getFilePtr() ? stb_vorbis_open_file(fp->getFilePtr(), 0, \&e, 0) : stb_vorbis_open_memory(fp->getMemPtr(), (int)fp->length(), \&e, 0);|' "$f"
grep -c "stb_vorbis_open_memory" "$f"
