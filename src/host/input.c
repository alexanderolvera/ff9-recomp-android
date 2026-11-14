/* Gamepad: the game reads XInput through XInputDotNetPure (XInputInterface.dll on PC). The
 * platform layer (the APK's input code, or evdev in the headless host) fills ff9_pad. */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define EXPORT __attribute__((visibility("default")))

typedef struct {
    uint32_t packet;
    uint16_t buttons;          /* XINPUT_GAMEPAD_* bits */
    uint8_t left_trigger, right_trigger;
    int16_t lx, ly, rx, ry;
} XInputRawState;

XInputRawState ff9_pad;
int ff9_pad_connected = 1;
/* A press stays visible for at least MIN_HOLD_NS, so a tap shorter than a frame (or one the
   game's several pad readers would see only once) still registers. */
#include <time.h>
#define MIN_HOLD_NS 80000000LL
static int64_t pressed_at[16];

static int64_t now_ns(void) { struct timespec ts; clock_gettime(CLOCK_MONOTONIC, &ts); return (int64_t)ts.tv_sec * 1000000000LL + ts.tv_nsec; }

EXPORT uint32_t XInputGamePadGetState(uint32_t player, XInputRawState *s) {
    if (player != 0 || !ff9_pad_connected) return 1167;   /* ERROR_DEVICE_NOT_CONNECTED */
    *s = ff9_pad;
    int64_t t = now_ns();
    for (int i = 0; i < 16; i++)
        if (!(s->buttons & (1u << i)) && pressed_at[i] && t - __atomic_load_n(&pressed_at[i], __ATOMIC_ACQUIRE) < MIN_HOLD_NS)
            s->buttons |= (uint16_t)(1u << i);
    return 0;
}

/* the game's rumble (both motors, 0..1): the APK reads it (nativeRumble) and drives a vibrator */
volatile float ff9_rumble_left, ff9_rumble_right;
EXPORT void XInputGamePadSetState(uint32_t player, float left, float right) {
    if (player != 0) return;
    ff9_rumble_left = left;
    ff9_rumble_right = right;
}

/* frames shown (FF9Engine Gfx/Device.Present), for the app's FPS counter */
volatile uint32_t ff9_frames_shown;
EXPORT void ff9_frame_shown(void) { __atomic_add_fetch(&ff9_frames_shown, 1, __ATOMIC_RELAXED); }

/* called by the platform layer */
void ff9_pad_set(uint16_t buttons, uint8_t lt, uint8_t rt, int16_t lx, int16_t ly, int16_t rx, int16_t ry) {
    ff9_pad.packet++;
    uint16_t down = buttons & ~ff9_pad.buttons;
    if (down) { int64_t t = now_ns(); for (int i = 0; i < 16; i++) if (down & (1u << i)) __atomic_store_n(&pressed_at[i], t, __ATOMIC_RELEASE); }
    ff9_pad.buttons = buttons;
    ff9_pad.left_trigger = lt;
    ff9_pad.right_trigger = rt;
    ff9_pad.lx = lx; ff9_pad.ly = ly; ff9_pad.rx = rx; ff9_pad.ry = ry;
}

/* scripted input for headless tests: FF9_PAD_SCRIPT="from-to:buttons,..." with buttons as XInput
   bits in hex, e.g. "900-905:1000" presses A on frames 900..905 (driven by FF9Engine each frame) */
EXPORT void ff9_pad_frame(int frame) {
    static char *script;
    static int loaded;
    if (!loaded) { loaded = 1; const char *s = getenv("FF9_PAD_SCRIPT"); if (s) script = strdup(s); }
    if (!script) return;
    uint16_t buttons = 0;
    for (char *p = script; *p;) {
        int a = 0, b = 0; unsigned v = 0;
        if (sscanf(p, "%d-%d:%x", &a, &b, &v) == 3 && frame >= a && frame <= b) buttons |= (uint16_t)v;
        char *c = strchr(p, ',');
        if (!c) break;
        p = c + 1;
    }
    ff9_pad.packet++;
    ff9_pad.buttons = buttons;
}
