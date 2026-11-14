// SoLoud C API functions Memoria's soloud.dll has beyond FFNx's fork: loop start/end points and
// the 6-channel pan call.
#include "soloud.h"
// WavStreamInstance::mParent is private in SoLoud (implicitly): this file reaches it on purpose
#define class struct
#include "soloud_wavstream.h"
#undef class
using namespace SoLoud;

extern "C" {
__attribute__((visibility("default"))) void Soloud_setLoopStartPoint(void *s, unsigned int h, double t) { ((Soloud *)s)->setLoopPoint(h, t); }

__attribute__((visibility("default"))) void Soloud_setLoopEndPoint(void *s, unsigned int h, double t) {
    auto *so = (Soloud *)s;
    so->lockAudioMutex_internal();
    int ch = so->getVoiceFromHandle_internal(h);
    if (ch >= 0 && so->mVoice[ch]) {
        // a voice's loop end lives on its WavStream source (one voice per stream in Memoria)
        auto *inst = dynamic_cast<WavStreamInstance *>(so->mVoice[ch]);
        if (inst) inst->mParent->mLoopEnd = t;
    }
    so->unlockAudioMutex_internal();
}

__attribute__((visibility("default"))) void WavStream_setLoopStartPoint(void *w, double t) { ((WavStream *)w)->setLoopPoint(t); }
__attribute__((visibility("default"))) double WavStream_getLoopStartPoint(void *w) { return ((WavStream *)w)->getLoopPoint(); }
__attribute__((visibility("default"))) void WavStream_setLoopEndPoint(void *w, double t) { ((WavStream *)w)->mLoopEnd = t; }
__attribute__((visibility("default"))) double WavStream_getLoopEndPoint(void *w) { return ((WavStream *)w)->mLoopEnd; }

__attribute__((visibility("default"))) void Soloud_setPanAbsoluteEx(void *s, unsigned int h, float l, float r, float lb, float rb, float c, float sw) {
    ((Soloud *)s)->setPanAbsolute(h, l, r);
}
}
