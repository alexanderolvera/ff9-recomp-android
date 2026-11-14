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

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        SurfaceView v = new SurfaceView(this);
        v.getHolder().addCallback(this);
        setContentView(v);
        hideSystemUi();
    }

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
                return;
            }
            int r = nativeRun(gi.files.getPath(), new File(gi.files, "ff9.log").getPath());
            Log.i(TAG, "game exited " + r);
            runOnUiThread(this::finish);
            android.os.Process.killProcess(android.os.Process.myPid());
        }, "FF9").start();
    }

    @Override public void surfaceChanged(SurfaceHolder h, int f, int w, int hh) { nativeSetSurface(h.getSurface()); }
    @Override public void surfaceDestroyed(SurfaceHolder h) { }

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

    private void send() { nativePad(buttons | dpad, lt, rt, lx, ly, rx, ry); }

    @Override
    public boolean dispatchKeyEvent(KeyEvent e) {
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
