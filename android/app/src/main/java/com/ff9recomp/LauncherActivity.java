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
        item("Save states", "Resume a place you kept in game with Select+R1.", this::showStates);
        item("Achievements", "What you have unlocked in this copy.", this::showAchievements);
        item("Settings", "Frame rate, widescreen, boosters, controls, volume.", this::showSettings);
        item("Report a problem", "Make one zip with the logs and send it.", this::showReport);
        TextView imp = item("Import game folder", "Pick your FINAL FANTASY IX folder (Steam, with Memoria; Moguri Mod if you use it). It is copied into the app once.",
                () -> startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE), PICK_GAME));
        Updater.Release up = Updater.available;
        item(up != null ? "Update to " + up.version + "  ●" : "Check for updates",
                up != null ? "A newer FF9Recomp is out. Your imported game, mods and saves are kept." : "Look for a newer FF9Recomp on GitHub.",
                this::updateOrCheck);
        item("Quit", "Close the launcher, and the game if it is running.", this::quit);
        if (!has) status.setText("No game imported yet.");
        else if (missing != null) status.setText(missing);
        else status.setText("Ready." + (install.hasMoguri() ? " Moguri Mod found." : "") + " " + Reports.version(this));
        String exit = Reports.lastGameExit(this);
        if (exit != null && exit.startsWith("crashed")) status.append("\nThe game last " + exit + ". Report a problem sends the details.");
        (ready ? play : imp).requestFocus();
        if (!Updater.busy && System.currentTimeMillis() - Updater.lastCheck > 3600_000L) checkUpdates(false);
        // achievements earned while playing go to Steam when the launcher is back
        new Thread(() -> {
            try { SteamLink sl = new SteamLink(this); if (sl.signedIn() && sl.waiting() > 0) sl.sync(); } catch (Exception ignored) { }
        }).start();
    }

    // ------------------------------------------------------------------ updates

    private void checkUpdates(boolean loud) {
        Updater.busy = true;
        new Thread(() -> {
            String msg;
            try {
                Updater.Release r = Updater.check(this);
                msg = r != null ? "FF9Recomp " + r.version + " is out." : "FF9Recomp is up to date.";
            } catch (Exception e) {
                msg = "Could not check for updates: " + e.getMessage();
            }
            Updater.busy = false;
            String m = msg;
            runOnUiThread(() -> {
                if (!page.equals("main")) return;
                boolean had = Updater.available != null;
                if (loud || had) { showMain(); status.setText(m); }
            });
        }).start();
    }

    private void updateOrCheck() {
        if (Updater.busy) return;
        Updater.Release r = Updater.available;
        if (r == null) { status.setText("Checking..."); checkUpdates(true); return; }
        Updater.busy = true;
        new Thread(() -> {
            try {
                Updater.install(this, r);
            } catch (Exception e) {
                Updater.busy = false;
                Updater.status = "Update failed: " + e.getMessage();
            }
        }).start();
        Runnable tick = new Runnable() {
            @Override public void run() {
                if (status == null) return;
                status.setText(Updater.status + (Updater.percent >= 0 ? " " + Updater.percent + "%" : ""));
                if (Updater.busy) root.postDelayed(this, 300);
            }
        };
        root.post(tick);
    }

    private void play() { play(false); }

    private void play(boolean autoload) {
        if (busy || !install.hasGame() || install.missingMessage() != null) return;
        try {
            Settings.apply(this, install.game);
        } catch (Exception e) {
            status.setText("Could not write the settings into Memoria.ini: " + e.getMessage());
            return;
        }
        Intent g = new Intent(this, GameActivity.class).putExtra("autoload", autoload);
        // Swap screens: the game on the second display (the panels then sit on this one)
        if (Settings.prefs(this).getBoolean("swap_screens", false)) {
            android.hardware.display.DisplayManager dm = (android.hardware.display.DisplayManager) getSystemService(DISPLAY_SERVICE);
            android.view.Display[] ds = dm.getDisplays(android.hardware.display.DisplayManager.DISPLAY_CATEGORY_PRESENTATION);
            if (ds.length > 0) {
                android.app.ActivityOptions o = android.app.ActivityOptions.makeBasic();
                o.setLaunchDisplayId(ds[0].getDisplayId());
                startActivity(g.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_MULTIPLE_TASK), o.toBundle());
                return;
            }
        }
        startActivity(g);
    }

    private void quit() {
        int pid = TestControl.gamePid(this);
        if (pid > 0) android.os.Process.killProcess(pid);
        finishAndRemoveTask();
    }

    // ------------------------------------------------------------------ save states

    private void showStates() {
        page = "states";
        newWindow("Save states", true);
        TextView first = null;
        for (int i = 1; i <= SaveStates.SLOTS; i++) {
            int slot = i;
            String d = SaveStates.describe(this, slot);
            TextView v = item("Slot " + slot + ":  " + (d == null ? "empty" : d),
                    d == null ? "In game, Select+R1 keeps the current place here." : "A resumes here (at the start of that place). Y deletes it.",
                    () -> resume(slot));
            v.setTag(slot);
            if (first == null) first = v;
        }
        status.setText("Slots keep the place the party entered last. B to go back.");
        if (first != null) first.requestFocus();
    }

    private void resume(int slot) {
        if (!SaveStates.used(this, slot)) return;
        if (TestControl.gamePid(this) > 0) { status.setText("Leave the game first (Select+Start)."); return; }
        try {
            SaveStates.restore(this, slot);
        } catch (Exception e) {
            status.setText("Could not resume: " + e.getMessage());
            return;
        }
        play(true);
    }

    // ------------------------------------------------------------------ achievements

    private void showAchievements() {
        page = "achievements";
        newWindow("Achievements", true);
        java.util.List<String> got = Achievements.unlocked(this);
        item(got.size() + " unlocked", "Unlocked as you play. Steam: " + (Achievements.steamNote(this)), null).setTag(null);
        SteamLink st = new SteamLink(this);
        if (!st.signedIn())
            item("Sign in to Steam", "Send these to your Steam account: scan a QR code with the Steam app. You never type your password here.", this::steamSignIn);
        else {
            item("Send to Steam now", "Signed in as " + st.account() + ".", this::steamSync);
            item("Sign out of Steam", "Achievements stay here; they are sent again after you sign in.", () -> { st.signOut(); showAchievements(); });
        }
        for (String id : got) item(Achievements.pretty(id), id, null);
        status.setText("B to go back.");
        window.getChildAt(0).requestFocus();
    }

    private void steamSync() {
        busy = true;
        status.setText("Talking to Steam...");
        new Thread(() -> {
            String m;
            try {
                java.util.List<String> got = new SteamLink(this).sync();
                m = got.isEmpty() ? "Steam is up to date." : "Sent to Steam: " + String.join(", ", got);
            } catch (Exception e) {
                m = "Steam: " + SteamLink.message(e);
            }
            String mm = m;
            runOnUiThread(() -> { busy = false; showAchievements(); status.setText(mm); });
        }).start();
    }

    private void steamSignIn() {
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setGravity(Gravity.CENTER_HORIZONTAL);
        box.setPadding(dp(16), dp(12), dp(16), dp(4));
        TextView how = new TextView(this);
        how.setText("On your phone: open the Steam app, tap the Steam Guard shield, then \"Scan a QR code\", and approve the sign-in.");
        how.setTextColor(TEXT);
        box.addView(how);
        android.widget.ImageView qr = new android.widget.ImageView(this);
        qr.setBackgroundColor(Color.WHITE);
        android.util.DisplayMetrics dm = getResources().getDisplayMetrics();
        int size = Math.max(dp(150), Math.min(dp(260), Math.min(dm.widthPixels, dm.heightPixels) - dp(220)));
        box.addView(qr, new LinearLayout.LayoutParams(size, size));
        TextView st = new TextView(this);
        st.setText("Connecting to Steam...");
        st.setTextColor(DIM);
        box.addView(st);
        ScrollView sv = new ScrollView(this);
        sv.addView(box);
        final java.util.concurrent.Future<?>[] cancel = new java.util.concurrent.Future<?>[1];
        final boolean[] open = { true };
        android.app.AlertDialog dlg = new android.app.AlertDialog.Builder(this, android.R.style.Theme_DeviceDefault_Dialog_Alert)
                .setTitle("Sign in to Steam").setView(sv).setNegativeButton("Cancel", null).show();
        dlg.setOnDismissListener(d -> { open[0] = false; if (cancel[0] != null) cancel[0].cancel(true); });
        SteamLink link = new SteamLink(this);
        new Thread(() -> link.signInQr(new SteamLink.QrListener() {
            @Override public void qr(String url) {
                android.graphics.Bitmap bm = qrBitmap(url, size);
                runOnUiThread(() -> { qr.setImageBitmap(bm); st.setText("Waiting for the Steam app..."); });
            }
            @Override public void done(String error) {
                runOnUiThread(() -> {
                    if (open[0]) dlg.dismiss();
                    showAchievements();
                    if (error == null) status.setText("Signed in to Steam as " + link.account() + ".");
                    else if (!error.equals("cancelled")) status.setText("Not signed in: " + error);
                });
            }
        }, cancel), "steam-sign-in").start();
    }

    private static android.graphics.Bitmap qrBitmap(String text, int size) {
        try {
            com.google.zxing.common.BitMatrix m = new com.google.zxing.qrcode.QRCodeWriter()
                    .encode(text, com.google.zxing.BarcodeFormat.QR_CODE, size, size);
            int[] px = new int[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) px[y * size + x] = m.get(x, y) ? Color.BLACK : Color.WHITE;
            return android.graphics.Bitmap.createBitmap(px, size, size, android.graphics.Bitmap.Config.ARGB_8888);
        } catch (Exception e) {
            return null;
        }
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
        item(storageLabel(), "Keep the imported game, mods and saves on the SD card or in internal storage. Moving copies everything, then removes the old copy.",
                this::moveStorage);
        item("Arrange touch controls", "Drag the on-screen buttons where you want them; set their size and opacity.",
                () -> startActivity(new Intent(this, TouchLayoutActivity.class)));
        status.setText("Left/right or A to change. B to go back. Applied when the game starts.");
        if (first != null) first.requestFocus();
    }

    private String storageLabel() {
        return "Storage:  " + (Storage.onSd(this) ? "SD card" + (Storage.sdMissing(this) ? " (not inserted)" : "") : "Internal");
    }

    private void moveStorage() {
        boolean toSd = !Storage.onSd(this);
        if (toSd && Storage.sdRoot(this) == null) { status.setText("No SD card is inserted."); return; }
        if (TestControl.gamePid(this) > 0) { status.setText("Leave the game first."); return; }
        busy = true;
        new Thread(() -> {
            String msg;
            try {
                Storage.move(this, toSd, what -> runOnUiThread(() -> status.setText(what)));
                msg = toSd ? "Your files are on the SD card now." : "Your files are in internal storage now.";
            } catch (Exception e) {
                msg = "Moving failed, nothing changed: " + e.getMessage();
            }
            String m = msg;
            runOnUiThread(() -> {
                busy = false;
                install = new GameInstall(this);
                showSettings();
                status.setText(m);
            });
        }).start();
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
        if (page.equals("states") && code == KeyEvent.KEYCODE_BUTTON_Y && f != null && f.getTag() instanceof Integer) {
            if (down) { SaveStates.delete(this, (Integer) f.getTag()); int at = (Integer) f.getTag(); showStates(); window.getChildAt(at - 1).requestFocus(); }
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
