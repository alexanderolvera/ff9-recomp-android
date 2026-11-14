package com.ff9recomp;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Bundle;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import java.io.File;

/**
 * The entry point (the game runs in its own ":game" process, so the launcher survives a game
 * crash). Pages: the main menu (Play, Settings, Report a problem, Import game folder, Quit),
 * Settings (written into Memoria.ini when the game starts) and Report a problem. Everything works
 * with a controller: the D-pad moves, A confirms, B goes back, left/right change a setting.
 */
public class LauncherActivity extends Activity {
    private static final int PICK_GAME = 1;
    private static final int TEXT = Color.rgb(236, 232, 220), DIM = Color.rgb(170, 168, 160), INFO_BG = Color.argb(200, 10, 12, 20);

    private GameInstall install;
    private FrameLayout root;
    private LinearLayout window;
    private TextView info, status;
    private String page = "main";
    private boolean busy;
    private boolean withSaves;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        Reports.installCrashHandler(this);
        install = new GameInstall(this);
        root = new FrameLayout(this);
        root.setBackgroundColor(Color.rgb(8, 10, 18));
        setContentView(root);
        showMain();
        handleTestIntent(getIntent());
    }

    @Override
    protected void onNewIntent(Intent i) {
        super.onNewIntent(i);
        handleTestIntent(i);
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (page.equals("main")) showMain();
    }

    /** adb test API: am start ... --ez test_play true presses Play once the launcher is ready. */
    private void handleTestIntent(Intent i) {
        if (i != null && i.getBooleanExtra(TestControl.EXTRA_PLAY, false)) {
            i.removeExtra(TestControl.EXTRA_PLAY);
            root.post(this::play);
        }
    }

    // ------------------------------------------------------------------ layout helpers

    private int dp(float v) { return Math.round(TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v, getResources().getDisplayMetrics())); }

    /** A menu window in the spirit of FF9's: a dark slate panel with a light double border. */
    private LinearLayout newWindow(String title) { return newWindow(title, false); }

    private LinearLayout newWindow(String title, boolean tall) {
        root.removeAllViews();
        LinearLayout outer = new LinearLayout(this);
        outer.setOrientation(LinearLayout.VERTICAL);
        GradientDrawable bg = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
                new int[] { Color.rgb(74, 78, 96), Color.rgb(38, 40, 54) });
        bg.setCornerRadius(dp(6));
        bg.setStroke(dp(2), Color.rgb(214, 210, 196));
        outer.setBackground(bg);
        outer.setPadding(dp(22), dp(14), dp(22), dp(14));
        TextView t = new TextView(this);
        t.setText(title);
        t.setTextColor(TEXT);
        t.setTextSize(24);
        t.setTypeface(Typeface.SERIF, Typeface.BOLD);
        t.setPadding(0, 0, 0, dp(8));
        outer.addView(t);
        LinearLayout items = new LinearLayout(this);
        items.setOrientation(LinearLayout.VERTICAL);
        ScrollView sv = new ScrollView(this);
        sv.addView(items);
        outer.addView(sv, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1));
        status = new TextView(this);
        status.setTextColor(DIM);
        status.setTextSize(14);
        status.setPadding(0, dp(8), 0, 0);
        outer.addView(status);
        // a long page (Settings) takes the screen's height and scrolls; a short one sits centred
        FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(dp(560), tall ? ViewGroup.LayoutParams.MATCH_PARENT : ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.CENTER);
        lp.topMargin = dp(16); lp.bottomMargin = dp(56);
        root.addView(outer, lp);
        info = new TextView(this);
        info.setTextColor(TEXT);
        info.setTextSize(15);
        info.setBackgroundColor(INFO_BG);
        info.setPadding(dp(16), dp(10), dp(16), dp(10));
        root.addView(info, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM));
        window = items;
        return items;
    }

    /** A menu line with the hand cursor; `help` goes to the info bar while it has focus. */
    private TextView item(String text, String help, Runnable onA) {
        TextView v = new TextView(this);
        v.setText(text);
        v.setTextColor(TEXT);
        v.setTextSize(20);
        v.setPadding(dp(4), dp(7), dp(4), dp(7));
        v.setFocusable(true);
        v.setFocusableInTouchMode(true);
        HandCursor hc = new HandCursor(getResources().getDisplayMetrics().density);
        v.setCompoundDrawablesRelativeWithIntrinsicBounds(hc, null, null, null);
        v.setCompoundDrawablePadding(dp(10));
        v.setOnFocusChangeListener((x, f) -> { if (f && info != null) info.setText(help == null ? "" : help); });
        v.setOnClickListener(x -> { if (!busy && onA != null) onA.run(); });
        window.addView(v);
        return v;
    }

    // ------------------------------------------------------------------ main menu

    private void showMain() {
        page = "main";
        newWindow("FF9Recomp");
        boolean has = install.hasGame();
        String missing = has ? install.missingMessage() : null;
        boolean ready = has && missing == null;
        TextView play = item("Play", ready ? "Start FINAL FANTASY IX." : "Import your game folder first.", this::play);
        play.setAlpha(ready ? 1f : 0.45f);
        item("Settings", "Frame rate, widescreen, boosters, controls, volume.", this::showSettings);
        item("Report a problem", "Make one zip with the logs and send it.", this::showReport);
        TextView imp = item("Import game folder", "Pick your FINAL FANTASY IX folder (Steam, with Memoria; Moguri Mod if you use it). It is copied into the app once.",
                () -> startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE), PICK_GAME));
        item("Quit", "Close the launcher, and the game if it is running.", this::quit);
        if (!has) status.setText("No game imported yet.");
        else if (missing != null) status.setText(missing);
        else status.setText("Ready." + (install.hasMoguri() ? " Moguri Mod found." : "") + " " + Reports.version(this));
        String exit = Reports.lastGameExit(this);
        if (exit != null && exit.startsWith("crashed")) status.append("\nThe game last " + exit + ". Report a problem sends the details.");
        (ready ? play : imp).requestFocus();
    }

    private void play() {
        if (busy || !install.hasGame() || install.missingMessage() != null) return;
        try {
            Settings.apply(this, install.game);
        } catch (Exception e) {
            status.setText("Could not write the settings into Memoria.ini: " + e.getMessage());
            return;
        }
        startActivity(new Intent(this, GameActivity.class));
    }

    private void quit() {
        int pid = TestControl.gamePid(this);
        if (pid > 0) android.os.Process.killProcess(pid);
        finishAndRemoveTask();
    }

    // ------------------------------------------------------------------ settings

    private void showSettings() {
        page = "settings";
        newWindow("Settings", true);
        TextView first = null;
        for (Settings.Option o : Settings.OPTIONS) {
            TextView v = item(o.label + ":  " + Settings.valueText(this, o), o.note, null);
            v.setTag(o);
            v.setOnClickListener(x -> change(v, 1));
            if (first == null) first = v;
        }
        status.setText("Left/right or A to change. B to go back. Applied when the game starts.");
        if (first != null) first.requestFocus();
    }

    private void change(TextView v, int dir) {
        Settings.Option o = (Settings.Option) v.getTag();
        int n = o.isBool() ? 2 : o.choices.length;
        int val = (Settings.get(this, o) + dir + n) % n;
        Settings.set(this, o, val);
        v.setText(o.label + ":  " + Settings.valueText(this, o));
    }

    // ------------------------------------------------------------------ report a problem

    private void showReport() {
        page = "report";
        newWindow("Report a problem");
        item("Make the report", "One zip with the logs, the crash record and the settings. It goes to Downloads; then share it.", this::makeReport);
        TextView s = item(savesLabel(), "Add your save files to the zip (useful when a save does not load).", null);
        s.setOnClickListener(x -> { withSaves = !withSaves; s.setText(savesLabel()); });
        item("Back", null, this::showMain).setTag(null);
        status.setText("Nothing is sent by the app: you choose where the zip goes.");
        window.getChildAt(0).requestFocus();
    }

    private String savesLabel() { return "Include saves:  " + (withSaves ? "Yes" : "No"); }

    private void makeReport() {
        busy = true;
        status.setText("Making the report...");
        new Thread(() -> {
            String msg;
            Uri shared = null;
            try {
                File zip = Reports.build(this, withSaves);
                shared = Reports.toDownloads(this, zip);
                msg = "Saved to Downloads: " + zip.getName();
            } catch (Exception e) {
                msg = "The report failed: " + e.getMessage();
            }
            Uri u = shared;
            String m = msg;
            runOnUiThread(() -> {
                busy = false;
                status.setText(m);
                if (u != null) {
                    Intent send = new Intent(Intent.ACTION_SEND).setType("application/zip").putExtra(Intent.EXTRA_STREAM, u)
                            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                    try { startActivity(Intent.createChooser(send, "Send the report")); } catch (Exception ignored) { }
                }
            });
        }).start();
    }

    // ------------------------------------------------------------------ import

    @Override
    protected void onActivityResult(int req, int res, Intent data) {
        super.onActivityResult(req, res, data);
        if (req != PICK_GAME || res != RESULT_OK || data == null) return;
        Uri tree = data.getData();
        busy = true;
        new Thread(() -> {
            String err = null;
            try {
                install.importFrom(tree, what -> runOnUiThread(() -> { if (status != null) status.setText(what); }));
            } catch (Exception e) {
                err = e.getMessage();
            }
            String e2 = err;
            runOnUiThread(() -> {
                busy = false;
                showMain();
                if (e2 != null) status.setText("Import failed: " + e2);
            });
        }).start();
    }

    // ------------------------------------------------------------------ keys

    @Override
    public boolean dispatchKeyEvent(KeyEvent e) {
        int code = e.getKeyCode();
        View f = getCurrentFocus();
        boolean down = e.getAction() == KeyEvent.ACTION_DOWN;
        if (code == KeyEvent.KEYCODE_BUTTON_A || code == KeyEvent.KEYCODE_ENTER || code == KeyEvent.KEYCODE_DPAD_CENTER) {
            if (down && e.getRepeatCount() == 0 && f != null) f.performClick();
            return true;
        }
        if (code == KeyEvent.KEYCODE_BUTTON_B || code == KeyEvent.KEYCODE_BACK || code == KeyEvent.KEYCODE_ESCAPE) {
            if (!down) return true;
            if (!page.equals("main")) showMain();
            else if (code == KeyEvent.KEYCODE_BACK) moveTaskToBack(true);
            return true;
        }
        if (page.equals("settings") && f instanceof TextView && f.getTag() instanceof Settings.Option
                && (code == KeyEvent.KEYCODE_DPAD_LEFT || code == KeyEvent.KEYCODE_DPAD_RIGHT)) {
            if (down) change((TextView) f, code == KeyEvent.KEYCODE_DPAD_RIGHT ? 1 : -1);
            return true;
        }
        return super.dispatchKeyEvent(e);
    }
}
