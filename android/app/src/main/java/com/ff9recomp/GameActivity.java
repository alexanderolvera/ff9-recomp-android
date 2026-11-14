package com.ff9recomp;

import android.app.Activity;
import android.os.Bundle;
import android.system.Os;
import android.util.Log;
import android.view.InputDevice;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.SurfaceHolder;
import android.view.SurfaceView;
import android.view.View;
import android.view.WindowManager;
import java.io.File;

/**
 * The game: a SurfaceView for the EGL window, the engine on its own thread (libff9.so), and the
 * controller as an XInput-style pad (the game reads XInput).
 */
public class GameActivity extends Activity implements SurfaceHolder.Callback {
    private static final String TAG = "FF9Recomp";
    private static boolean started;

    static native void nativeSetSurface(Object surface);
    static native int nativeRun(String dir, String log);
    static native String nativeCompanion();
    static native int nativeRumble();
    static native int nativeFrames();
    static native void nativePad(int buttons, int lt, int rt, int lx, int ly, int rx, int ry);

    // XInput button bits
    static final int UP = 0x1, DOWN = 0x2, LEFT = 0x4, RIGHT = 0x8, START = 0x10, BACK = 0x20, LTHUMB = 0x40, RTHUMB = 0x80,
            LB = 0x100, RB = 0x200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;
    private int buttons, lt, rt, lx, ly, rx, ry, dpad;

    private final android.os.Handler ui = new android.os.Handler(android.os.Looper.getMainLooper());
    private android.widget.FrameLayout root;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        current = this;
        Reports.installCrashHandler(this);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        root = new android.widget.FrameLayout(this);
        SurfaceView v = new SurfaceView(this);
        v.getHolder().addCallback(this);
        root.addView(v);
        // the on-screen pad (Settings > Touch controls: Auto shows it when no controller is connected)
        android.content.SharedPreferences prefs = getSharedPreferences("launcher", MODE_PRIVATE);
        touchMode = prefs.getInt("touch_controls", 0);
        if (touchMode != 2) {
            touch = new TouchControls(this, (tb, tl, tr) -> { touchButtons = tb; touchLt = tl; touchRt = tr; send(); });
            touch.setLayout(prefs.getString("touch_layout", ""));
            root.addView(touch, new android.widget.FrameLayout.LayoutParams(-1, -1));
            ((android.hardware.input.InputManager) getSystemService(INPUT_SERVICE)).registerInputDeviceListener(deviceListener, null);
        }
        if (prefs.getBoolean("crt_filter", false)) root.addView(new CrtOverlay(this), new android.widget.FrameLayout.LayoutParams(-1, -1));
        if (prefs.getBoolean("show_fps", false)) {
            fpsView = new android.widget.TextView(this);
            fpsView.setTextColor(android.graphics.Color.YELLOW);
            fpsView.setTextSize(14);
            fpsView.setShadowLayer(3, 1, 1, android.graphics.Color.BLACK);
            fpsView.setPadding(16, 8, 16, 8);
            root.addView(fpsView, new android.widget.FrameLayout.LayoutParams(-2, -2, android.view.Gravity.TOP | android.view.Gravity.END));
        }
        rumble = prefs.getInt("rumble", 1);
        crt = prefs.getBoolean("crt_filter", false);
        setContentView(root);
        // Smooth 120: ask the display for its fastest mode at this resolution (the Thor's 120 Hz)
        if (prefs.getInt("fps", 0) == 2) preferHighestRefreshRate();
        if (prefs.getBoolean("bottom_screen", true)) showCompanion();
        ui.post(poller);
        updateTouch();
        hideSystemUi();
        android.content.IntentFilter padFilter = new android.content.IntentFilter("com.ff9recomp.test.PAD");
        if (android.os.Build.VERSION.SDK_INT >= 33)
            registerReceiver(testPad, padFilter, android.Manifest.permission.DUMP, null, RECEIVER_EXPORTED);
        else
            registerReceiver(testPad, padFilter, android.Manifest.permission.DUMP, null);
    }

    // ---- the second screen (AYN Thor's bottom screen): the panels, in a Presentation that never
    // takes focus; keys and sticks that reach it go to the game
    private android.app.Presentation companion;
    private static volatile boolean companionOn;
    static volatile GameActivity current;
    Activity companionHost;      // the panels on the main display when the screens are swapped

    /** The game's latest panel state (JSON) for CompanionView, or null. */
    static String companionState() {
        try { return nativeCompanion(); } catch (UnsatisfiedLinkError e) { return null; }
    }

    private void showCompanion() {
        android.hardware.display.DisplayManager dm = (android.hardware.display.DisplayManager) getSystemService(DISPLAY_SERVICE);
        android.view.Display[] ds = dm.getDisplays(android.hardware.display.DisplayManager.DISPLAY_CATEGORY_PRESENTATION);
        if (ds.length == 0) { Log.i(TAG, "no second display"); return; }
        android.view.Display d = ds[0];
        android.view.Display mine = getDisplay();
        if (mine != null && mine.getDisplayId() != android.view.Display.DEFAULT_DISPLAY) {
            // swapped: the game is on the second display, the panels go to the main one
            Log.i(TAG, "screens swapped: panels on the main display");
            try {
                android.app.ActivityOptions o = android.app.ActivityOptions.makeBasic();
                o.setLaunchDisplayId(android.view.Display.DEFAULT_DISPLAY);
                startActivity(new android.content.Intent(this, CompanionHostActivity.class)
                        .addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK | android.content.Intent.FLAG_ACTIVITY_NO_ANIMATION), o.toBundle());
                companionOn = true;
                try { Os.setenv("FF9_COMPANION", "1", true); } catch (Exception ignored) { }
            } catch (RuntimeException e) { Log.w(TAG, "panels not shown on the main display: " + e); }
            return;
        }
        Log.i(TAG, "panels on display " + d.getDisplayId() + " \"" + d.getName() + "\"");
        companion = new android.app.Presentation(this, d) {
            @Override public boolean dispatchKeyEvent(KeyEvent e) { return GameActivity.this.dispatchKeyEvent(e); }
            @Override public boolean dispatchGenericMotionEvent(MotionEvent e) { return GameActivity.this.dispatchGenericMotionEvent(e); }
        };
        companion.getWindow().addFlags(WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        android.widget.FrameLayout panels = new android.widget.FrameLayout(companion.getContext());
        panels.addView(new CompanionView(companion.getContext()));
        // the guide (Settings > Guide page): a web page the player chose, on this screen, swapped
        // with the panels by Select+L1; scrolled by touch
        String guideUrl = getSharedPreferences("launcher", MODE_PRIVATE).getString("guide_url", "");
        if (!guideUrl.isEmpty()) {
            guide = new android.webkit.WebView(companion.getContext());
            guide.getSettings().setJavaScriptEnabled(true);
            guide.getSettings().setDomStorageEnabled(true);
            guide.setWebViewClient(new android.webkit.WebViewClient());
            guide.setFocusable(false);
            guide.setVisibility(View.GONE);
            panels.addView(guide, new android.widget.FrameLayout.LayoutParams(-1, -1));
            guidePending = guideUrl;
        }
        if (crt) panels.addView(new CrtOverlay(companion.getContext()), new android.widget.FrameLayout.LayoutParams(-1, -1));
        companion.setContentView(panels);
        try {
            companion.show();
            companionOn = true;
            try { Os.setenv("FF9_COMPANION", "1", true); } catch (Exception ignored) { }
        } catch (RuntimeException e) {
            Log.w(TAG, "panels not shown: " + e);
            companion = null;
        }
    }

    // ---- FPS counter and rumble (polled from the host every 50 ms)
    private android.widget.TextView fpsView;
    private int rumble, lastFrames = -1, lastRumble;
    private boolean crt;
    private long lastFpsAt, lastRumbleAt;
    private final Runnable poller = new Runnable() {
        @Override public void run() {
            try {
                long now = android.os.SystemClock.uptimeMillis();
                if (fpsView != null && now - lastFpsAt >= 500) {
                    int f = nativeFrames();
                    if (lastFrames >= 0) fpsView.setText(Math.round((f - lastFrames) * 1000.0 / (now - lastFpsAt)) + " FPS");
                    lastFrames = f;
                    lastFpsAt = now;
                }
                if (rumble != 0) {
                    int r = nativeRumble();
                    // a level held by the game is renewed in slices; zero stops it
                    if (r != lastRumble || (r != 0 && now - lastRumbleAt >= 100)) { lastRumble = r; lastRumbleAt = now; vibrate(Math.max(r >> 8, r & 0xFF)); }
                }
            } catch (UnsatisfiedLinkError ignored) { }
            ui.postDelayed(this, 50);
        }
    };

    /** The game's rumble on the controller's motor when it has one, else the device's. */
    private void vibrate(int strength) {
        android.os.Vibrator v = null;
        for (int id : InputDevice.getDeviceIds()) {
            InputDevice d = InputDevice.getDevice(id);
            if (isGamepad(d) && d.getVibratorManager().getVibratorIds().length > 0) { v = d.getVibratorManager().getDefaultVibrator(); break; }
        }
        if (v == null) v = ((android.os.VibratorManager) getSystemService(VIBRATOR_MANAGER_SERVICE)).getDefaultVibrator();
        if (v == null || !v.hasVibrator()) return;
        if (strength == 0) { v.cancel(); return; }
        int amp = Math.max(1, Math.min(255, rumble == 2 ? strength : strength * 2 / 3));   // Light: two thirds
        // the game sets a motor level and later zero: vibrate in short slices while it is on
        v.vibrate(android.os.VibrationEffect.createOneShot(120, v.hasAmplitudeControl() ? amp : android.os.VibrationEffect.DEFAULT_AMPLITUDE));
    }

    private void preferHighestRefreshRate() {
        android.view.Display d = getDisplay();
        if (d == null) return;
        android.view.Display.Mode cur = d.getMode(), best = cur;
        for (android.view.Display.Mode m : d.getSupportedModes())
            if (m.getPhysicalWidth() == cur.getPhysicalWidth() && m.getPhysicalHeight() == cur.getPhysicalHeight() && m.getRefreshRate() > best.getRefreshRate()) best = m;
        WindowManager.LayoutParams lp = getWindow().getAttributes();
        lp.preferredDisplayModeId = best.getModeId();
        getWindow().setAttributes(lp);
        Log.i(TAG, "display mode " + best.getRefreshRate() + " Hz");
    }

    // ---- touch controls and controller detection
    private TouchControls touch;
    private int touchMode, touchButtons, touchLt, touchRt;

    private static boolean isGamepad(InputDevice d) {
        if (d == null || d.isVirtual()) return false;
        int src = d.getSources();
        return (src & InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD || (src & InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK;
    }

    private static boolean gamepadConnected() {
        for (int id : InputDevice.getDeviceIds()) if (isGamepad(InputDevice.getDevice(id))) return true;
        return false;
    }

    private void updateTouch() {
        if (touch == null) return;
        boolean show = touchMode == 1 || (touchMode == 0 && companion == null && companionHost == null && !gamepadConnected());
        touch.setVisibility(show ? View.VISIBLE : View.GONE);
        if (!show && (touchButtons | touchLt | touchRt) != 0) { touchButtons = touchLt = touchRt = 0; send(); }
    }

    private final android.hardware.input.InputManager.InputDeviceListener deviceListener =
            new android.hardware.input.InputManager.InputDeviceListener() {
        @Override public void onInputDeviceAdded(int id) { updateTouch(); }
        @Override public void onInputDeviceRemoved(int id) { updateTouch(); }
        @Override public void onInputDeviceChanged(int id) { updateTouch(); }
    };

    @Override
    protected void onDestroy() {
        try { unregisterReceiver(testPad); } catch (Exception ignored) { }
        if (companion != null) { companion.dismiss(); companion = null; }
        if (companionHost != null) { companionHost.finish(); companionHost = null; }
        if (current == this) current = null;
        super.onDestroy();
    }

    /** Setup failed before the game could start: say what happened instead of a black screen. */
    private void showError(String title, String detail) {
        runOnUiThread(() -> {
            android.widget.TextView t = new android.widget.TextView(this);
            t.setText(title + "\n\n" + detail + "\n\nPress B to go back. Report a problem in the launcher sends the details.");
            t.setTextColor(android.graphics.Color.WHITE);
            t.setTextSize(18);
            t.setPadding(60, 60, 60, 60);
            t.setBackgroundColor(android.graphics.Color.rgb(12, 14, 24));
            root.addView(t);
            failed = true;
        });
    }
    private boolean failed;

    private void hideSystemUi() {
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY | View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
    }

    @Override
    public void surfaceCreated(SurfaceHolder h) {
        System.loadLibrary("ff9");
        nativeSetSurface(h.getSurface());
        if (started) return;
        started = true;
        GameInstall gi = new GameInstall(this);
        new Thread(() -> {
            try {
                gi.extractRuntime();
                String nat = getApplicationInfo().nativeLibraryDir;
                Os.setenv("FF9_BCL", new File(gi.runtime, "bcl").getPath(), true);
                Os.setenv("FF9_ENGINE", new File(gi.runtime, "engine").getPath(), true);
                Os.setenv("FF9_NATIVE", nat, true);
                Os.setenv("FF9_MANAGED", new File(gi.game, "x64/FF9_Data/Managed").getPath(), true);
                Os.setenv("FF9_GAME", gi.game.getPath(), true);
                Os.setenv("LD_LIBRARY_PATH", nat, true);
                Os.setenv("FF9_COMPANION", "1", true);
                Os.setenv("FF9_UI_SOUNDS", Sfx.dir(this).getPath(), true);   // the launcher's menu sounds (Core/UiSounds.cs)
                Os.setenv("FF9_ACHIEVEMENTS", Achievements.file(this).getPath(), true);   // src/host/win32.c     // the game's state for the panels and save-state names
                // resume a save state, or Settings > Start: Continue (Memoria loads its autosave at once)
                boolean autoload = getIntent().getBooleanExtra("autoload", false)
                        || getSharedPreferences("launcher", MODE_PRIVATE).getInt("start", 0) == 1;
                if (autoload && SaveStates.autosave(this).isFile()) Os.setenv("FF9_ARGS", "-memoria-autoload", true);
                String testArgs = getIntent().getStringExtra("test_args");
                if (testArgs != null) Os.setenv("FF9_ARGS", testArgs.trim().replace(' ', '+'), true);
            } catch (Exception e) {
                Log.e(TAG, "runtime setup failed", e);
                Reports.writeText(new File(gi.files, "setup-error.txt"), Log.getStackTraceString(e));
                showError("The game could not start", "Setting up the runtime failed: " + e.getMessage());
                return;
            }
            File log = new File(gi.files, "ff9.log");
            if (log.isFile()) log.renameTo(new File(gi.files, "ff9.prev.log"));   // the run before, for reports
            int r = nativeRun(gi.files.getPath(), log.getPath());
            Log.i(TAG, "game exited " + r);
            runOnUiThread(this::finish);
            android.os.Process.killProcess(android.os.Process.myPid());
        }, "FF9").start();
    }

    @Override public void surfaceChanged(SurfaceHolder h, int f, int w, int hh) { nativeSetSurface(h.getSurface()); }
    /** The window goes (sleep, background): the engine drops its surface and the game pauses
     *  until a new one comes (src/host/android_jni.c, Gfx/Device.cs). */
    @Override public void surfaceDestroyed(SurfaceHolder h) { nativeSetSurface(null); }

    @Override
    public void onWindowFocusChanged(boolean focus) {
        super.onWindowFocusChanged(focus);
        if (focus) hideSystemUi();
    }

    private static int bit(int code) {
        switch (code) {
            case KeyEvent.KEYCODE_BUTTON_A: return A;
            case KeyEvent.KEYCODE_BUTTON_B: return B;
            case KeyEvent.KEYCODE_BUTTON_X: return X;
            case KeyEvent.KEYCODE_BUTTON_Y: return Y;
            case KeyEvent.KEYCODE_BUTTON_L1: return LB;
            case KeyEvent.KEYCODE_BUTTON_R1: return RB;
            case KeyEvent.KEYCODE_BUTTON_START: return START;
            case KeyEvent.KEYCODE_BUTTON_SELECT: case KeyEvent.KEYCODE_BACK: return BACK;
            case KeyEvent.KEYCODE_BUTTON_THUMBL: return LTHUMB;
            case KeyEvent.KEYCODE_BUTTON_THUMBR: return RTHUMB;
            case KeyEvent.KEYCODE_DPAD_UP: return UP;
            case KeyEvent.KEYCODE_DPAD_DOWN: return DOWN;
            case KeyEvent.KEYCODE_DPAD_LEFT: return LEFT;
            case KeyEvent.KEYCODE_DPAD_RIGHT: return RIGHT;
        }
        return 0;
    }

    // ---- a physical keyboard: arrows/WASD move, Enter/Space confirm, Esc/Backspace cancel,
    // Tab = menu, Q/E = L1/R1, Shift = the fourth button, Backslash = Select, P = Start
    private int keyButtons;

    private static int keyboardBit(int code) {
        switch (code) {
            case KeyEvent.KEYCODE_DPAD_UP: case KeyEvent.KEYCODE_W: return UP;
            case KeyEvent.KEYCODE_DPAD_DOWN: case KeyEvent.KEYCODE_S: return DOWN;
            case KeyEvent.KEYCODE_DPAD_LEFT: case KeyEvent.KEYCODE_A: return LEFT;
            case KeyEvent.KEYCODE_DPAD_RIGHT: case KeyEvent.KEYCODE_D: return RIGHT;
            case KeyEvent.KEYCODE_ENTER: case KeyEvent.KEYCODE_SPACE: case KeyEvent.KEYCODE_NUMPAD_ENTER: return A;
            case KeyEvent.KEYCODE_ESCAPE: case KeyEvent.KEYCODE_DEL: return B;
            case KeyEvent.KEYCODE_TAB: return Y;
            case KeyEvent.KEYCODE_SHIFT_LEFT: case KeyEvent.KEYCODE_SHIFT_RIGHT: return X;
            case KeyEvent.KEYCODE_Q: return LB;
            case KeyEvent.KEYCODE_E: return RB;
            case KeyEvent.KEYCODE_BACKSLASH: return BACK;
            case KeyEvent.KEYCODE_P: return START;
        }
        return 0;
    }

    private void send() {
        int all = buttons | dpad | testButtons | keyButtons | touchButtons;
        // Select+Start together is ours (leave the game): the game sees neither while both are held
        int toGame = (all & (START | BACK)) == (START | BACK) ? all & ~(START | BACK) : all;
        nativePad(toGame, Math.max(lt, touchLt), Math.max(rt, touchRt), lx, ly, rx, ry);
        // Select+L1: the guide or the panels on the second screen
        boolean guideCombo = (all & (BACK | LB)) == (BACK | LB);
        if (guideCombo && !guideHeld) toggleGuide();
        guideHeld = guideCombo;
        // Select+R1: keep the current place in a save-state slot
        boolean keepCombo = (all & (BACK | RB)) == (BACK | RB);
        if (keepCombo && !keepHeld) keepState();
        keepHeld = keepCombo;
        // Select+Start held for a second: the leave-the-game dialog
        boolean combo = (all & (START | BACK)) == (START | BACK);
        if (combo != quitHeld) {
            quitHeld = combo;
            if (combo) ui.postDelayed(askQuit, 1000);
            else ui.removeCallbacks(askQuit);
        }
    }

    // ---- leaving the game: Select+Start held, then Leave. Ends the game's process (":game");
    // the launcher (its own process) is back. Unsaved progress is lost, as the dialog says.
    private boolean keepHeld, guideHeld;
    private android.webkit.WebView guide;
    private String guidePending;

    private void toggleGuide() {
        if (guide == null) return;
        if (guidePending != null) { guide.loadUrl(guidePending); guidePending = null; }
        guide.setVisibility(guide.getVisibility() == View.VISIBLE ? View.GONE : View.VISIBLE);
    }

    private void keepState() {
        String place = "";
        try { place = CompanionView.clean(new org.json.JSONObject(String.valueOf(companionState())).optString("loc")); } catch (Exception ignored) { }
        String p = place;
        new Thread(() -> {
            String msg;
            try { msg = "Save state " + SaveStates.keep(this, p) + ": " + (p.isEmpty() ? "kept" : p); }
            catch (Exception e) { msg = "No save state: " + e.getMessage(); }
            String m = msg;
            runOnUiThread(() -> android.widget.Toast.makeText(this, m, android.widget.Toast.LENGTH_SHORT).show());
        }).start();
    }

    private final Runnable askQuit = this::askQuit;
    private boolean quitHeld;
    private android.app.AlertDialog quitDialog;

    private void askQuit() {
        quitHeld = false;
        if (isFinishing() || (quitDialog != null && quitDialog.isShowing())) return;
        buttons = dpad = testButtons = 0;
        lt = rt = 0;
        nativePad(0, 0, 0, lx, ly, rx, ry);
        quitDialog = new android.app.AlertDialog.Builder(this, android.R.style.Theme_DeviceDefault_Dialog_Alert)
                .setTitle("Leave the game?")
                .setMessage("You go back to the launcher. Anything since your last save is lost.")
                .setPositiveButton("Leave", (d, w) -> leaveGame())
                .setNegativeButton("Keep playing", null)
                .show();
        android.widget.Button keep = quitDialog.getButton(android.app.AlertDialog.BUTTON_NEGATIVE);
        android.widget.Button leave = quitDialog.getButton(android.app.AlertDialog.BUTTON_POSITIVE);
        // a pad drives it: left/right pick, A presses, B keeps playing; the picked one is underlined
        Runnable mark = () -> {
            for (android.widget.Button bt : new android.widget.Button[] { keep, leave })
                if (bt != null) bt.setPaintFlags(bt.isFocused() ? bt.getPaintFlags() | android.graphics.Paint.UNDERLINE_TEXT_FLAG
                                                                : bt.getPaintFlags() & ~android.graphics.Paint.UNDERLINE_TEXT_FLAG);
        };
        if (keep != null) { keep.setFocusableInTouchMode(true); keep.requestFocus(); }
        if (leave != null) leave.setFocusableInTouchMode(true);
        mark.run();
        quitDialog.setOnKeyListener((d, code, ev) -> {
            if (ev.getAction() != KeyEvent.ACTION_DOWN) return code == KeyEvent.KEYCODE_BUTTON_A || code == KeyEvent.KEYCODE_BUTTON_B;
            switch (code) {
                case KeyEvent.KEYCODE_DPAD_LEFT: if (keep != null) keep.requestFocus(); mark.run(); return true;
                case KeyEvent.KEYCODE_DPAD_RIGHT: if (leave != null) leave.requestFocus(); mark.run(); return true;
                case KeyEvent.KEYCODE_BUTTON_A: case KeyEvent.KEYCODE_ENTER: case KeyEvent.KEYCODE_DPAD_CENTER:
                    if (leave != null && leave.isFocused()) leave.performClick(); else d.dismiss();
                    return true;
                case KeyEvent.KEYCODE_BUTTON_B: case KeyEvent.KEYCODE_BACK: d.dismiss(); return true;
            }
            return false;
        });
    }

    private void leaveGame() {
        Log.i(TAG, "leaving the game (Select+Start)");
        startActivity(new android.content.Intent(this, LauncherActivity.class).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK));
        finish();
        ui.postDelayed(() -> android.os.Process.killProcess(android.os.Process.myPid()), 300);
    }

    // ---- test API: adb holds pad buttons for a set time (`input keyevent` is too short for the
    // game to see reliably). Only the shell can send it (DUMP):
    //   am broadcast -a com.ff9recomp.test.PAD -p com.ff9recomp --ei buttons 0x1000 --ei ms 100
    private int testButtons;
    private final Runnable testRelease = () -> { testButtons = 0; send(); };
    private final android.content.BroadcastReceiver testPad = new android.content.BroadcastReceiver() {
        @Override public void onReceive(android.content.Context c, android.content.Intent i) {
            ui.removeCallbacks(testRelease);
            testButtons = i.getIntExtra("buttons", 0);
            send();
            ui.postDelayed(testRelease, Math.max(16, Math.min(10000, i.getIntExtra("ms", 100))));
        }
    };

    @Override
    public boolean dispatchKeyEvent(KeyEvent e) {
        if (failed) {
            if (e.getAction() == KeyEvent.ACTION_UP && (e.getKeyCode() == KeyEvent.KEYCODE_BUTTON_B || e.getKeyCode() == KeyEvent.KEYCODE_BACK)) finish();
            return true;
        }
        InputDevice dev = e.getDevice();
        if (dev != null && !dev.isVirtual() && !isGamepad(dev) && dev.getKeyboardType() == InputDevice.KEYBOARD_TYPE_ALPHABETIC) {
            int kb = keyboardBit(e.getKeyCode());
            if (kb == 0) return super.dispatchKeyEvent(e);
            if (e.getAction() == KeyEvent.ACTION_DOWN) keyButtons |= kb; else if (e.getAction() == KeyEvent.ACTION_UP) keyButtons &= ~kb;
            send();
            return true;
        }
        int b = bit(e.getKeyCode());
        if (b == 0) return super.dispatchKeyEvent(e);
        if (e.getKeyCode() == KeyEvent.KEYCODE_BUTTON_L2 || e.getKeyCode() == KeyEvent.KEYCODE_BUTTON_R2) return true;
        if (e.getAction() == KeyEvent.ACTION_DOWN) buttons |= b; else if (e.getAction() == KeyEvent.ACTION_UP) buttons &= ~b;
        send();
        return true;
    }

    private static int axis(float v) { return Math.max(-32767, Math.min(32767, Math.round(v * 32767))); }

    @Override
    public boolean onGenericMotionEvent(MotionEvent e) {
        if ((e.getSource() & InputDevice.SOURCE_JOYSTICK) == 0) return super.onGenericMotionEvent(e);
        lx = axis(e.getAxisValue(MotionEvent.AXIS_X));
        ly = -axis(e.getAxisValue(MotionEvent.AXIS_Y));       // XInput: up is positive
        rx = axis(e.getAxisValue(MotionEvent.AXIS_Z));
        ry = -axis(e.getAxisValue(MotionEvent.AXIS_RZ));
        lt = Math.round(Math.max(e.getAxisValue(MotionEvent.AXIS_LTRIGGER), e.getAxisValue(MotionEvent.AXIS_BRAKE)) * 255);
        rt = Math.round(Math.max(e.getAxisValue(MotionEvent.AXIS_RTRIGGER), e.getAxisValue(MotionEvent.AXIS_GAS)) * 255);
        float hx = e.getAxisValue(MotionEvent.AXIS_HAT_X), hy = e.getAxisValue(MotionEvent.AXIS_HAT_Y);
        dpad = (hx < -0.5f ? LEFT : 0) | (hx > 0.5f ? RIGHT : 0) | (hy < -0.5f ? UP : 0) | (hy > 0.5f ? DOWN : 0);
        send();
        return true;
    }
}
