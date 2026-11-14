/* Stand-ins for the Windows and Steam DLL functions the game P/Invokes (user32, kernel32,
 * steamwrapper, JoyShockLibrary, __Internal). FF9Engine's DllImport resolver points these
 * libraries at the host executable, which exports these symbols.
 * Gamepad input (XInputInterface) lives in input.c. */
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#define EXPORT __attribute__((visibility("default")))
typedef int BOOL;
typedef struct { int32_t left, top, right, bottom; } RECT;

int ff9_screen_w = 1920, ff9_screen_h = 1080;
static const intptr_t HWND_MAIN = 0x1000, HMONITOR_MAIN = 0x2000;

/* ---- user32 ---- */
EXPORT intptr_t GetActiveWindow(void) { return HWND_MAIN; }
EXPORT intptr_t GetForegroundWindow(void) { return HWND_MAIN; }
EXPORT intptr_t FindWindow(const void *cls, const void *name) { return HWND_MAIN; }
EXPORT intptr_t FindWindowW(const void *cls, const void *name) { return HWND_MAIN; }
EXPORT BOOL SetWindowText(intptr_t h, const void *s) { return 1; }
EXPORT BOOL SetWindowTextW(intptr_t h, const void *s) { return 1; }
EXPORT intptr_t SetWindowLong(intptr_t h, int idx, void *proc) { return 0; }
EXPORT intptr_t SetWindowLongPtr(intptr_t h, int idx, void *proc) { return 0; }
EXPORT intptr_t CallWindowProc(intptr_t prev, intptr_t h, uint32_t msg, intptr_t w, intptr_t l) { return 0; }
EXPORT int GetWindowThreadProcessId(intptr_t h, int *pid) { if (pid) *pid = 0; return 0; }
EXPORT BOOL DestroyWindow(intptr_t h) { return 1; }
EXPORT void MoveWindow(intptr_t h, int x, int y, int w, int hh, BOOL repaint) {}

EXPORT BOOL GetWindowRect(intptr_t h, RECT *r) {
    r->left = 0; r->top = 0; r->right = ff9_screen_w; r->bottom = ff9_screen_h;
    return 1;
}

typedef BOOL (*MonitorEnumProc)(intptr_t monitor, intptr_t hdc, RECT *rect, intptr_t data);
EXPORT BOOL EnumDisplayMonitors(intptr_t hdc, void *clip, MonitorEnumProc fn, intptr_t data) {
    RECT r = {0, 0, ff9_screen_w, ff9_screen_h};
    if (fn) fn(HMONITOR_MAIN, 0, &r, data);
    return 1;
}

/* MONITORINFOEX (TCHAR = char here): cbSize, rcMonitor, rcWork, dwFlags, szDevice[32] */
typedef struct { uint32_t cbSize; RECT monitor, work; uint32_t flags; char device[32]; } MONITORINFOEX;
EXPORT BOOL GetMonitorInfo(intptr_t mon, MONITORINFOEX *mi) {
    RECT r = {0, 0, ff9_screen_w, ff9_screen_h};
    mi->monitor = r;
    mi->work = r;
    mi->flags = 1;
    strcpy(mi->device, "\\\\.\\DISPLAY1");
    return 1;
}
EXPORT BOOL GetMonitorInfoA(intptr_t mon, MONITORINFOEX *mi) { return GetMonitorInfo(mon, mi); }

/* keyboard: no physical keyboard on the Thor; the gamepad goes through XInput */
EXPORT short GetAsyncKeyState(int key) { return 0; }
EXPORT short GetKeyState(int key) { return 0; }

/* ---- kernel32 (Memoria's speedrun auto-splitter pipe: not available) ---- */
EXPORT intptr_t CreateFile(const char *n, uint32_t a, uint32_t s, void *sa, uint32_t d, uint32_t f, intptr_t t) { return -1; }
EXPORT BOOL WriteFile(intptr_t h, void *b, uint32_t n, uint32_t *w, void *o) { if (w) *w = 0; return 0; }
EXPORT BOOL ReadFile(intptr_t h, void *b, uint32_t n, uint32_t *r, void *o) { if (r) *r = 0; return 0; }
EXPORT BOOL CloseHandle(intptr_t h) { return 1; }
EXPORT BOOL ConnectNamedPipe(intptr_t h, void *o) { return 0; }
EXPORT intptr_t CreateEvent(void *a, BOOL m, BOOL i, const char *n) { return 0; }
EXPORT intptr_t CreateNamedPipe(const char *n, uint32_t a, uint32_t b, uint32_t c, uint32_t d, uint32_t e, uint32_t f, void *g) { return -1; }
EXPORT BOOL CallNamedPipe(const char *n, void *a, uint32_t b, void *c, uint32_t d, void *e, uint32_t f) { return 0; }
EXPORT BOOL SetNamedPipeHandleState(const char *n, uint32_t *a, uint32_t *b, uint32_t *c) { return 0; }
EXPORT BOOL PeekNamedPipe(intptr_t h, void *b, uint32_t n, void *r, uint32_t *avail, void *left) { if (avail) *avail = 0; return 0; }
EXPORT void RtlFillMemory(void *dst, uint32_t len, uint8_t fill) { memset(dst, fill, len); }

/* ---- steamwrapper: no Steam on Android ---- */
EXPORT BOOL Init(void) { return 0; }
EXPORT void Shutdown_Internal(void) {}
EXPORT void ProcessCallbacks_Internal(void) {}
EXPORT BOOL TriggerAchievement_Internal(void *id) { return 0; }
EXPORT BOOL UpdateStatProgress_Internal(void *id, int p) { return 0; }
EXPORT BOOL ClearAchievement_Internal(void *id) { return 0; }
EXPORT BOOL IsCloudEnabled_Internal(void) { return 0; }
EXPORT void RegisterStatsReceivedCallback(void *cb) {}
EXPORT void RegisterStatsStoredCallback(void *cb) {}
EXPORT void RegisterAchievementStoredCallback(void *cb) {}
EXPORT BOOL RequestStats(void) { return 0; }
EXPORT void SetSendAchievementToCS(void *cb) {}
EXPORT BOOL SteamAPIRestartAppIfNecessary(int app) { return 0; }

/* ---- __Internal (iOS/console achievement banner) ---- */
EXPORT void _NotifyAchievement(int id, const char *sid, int progress, BOOL banner, void *cb) {}

/* ---- JoyShockLibrary: DualShock/Switch pads; the Thor's pad comes through XInput ---- */
EXPORT int JslConnectDevices(void) { return 0; }
EXPORT void JslDisconnectAndDisposeAll(void) {}
EXPORT BOOL JslStillConnected(int id) { return 0; }
EXPORT int JslGetConnectedDeviceHandles(int *a, int n) { return 0; }
typedef struct { int buttons; float lTrigger, rTrigger, stickLX, stickLY, stickRX, stickRY; } JSL_STATE;
EXPORT JSL_STATE JslGetSimpleState(int id) { JSL_STATE s = {0}; return s; }
EXPORT void JslSetRumble(int id, int s, int b) {}
