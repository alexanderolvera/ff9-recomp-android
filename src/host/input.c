/* Gamepad: the game reads XInput through XInputDotNetPure (XInputInterface.dll on PC). The
 * platform layer (the APK's input code, or evdev in the headless host) fills ff9_pad. */
#include <stdint.h>
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

EXPORT uint32_t XInputGamePadGetState(uint32_t player, XInputRawState *s) {
    if (player != 0 || !ff9_pad_connected) return 1167;   /* ERROR_DEVICE_NOT_CONNECTED */
    *s = ff9_pad;
    return 0;
}

EXPORT void XInputGamePadSetState(uint32_t player, float left, float right) {}

/* called by the platform layer */
void ff9_pad_set(uint16_t buttons, uint8_t lt, uint8_t rt, int16_t lx, int16_t ly, int16_t rx, int16_t ry) {
    ff9_pad.packet++;
    ff9_pad.buttons = buttons;
    ff9_pad.left_trigger = lt;
    ff9_pad.right_trigger = rt;
    ff9_pad.lx = lx; ff9_pad.ly = ly; ff9_pad.rx = rx; ff9_pad.ry = ry;
}
