#!/bin/bash
# Memoria's SoLoud build adds a loop end point to WavStream (FF9 music loops between two points
# inside the stream). $1 = source copy root.
SL="$1"
h=$SL/include/soloud_wavstream.h
c=$SL/src/audiosource/wav/soloud_wavstream.cpp
sed -i 's|		unsigned int mSampleCount;|		unsigned int mSampleCount;\n		double mLoopEnd = 0;   // seconds; 0 = end of stream (FF9Recomp: Memoria loop end point)|' $h
# stop reading at the loop end, and report the end there so the core loops back to the loop point
sed -i '0,/		unsigned int offset = 0;\n/s||&|' $c
sed -i 's|	unsigned int WavStreamInstance::getAudio(float \*aBuffer, unsigned int aSamplesToRead, unsigned int aBufferSize)\r\?$|&\n	{\n		if (mParent->mLoopEnd > 0) {\n			unsigned int endS = (unsigned int)(mParent->mLoopEnd * mParent->mBaseSamplerate);\n			if (mOffset >= endS) return 0;\n			if (mOffset + aSamplesToRead > endS) aSamplesToRead = endS - mOffset;\n		}\n		return getAudioInner(aBuffer, aSamplesToRead, aBufferSize);\n	}\n	unsigned int WavStreamInstance::getAudioInner(float *aBuffer, unsigned int aSamplesToRead, unsigned int aBufferSize)|' $c
sed -i 's|		virtual unsigned int getAudio(float \*aBuffer, unsigned int aSamplesToRead, unsigned int aBufferSize);|&\n		unsigned int getAudioInner(float *aBuffer, unsigned int aSamplesToRead, unsigned int aBufferSize);|' $h
sed -i 's|	bool WavStreamInstance::hasEnded()\r\?$|&\n	{\n		if (mParent->mLoopEnd > 0 \&\& mOffset >= (unsigned int)(mParent->mLoopEnd * mParent->mBaseSamplerate)) return 1;\n		return hasEndedInner();\n	}\n	bool WavStreamInstance::hasEndedInner()|' $c
sed -i 's|		virtual bool hasEnded();|&\n		bool hasEndedInner();|' $h
grep -c "getAudioInner\|hasEndedInner\|mLoopEnd" $c $h
