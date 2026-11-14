/* FF9SpecialEffectPlugin stand-in (placeholder until the plugin itself is ported): the battle
   effect/camera engine's 13 entry points, doing nothing. With Memoria's SFXRework the effects
   draw from its own data; the camera stays at a fixed view. */
#include <stdint.h>
#include <string.h>

#define EXPORT __attribute__((visibility("default")))

typedef void (*SfxCallback)(void);

static float camera[13];

EXPORT void SFX_InitSystem(SfxCallback cb) { (void)cb; }
EXPORT void SFX_StartPlungeCamera(void *btlseq, int len, int camOffset, int projOffset) { (void)btlseq; (void)len; (void)camOffset; (void)projOffset; }
EXPORT void SFX_SkipCameraAnimation(int skip) { (void)skip; }
EXPORT void SFX_InitBattle(void *param) { (void)param; }
EXPORT int SFX_Update(int *frameIndex) { (void)frameIndex; return 0; }
EXPORT void SFX_LateUpdate(void) {}
EXPORT void *SFX_UpdateCamera(int isDebug) {
    (void)isDebug;
    /* a PSX camera: 3x3 rotation (4096 = 1.0), translation, projection distance */
    memset(camera, 0, sizeof camera);
    camera[0] = 4096; camera[4] = 3500; camera[5] = -2100; camera[7] = 2100; camera[8] = 3500;
    camera[9] = 0; camera[10] = 1000; camera[11] = 8000;
    camera[12] = 350;
    return camera;
}
EXPORT void SFX_MoveFreeCamera(int type, int x, int y) { (void)type; (void)x; (void)y; }
EXPORT int SFX_SendFloatData(int type, int btl_id, float a0, float a1, float a2) { (void)type; (void)btl_id; (void)a0; (void)a1; (void)a2; return 0; }
EXPORT int SFX_SendIntData(int type, int a0, int a1, int a2) { (void)type; (void)a0; (void)a1; (void)a2; return 0; }
EXPORT void SFX_Play(int effnum, void *bin, int size, void *req) { (void)effnum; (void)bin; (void)size; (void)req; }
EXPORT int SFX_BeginRender(void) { return 0; }
EXPORT void *SFX_GetPrim(int *otz) { (void)otz; return 0; }
