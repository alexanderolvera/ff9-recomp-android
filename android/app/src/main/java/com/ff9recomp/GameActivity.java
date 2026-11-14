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
        Reports.installCrashHandler(this);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        root = new android.widget.FrameLayout(this);
        SurfaceView v = new SurfaceView(this);
        v.getHolder().addCallback(this);
        root.addView(v);
        setContentView(root);
        hideSystemUi();
        android.content.IntentFilter padFilter = new android.content.IntentFilter("com.ff9recomp.test.PAD");
        if (android.os.Build.VERSION.SDK_INT >= 33)
            registerReceiver(testPad, padFilter, android.Manifest.permission.DUMP, null, RECEIVER_EXPORTED);
        else
            registerReceiver(testPad, padFilter, android.Manifest.permission.DUMP, null);
    }

    @Override
    protected void onDestroy() {
        try { unregisterReceiver(testPad); } catch (Exception ignored) { }
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

    private void send() {
        int all = buttons | dpad | testButtons;
        // Select+Start together is ours (leave the game): the game sees neither while both are held
        int toGame = (all & (START | BACK)) == (START | BACK) ? all & ~(START | BACK) : all;
        nativePad(toGame, lt, rt, lx, ly, rx, ry);
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
