package com.ff9recomp;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.LayerDrawable;
import android.graphics.drawable.StateListDrawable;
import android.net.Uri;
import android.os.Bundle;
import android.text.SpannableStringBuilder;
import android.text.Spanned;
import android.text.style.ForegroundColorSpan;
import android.text.style.RelativeSizeSpan;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.CompoundButton;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.RadioButton;
import android.widget.RadioGroup;
import android.widget.ScrollView;
import android.widget.TextView;

import java.io.File;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * The entry point, laid out like FF7Recomp's launcher (the game runs in its own ":game" process,
 * so the launcher survives a crash): the command window on the left (Play, Mods, Save states,
 * Achievements, Settings, Game files, Report a problem, Quit), a one-line help window on top,
 * and the page's window on the right. FF9's look: slate windows with a pale border, gold accents,
 * the hand cursor on the commands. Everything works with a controller: the D-pad moves, A
 * confirms, B goes back to the commands.
 */
public class LauncherActivity extends Activity {
    private static final int PICK_GAME = 1, PICK_SAVE = 2;
    private static final int BG = Color.rgb(8, 9, 14), TEXT = 0xFFF2EEE2, DIM = 0xFFAEB0BC, ACCENT = 0xFFE8C66A;
    private static final int WIN_TOP = 0xFF5E6478, WIN_BOTTOM = 0xFF22252F, HOT_TOP = 0xFF8A8466, HOT_BOTTOM = 0xFF3E3A2A,
            BTN_TOP = 0xFF4A4F60, BTN_BOTTOM = 0xFF262935, BORDER = 0xFFDCD6C4, INNER = 0xFF3A3F50;

    private GameInstall install;
    private TextView status;
    private FrameLayout content;
    private Button playButton;
    private final Map<String, Button> nav = new HashMap<>();
    private String page = "settings";
    private boolean busy, withSaves;
    private ModCatalog.Mod modShown;
    private String testArgs, testLoad;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        Reports.installCrashHandler(this);
        install = new GameInstall(this);

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.HORIZONTAL);
        root.setBackgroundColor(BG);
        root.setPadding(dp(24), dp(20), dp(24), dp(20));

        // left: the title and the command window
        LinearLayout side = column();
        side.setPadding(0, 0, dp(14), 0);
        TextView title = text("FINAL FANTASY IX", 17, TEXT);
        title.setTypeface(Typeface.create("serif", Typeface.BOLD));
        title.setLetterSpacing(0.04f);
        title.setSingleLine(true);
        side.addView(title);
        side.addView(text("native ARM64 · Memoria" + (install.hasMoguri() ? " · Moguri" : ""), 11, ACCENT));
        side.addView(space(10));
        LinearLayout menu = column();
        menu.setBackground(window(WIN_TOP, WIN_BOTTOM, 8));
        menu.setPadding(dp(8), dp(6), dp(8), dp(6));
        playButton = menuItem("Play", null);
        playButton.setTextSize(19);
        playButton.setTypeface(Typeface.DEFAULT_BOLD);
        menu.addView(playButton);
        menu.addView(menuItem("Mods", "mods"));
        menu.addView(menuItem("Save states", "states"));
        menu.addView(menuItem("Achievements", "achievements"));
        menu.addView(menuItem("Settings", "settings"));
        menu.addView(menuItem("Game files", "game"));
        menu.addView(menuItem("Report a problem", "report"));
        Button quit = menuItem("Quit", null);
        quit.setOnClickListener(v -> quit());
        menu.addView(quit);
        side.addView(menu, new LinearLayout.LayoutParams(dp(200), ViewGroup.LayoutParams.WRAP_CONTENT));
        ScrollView sideScroll = new ScrollView(this);
        sideScroll.setVerticalScrollBarEnabled(false);
        sideScroll.addView(side);
        root.addView(sideScroll, new LinearLayout.LayoutParams(dp(214), ViewGroup.LayoutParams.MATCH_PARENT));

        // right: the help window over the page's window
        LinearLayout right = column();
        status = text("", 14, TEXT);
        status.setBackground(window(WIN_TOP, WIN_BOTTOM, 8));
        status.setPadding(dp(16), dp(8), dp(16), dp(8));
        status.setMaxLines(2);
        status.setEllipsize(android.text.TextUtils.TruncateAt.END);
        right.addView(status, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        right.addView(space(10));
        content = new FrameLayout(this);
        content.setBackground(window(WIN_TOP, WIN_BOTTOM, 10));
        content.setPadding(dp(22), dp(16), dp(22), dp(16));
        right.addView(content, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1));
        root.addView(right, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1));
        setContentView(root);
        hideSystemBars();

        if (!install.hasGame()) page = "game";
        refresh();
        (ready() ? playButton : nav.get(page)).requestFocus();
        handleTestIntent(getIntent());
        if (!Updater.busy && System.currentTimeMillis() - Updater.lastCheck > 3600_000L) checkUpdates(false);
        new Thread(() -> {    // achievements earned while playing go to Steam when the launcher is back
            try { SteamLink sl = new SteamLink(this); if (sl.signedIn() && sl.waiting() > 0) sl.sync(); } catch (Exception ignored) { }
        }).start();
    }

    @Override protected void onNewIntent(Intent i) { super.onNewIntent(i); handleTestIntent(i); }

    @Override protected void onResume() { super.onResume(); Sfx.load(this); hideSystemBars(); if (!busy) { View f = getCurrentFocus(); refresh(); if (f != null && !inContent(f)) f.requestFocus(); } }

    /** adb test API: am start ... --ez test_play true presses Play once the launcher is ready. */
    private void handleTestIntent(Intent i) {
        if (i != null && i.getBooleanExtra(TestControl.EXTRA_PLAY, false)) {
            i.removeExtra(TestControl.EXTRA_PLAY);
            testArgs = i.getStringExtra("test_args");     // adb tests: Memoria's launch options (e.g. "-memoria-battle 336")
            i.removeExtra("test_args");
            testLoad = i.getStringExtra("test_load");     // adb tests: "slot:file" (0-based) loaded from the title (FF9_LOAD_FILE)
            i.removeExtra("test_load");
            content.post(this::play);
        }
    }

    private boolean ready() { return install.hasGame() && install.missingMessage() == null; }

    // ------------------------------------------------------------------ pages

    private void show(String which) { page = which; refresh(); focusContent(); }

    private void refresh() {
        updateStatus();
        playButton.setEnabled(ready() && !busy);
        content.removeAllViews();
        View v;
        switch (page) {
            case "mods": v = modsPage(); break;
            case "mod": v = modPage(modShown); break;
            case "states": v = statesPage(); break;
            case "achievements": v = achievementsPage(); break;
            case "game": v = gamePage(); break;
            case "report": v = reportPage(); break;
            default: v = settingsPage(); break;
        }
        ScrollView sv = new ScrollView(this);
        sv.setFillViewport(true);
        sv.addView(v);
        content.addView(sv);
        theme(content);
        String sel = page.equals("mod") ? "mods" : page;
        for (Map.Entry<String, Button> e : nav.entrySet()) e.getValue().setSelected(e.getKey().equals(sel));
    }

    private void updateStatus() {
        String game = !install.hasGame() ? "not imported" : install.missingMessage() != null ? "files missing" : "ready";
        int on = ModCatalog.enabled(this).size();
        StringBuilder s = new StringBuilder("Game: " + game + "   ·   Mods: " + on + " on   ·   Frame rate: " + Settings.valueText(this, Settings.find("fps")));
        Updater.Release up = Updater.available;
        if (up != null) s.append("   ·   Update ").append(up.version).append(" is out (Game files)");
        String exit = Reports.lastGameExit(this);
        if (exit != null && exit.startsWith("crashed")) s.append("\nThe game last ").append(exit).append(": Report a problem sends the details.");
        status.setText(s);
    }

    private void focusContent() {
        content.post(() -> {
            View f = firstFocusable(content);
            if (f != null) f.requestFocus();
        });
    }

    private static View firstFocusable(View v) {
        if (v instanceof ViewGroup && !(v instanceof CompoundButton)) {
            ViewGroup g = (ViewGroup) v;
            for (int i = 0; i < g.getChildCount(); i++) { View f = firstFocusable(g.getChildAt(i)); if (f != null) return f; }
            return null;
        }
        return v.isFocusable() && v.getVisibility() == View.VISIBLE && v.isEnabled() ? v : null;
    }

    // ------------------------------------------------------------------ play / quit

    private void play() { play(false); }

    private void play(boolean autoload) {
        if (busy) return;
        if (!ready()) { show("game"); return; }
        try {
            Settings.apply(this, install.game);
        } catch (Exception e) {
            status.setText("Could not write the settings into Memoria.ini: " + e.getMessage());
            return;
        }
        Intent g = new Intent(this, GameActivity.class).putExtra("autoload", autoload);
        if (testArgs != null) { g.putExtra("test_args", testArgs); testArgs = null; }
        if (testLoad != null && testLoad.matches("\\d+:\\d+")) { g.putExtra("test_load", testLoad); testLoad = null; }
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

    // ------------------------------------------------------------------ settings

    private static final String[][] GROUPS = {
        { "Frame rate", "fps" },
        { "Display", "resolution", "widescreen", "skip_intros", "crt_filter", "show_fps" },
        { "Game", "start", "boosters", "speed", "turbo_dialog" },
        { "Controls", "swap_confirm", "analog_run", "touch_controls", "rumble" },
        { "Screens", "bottom_screen", "bottom_buttons", "swap_screens" },
        { "Sound", "menu_sounds", "music_volume", "sound_volume" },
    };

    /** A short note beside a choice, as FF7Recomp's frame-rate list has. */
    private static String choiceNote(String key, int i) {
        switch (key) {
            case "fps": return new String[] { "As shipped: fields 30, battles 15", "Memoria fills in frames: everything at 60", "Fields and battles at 120 (experimental, warmer)" }[i];
            case "start": return new String[] { "As shipped", "Straight back to the place you were last" }[i];
            case "resolution": return new String[] { "The screen's own", "Cooler, a little softer", "Coolest, for weaker devices" }[i];
            case "touch_controls": return new String[] { "Only when no controller is connected", "Always on screen", "Never" }[i];
            default: return null;
        }
    }

    private View settingsPage() {
        LinearLayout col = column();
        boolean first = true;
        for (String[] g : GROUPS) {
            TextView h = heading(g[0]);
            if (!first) h.setPadding(0, dp(14), 0, dp(4));
            first = false;
            col.addView(h);
            for (int k = 1; k < g.length; k++) {
                Settings.Option o = Settings.find(g[k]);
                if (o == null) continue;
                if (o.isBool()) {
                    CheckBox c = new CheckBox(this);
                    c.setText(withNote(o.label, o.note));
                    c.setChecked(Settings.get(this, o) != 0);
                    c.setOnCheckedChangeListener((x, on) -> { Settings.set(this, o, on ? 1 : 0); updateStatus(); });
                    col.addView(c);
                } else if (o.choices.length <= 3) {
                    if (g.length > 2) col.addView(text(o.label, 15, ACCENT));
                    RadioGroup rg = new RadioGroup(this);
                    int cur = Settings.get(this, o);
                    for (int i = 0; i < o.choices.length; i++) {
                        RadioButton r = new RadioButton(this);
                        r.setId(View.generateViewId());
                        r.setText(withNote(o.choices[i], choiceNote(o.key, i)));
                        rg.addView(r);
                        if (i == cur) r.setChecked(true);
                        int val = i;
                        r.setOnCheckedChangeListener((x, on) -> { if (on) { Settings.set(this, o, val); updateStatus(); } });
                    }
                    col.addView(rg);
                } else {
                    col.addView(stepper(o));     // a longer scale (volumes, speed): left/right or A
                }
            }
        }
        TextView gh = heading("Guide");
        gh.setPadding(0, dp(14), 0, dp(4));
        col.addView(gh);
        col.addView(note("A walkthrough page of your choice on the second screen: Select+L1 switches between it and the panels; scroll it by touch. Leave empty for none."));
        android.widget.EditText url = new android.widget.EditText(this);
        url.setSingleLine(true);
        url.setHint("https://... (a guide page you like)");
        url.setText(Settings.prefs(this).getString("guide_url", ""));
        url.setTextColor(TEXT);
        url.setHintTextColor(DIM);
        url.setInputType(android.text.InputType.TYPE_CLASS_TEXT | android.text.InputType.TYPE_TEXT_VARIATION_URI);
        url.addTextChangedListener(new android.text.TextWatcher() {
            @Override public void beforeTextChanged(CharSequence c, int a, int b, int d) { }
            @Override public void onTextChanged(CharSequence c, int a, int b, int d) { }
            @Override public void afterTextChanged(android.text.Editable e) { Settings.prefs(LauncherActivity.this).edit().putString("guide_url", e.toString().trim()).apply(); }
        });
        col.addView(url);
        col.addView(space(8));
        LinearLayout r = row();
        r.addView(button("Arrange touch controls", v -> startActivity(new Intent(this, TouchLayoutActivity.class))));
        col.addView(r);
        return col;
    }

    private View stepper(Settings.Option o) {
        Button b = listRow("", null);
        Runnable label = () -> b.setText(withNote(o.label + ":   ◀ " + Settings.valueText(this, o) + " ▶", o.note));
        label.run();
        b.setOnClickListener(v -> { change(o, 1); label.run(); });
        b.setOnKeyListener((v, code, e) -> {
            if (code != KeyEvent.KEYCODE_DPAD_LEFT && code != KeyEvent.KEYCODE_DPAD_RIGHT) return false;
            if (e.getAction() == KeyEvent.ACTION_DOWN) { change(o, code == KeyEvent.KEYCODE_DPAD_RIGHT ? 1 : -1); label.run(); }
            return true;
        });
        return b;
    }

    private void change(Settings.Option o, int dir) {
        int n = o.isBool() ? 2 : o.choices.length;
        Settings.set(this, o, (Settings.get(this, o) + dir + n) % n);
        updateStatus();
    }

    // ------------------------------------------------------------------ mods

    private String modState(ModCatalog.Mod m) {
        if (!ModCatalog.installed(this, m)) return "";
        String s = ModCatalog.isEnabled(this, m) ? "on" : "off";
        return ModCatalog.updatable(this, m) ? s + ", update " + m.version : s;
    }

    private View modsPage() {
        LinearLayout col = column();
        col.addView(heading("Mods"));
        List<ModCatalog.Mod> all = ModCatalog.mods(this);
        col.addView(note("Memoria's mod catalog. Installed mods are listed in Memoria.ini, highest priority first; that order decides which mod wins."));
        LinearLayout actions = row();
        actions.addView(button(all.isEmpty() ? "Get the catalog" : "Refresh the catalog", v -> refreshCatalog()));
        if (ModDownloader.busy) actions.addView(button("Cancel the download", v -> ModDownloader.cancel()));
        col.addView(actions);
        if (!ModDownloader.status.isEmpty()) col.addView(note(ModDownloader.status));
        String cat = null;
        for (ModCatalog.Mod m : all) {
            if (!m.category.equals(cat)) {
                cat = m.category;
                TextView h = text(cat.isEmpty() ? "Other" : cat, 15, ACCENT);
                h.setPadding(0, dp(10), 0, dp(2));
                col.addView(h);
            }
            String st = modState(m);
            col.addView(listRow(withNote(m.name, (st.isEmpty() ? "" : "[" + st + "]  ") + m.version + (m.author.isEmpty() ? "" : " · " + m.author)),
                    v -> { modShown = m; show("mod"); }));
        }
        if (ModDownloader.busy) pollMods();
        return col;
    }

    private View modPage(ModCatalog.Mod m) {
        LinearLayout col = column();
        if (m == null) return col;
        col.addView(heading(m.name));
        col.addView(note(m.category + " · version " + m.version + (m.author.isEmpty() ? "" : " · by " + m.author) + " · folder " + m.folder));
        LinearLayout actions = row();
        boolean inst = ModCatalog.installed(this, m), on = inst && ModCatalog.isEnabled(this, m);
        if (!inst) actions.addView(button("Install", v -> installMod(m)));
        else {
            actions.addView(button(on ? "Turn off" : "Turn on", v -> {
                try { ModCatalog.setEnabled(this, m, !on); } catch (Exception e) { status.setText(e.getMessage()); return; }
                refresh(); focusContent();
            }));
            if (ModCatalog.updatable(this, m)) actions.addView(button("Update to " + m.version, v -> installMod(m)));
            actions.addView(button("Remove", v -> styled(new AlertDialog.Builder(this, android.R.style.Theme_DeviceDefault_Dialog_Alert)
                    .setTitle("Remove " + m.name + "?").setMessage("Its folder is deleted from your game.")
                    .setPositiveButton("Remove", (d, w) -> {
                        try { ModDownloader.remove(this, m); } catch (Exception e) { status.setText(e.getMessage()); }
                        refresh(); focusContent();
                    }).setNegativeButton("Keep", null).show())));
        }
        actions.addView(button("Back", v -> show("mods")));
        col.addView(actions);
        if (!ModDownloader.status.isEmpty()) col.addView(note(ModDownloader.status));
        TextView d = text(m.description, 14, DIM);
        d.setPadding(0, dp(10), 0, 0);
        col.addView(d);
        if (ModDownloader.busy) pollMods();
        return col;
    }

    private void refreshCatalog() {
        busy = true;
        status.setText("Getting Memoria's catalog...");
        new Thread(() -> {
            String m = null;
            try { ModCatalog.refresh(this); } catch (Exception e) { m = "Could not get the catalog: " + e.getMessage(); }
            String mm = m;
            runOnUiThread(() -> { busy = false; refresh(); focusContent(); if (mm != null) status.setText(mm); });
        }).start();
    }

    private void installMod(ModCatalog.Mod m) {
        if (ModDownloader.busy) { status.setText("One install at a time: " + ModDownloader.status); return; }
        if (TestControl.gamePid(this) > 0) { status.setText("Leave the game first (Select+Start)."); return; }
        ModDownloader.busy = true;
        ModDownloader.status = "Starting " + m.name;
        new Thread(() -> {
            try { ModDownloader.install(this, m); }
            catch (Exception e) {
                ModDownloader.status = m.name + ": " + e.getMessage() + ("cancelled".equals(e.getMessage()) ? "" : " (Install again to continue the download.)");
            }
        }, "mod-install").start();
        refresh();
        focusContent();
    }

    private final Runnable modTick = new Runnable() {
        @Override public void run() {
            if (!page.equals("mods") && !page.equals("mod")) return;
            status.setText(ModDownloader.status);
            if (ModDownloader.busy) content.postDelayed(this, 500);
            else { String st = ModDownloader.status; refresh(); focusContent(); status.setText(st); }
        }
    };

    private void pollMods() { content.removeCallbacks(modTick); content.postDelayed(modTick, 500); }

    // ------------------------------------------------------------------ save states

    private View statesPage() {
        LinearLayout col = column();
        col.addView(heading("Save states"));
        col.addView(note("In game, Select+R1 keeps the place the party is in. A resumes a slot (at the start of that place); Y deletes it."));
        for (int i = 1; i <= SaveStates.SLOTS; i++) {
            int slot = i;
            String d = SaveStates.describe(this, slot);
            Button b = listRow(withNote("Slot " + slot, d == null ? "empty" : d), v -> resume(slot));
            b.setTag(slot);
            col.addView(b);
        }
        return col;
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

    private View achievementsPage() {
        LinearLayout col = column();
        col.addView(heading("Achievements"));
        List<String> got = Achievements.unlocked(this);
        col.addView(text(got.size() + " unlocked on this device", 16, ACCENT));
        SteamLink st = new SteamLink(this);
        col.addView(note(st.signedIn()
                ? "Signed in to Steam as " + st.account() + ". " + (st.waiting() > 0 ? st.waiting() + " waiting to be sent." : "All sent.")
                : "Sign in to send them to your Steam account: scan a QR code with the Steam app. You never type your Steam password here."));
        LinearLayout actions = row();
        if (!st.signedIn()) actions.addView(button("Sign in to Steam", v -> steamSignIn()));
        else {
            actions.addView(button("Send to Steam now", v -> steamSync()));
            actions.addView(button("Sign out", v -> { st.signOut(); refresh(); focusContent(); }));
        }
        col.addView(actions);
        for (String id : got) col.addView(text("★  " + Achievements.pretty(id), 15, TEXT));
        return col;
    }

    private void steamSync() {
        busy = true;
        status.setText("Talking to Steam...");
        new Thread(() -> {
            String m;
            try {
                List<String> got = new SteamLink(this).sync();
                m = got.isEmpty() ? "Steam is up to date." : "Sent to Steam: " + String.join(", ", got);
            } catch (Exception e) {
                m = "Steam: " + SteamLink.message(e);
            }
            String mm = m;
            runOnUiThread(() -> { busy = false; refresh(); focusContent(); status.setText(mm); });
        }).start();
    }

    private void steamSignIn() {
        LinearLayout box = column();
        box.setGravity(Gravity.CENTER_HORIZONTAL);
        box.setPadding(dp(16), dp(12), dp(16), dp(4));
        box.addView(text("On your phone: open the Steam app, tap the Steam Guard shield, then \"Scan a QR code\", and approve the sign-in.", 15, TEXT));
        android.widget.ImageView qr = new android.widget.ImageView(this);
        qr.setBackgroundColor(Color.WHITE);
        android.util.DisplayMetrics dm = getResources().getDisplayMetrics();
        int size = Math.max(dp(150), Math.min(dp(260), Math.min(dm.widthPixels, dm.heightPixels) - dp(220)));
        box.addView(qr, new LinearLayout.LayoutParams(size, size));
        TextView st = text("Connecting to Steam...", 13, DIM);
        box.addView(st);
        ScrollView sv = new ScrollView(this);
        sv.addView(box);
        final java.util.concurrent.Future<?>[] cancel = new java.util.concurrent.Future<?>[1];
        final boolean[] open = { true };
        AlertDialog dlg = styled(new AlertDialog.Builder(this, android.R.style.Theme_DeviceDefault_Dialog_Alert)
                .setTitle("Sign in to Steam").setView(sv).setNegativeButton("Cancel", null).show());
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
                    refresh();
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

    // ------------------------------------------------------------------ game files

    private View gamePage() {
        LinearLayout col = column();
        col.addView(heading("Game files"));
        boolean has = install.hasGame();
        String missing = has ? install.missingMessage() : null;
        col.addView(text(!has ? "No game imported yet." : missing != null ? missing
                : "Your FINAL FANTASY IX is imported" + (install.hasMoguri() ? ", with Moguri Mod." : "."), 15, has && missing == null ? ACCENT : TEXT));
        col.addView(note("Pick your FINAL FANTASY IX folder (the Steam copy, with Memoria installed; Moguri Mod if you use it). It is copied into the app once; nothing from the game ships in the app."));
        LinearLayout actions = row();
        actions.addView(button(has ? "Import again" : "Import game folder", v -> startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE), PICK_GAME)));
        col.addView(actions);

        TextView hs = heading("Saves");
        hs.setPadding(0, dp(14), 0, dp(4));
        col.addView(hs);
        col.addView(note("Bring your PC saves: pick SavedData_ww.dat from the PC (in AppData/LocalLow/SquareEnix/FINAL FANTASY IX/Steam/EncryptedSavedData). The saves here are kept as a backup first."));
        LinearLayout sv = row();
        sv.addView(button("Import a PC save", v -> startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*"), PICK_SAVE)));
        col.addView(sv);

        TextView h = heading("Storage");
        h.setPadding(0, dp(14), 0, dp(4));
        col.addView(h);
        col.addView(note("The game, mods and saves are " + (Storage.onSd(this) ? "on the SD card" + (Storage.sdMissing(this) ? " (not inserted)" : "") : "in internal storage")
                + ", " + Storage.human(install.files.getUsableSpace()) + " free there. Moving copies everything, then removes the old copy."));
        LinearLayout st = row();
        st.addView(button(Storage.onSd(this) ? "Move to internal storage" : "Move to the SD card", v -> moveStorage()));
        col.addView(st);

        h = heading("App");
        h.setPadding(0, dp(14), 0, dp(4));
        col.addView(h);
        Updater.Release up = Updater.available;
        col.addView(note("FF9Recomp " + Reports.version(this) + (up != null ? ". Version " + up.version + " is out: your imported game, mods and saves are kept." : ".")));
        LinearLayout ap = row();
        ap.addView(button(up != null ? "Update to " + up.version : "Check for updates", v -> updateOrCheck()));
        col.addView(ap);
        return col;
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
            runOnUiThread(() -> { busy = false; install = new GameInstall(this); refresh(); focusContent(); status.setText(m); });
        }).start();
    }

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
                if (busy) return;
                if (loud) { refresh(); focusContent(); status.setText(m); }
                else if (Updater.available != null) updateStatus();
            });
        }).start();
    }

    private void updateOrCheck() {
        if (Updater.busy) return;
        Updater.Release r = Updater.available;
        if (r == null) { status.setText("Checking..."); checkUpdates(true); return; }
        Updater.busy = true;
        new Thread(() -> {
            try { Updater.install(this, r); }
            catch (Exception e) { Updater.busy = false; Updater.status = "Update failed: " + e.getMessage(); }
        }).start();
        content.post(new Runnable() {
            @Override public void run() {
                status.setText(Updater.status + (Updater.percent >= 0 ? " " + Updater.percent + "%" : ""));
                if (Updater.busy) content.postDelayed(this, 300);
            }
        });
    }

    // ------------------------------------------------------------------ report a problem

    private View reportPage() {
        LinearLayout col = column();
        col.addView(heading("Report a problem"));
        col.addView(note("One zip with the logs, the crash record and the settings. It goes to Downloads, then you choose where to send it: nothing is sent by the app."));
        CheckBox saves = new CheckBox(this);
        saves.setText(withNote("Include my saves", "useful when a save does not load"));
        saves.setChecked(withSaves);
        saves.setOnCheckedChangeListener((x, on) -> withSaves = on);
        col.addView(saves);
        LinearLayout actions = row();
        actions.addView(button("Make the report", v -> makeReport()));
        col.addView(actions);
        return col;
    }

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
        if (req == PICK_SAVE && res == RESULT_OK && data != null) { importSave(data.getData()); return; }
        if (req != PICK_GAME || res != RESULT_OK || data == null) return;
        Uri tree = data.getData();
        busy = true;
        new Thread(() -> {
            String err = null;
            try {
                install.importFrom(tree, what -> runOnUiThread(() -> status.setText(what)));
            } catch (Exception e) {
                err = e.getMessage();
            }
            String e2 = err;
            runOnUiThread(() -> {
                busy = false;
                refresh();
                if (e2 != null) status.setText("Import failed: " + e2);
            });
        }).start();
    }

    /** A PC save file (SavedData_ww.dat, the Steam release's: the same format the game uses here). */
    private void importSave(Uri uri) {
        if (TestControl.gamePid(this) > 0) { status.setText("Leave the game first (Select+Start)."); return; }
        try {
            byte[] b;
            try (java.io.InputStream in = getContentResolver().openInputStream(uri)) { b = in.readAllBytes(); }
            if (b.length < 100_000) throw new java.io.IOException("this is not FF9's save file (SavedData_ww.dat is a few megabytes)");
            File dir = Reports.savesDir(this);
            dir.mkdirs();
            File cur = new File(dir, "SavedData_ww.dat");
            if (cur.isFile()) {
                String stamp = new java.text.SimpleDateFormat("yyyyMMdd-HHmm", java.util.Locale.US).format(new java.util.Date());
                if (!cur.renameTo(new File(dir, "SavedData_ww.dat.before-import-" + stamp))) throw new java.io.IOException("cannot keep the current saves");
            }
            java.nio.file.Files.write(cur.toPath(), b);
            status.setText("PC saves imported: Load Game on the title screen shows them. Your earlier saves are kept beside them as a backup.");
        } catch (Exception e) {
            status.setText("Save import failed: " + e.getMessage());
        }
    }

    // ------------------------------------------------------------------ keys

    @Override
    public boolean dispatchKeyEvent(KeyEvent e) {
        int code = e.getKeyCode();
        View f = getCurrentFocus();
        boolean down = e.getAction() == KeyEvent.ACTION_DOWN;
        if (code == KeyEvent.KEYCODE_BUTTON_A || code == KeyEvent.KEYCODE_ENTER || code == KeyEvent.KEYCODE_DPAD_CENTER) {
            if (down && e.getRepeatCount() == 0 && f != null) { Sfx.play(this, f.isEnabled() ? Sfx.CURSOR : Sfx.BUZZER); f.performClick(); }
            return true;
        }
        if (down && (code == KeyEvent.KEYCODE_BUTTON_B || code == KeyEvent.KEYCODE_BACK) && e.getRepeatCount() == 0 && inContent(f)) Sfx.play(this, Sfx.CANCEL);
        // right from the commands goes into the page
        if (code == KeyEvent.KEYCODE_DPAD_RIGHT && f != null && !inContent(f)) { if (down) { Sfx.play(this, Sfx.CURSOR); focusContent(); } return true; }
        if (down && (code == KeyEvent.KEYCODE_DPAD_UP || code == KeyEvent.KEYCODE_DPAD_DOWN || code == KeyEvent.KEYCODE_DPAD_LEFT || code == KeyEvent.KEYCODE_DPAD_RIGHT)) {
            View before = f;
            boolean r = super.dispatchKeyEvent(e);
            if (getCurrentFocus() != before) Sfx.play(this, Sfx.CURSOR);
            return r;
        }
        if (code == KeyEvent.KEYCODE_BUTTON_B || code == KeyEvent.KEYCODE_BACK || code == KeyEvent.KEYCODE_ESCAPE) {
            if (!down || e.getRepeatCount() > 0) return true;
            if (page.equals("mod") && inContent(f)) { show("mods"); return true; }
            if (inContent(f)) { Button n = nav.get(page.equals("mod") ? "mods" : page); if (n != null) n.requestFocus(); return true; }
            if (code == KeyEvent.KEYCODE_BACK) moveTaskToBack(true);
            return true;
        }
        if (code == KeyEvent.KEYCODE_BUTTON_Y && page.equals("states") && f != null && f.getTag() instanceof Integer) {
            if (down) { int slot = (Integer) f.getTag(); SaveStates.delete(this, slot); refresh(); focusTagged(slot); }
            return true;
        }
        return super.dispatchKeyEvent(e);
    }

    private boolean inContent(View v) {
        for (View p = v; p != null; p = p.getParent() instanceof View ? (View) p.getParent() : null) if (p == content) return true;
        return false;
    }

    private void focusTagged(Object tag) {
        content.post(() -> { View v = content.findViewWithTag(tag); if (v != null) v.requestFocus(); });
    }

    // ------------------------------------------------------------------ look

    private void hideSystemBars() {
        WindowInsetsController c = getWindow().getInsetsController();
        if (c != null) {
            c.hide(WindowInsets.Type.statusBars() | WindowInsets.Type.navigationBars());
            c.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
        }
    }

    private int dp(float v) { return Math.round(v * getResources().getDisplayMetrics().density); }

    private LinearLayout column() { LinearLayout l = new LinearLayout(this); l.setOrientation(LinearLayout.VERTICAL); return l; }

    private LinearLayout row() {
        LinearLayout l = new LinearLayout(this);
        l.setOrientation(LinearLayout.HORIZONTAL);
        l.setPadding(0, dp(6), 0, dp(6));
        return l;
    }

    private View space(int dpH) { View v = new View(this); v.setLayoutParams(new LinearLayout.LayoutParams(1, dp(dpH))); return v; }

    private TextView text(String s, int sp, int color) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(sp);
        t.setTextColor(color);
        shadow(t);
        return t;
    }

    private TextView note(String s) {
        TextView t = text(s, 13, DIM);
        t.setPadding(0, dp(2), 0, dp(4));
        return t;
    }

    private TextView heading(String s) {
        TextView t = text(s, 21, TEXT);
        t.setTypeface(Typeface.create("serif", Typeface.BOLD));
        t.setPadding(0, 0, 0, dp(4));
        return t;
    }

    /** "Label   note": the note smaller and dimmer, as in FF7Recomp's lists. */
    private CharSequence withNote(String label, String note) {
        if (note == null || note.isEmpty()) return label;
        SpannableStringBuilder b = new SpannableStringBuilder(label).append("   ");
        int at = b.length();
        b.append(note);
        b.setSpan(new ForegroundColorSpan(DIM), at, b.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        b.setSpan(new RelativeSizeSpan(0.82f), at, b.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        return b;
    }

    private void shadow(TextView t) {
        float d = getResources().getDisplayMetrics().density;
        t.setShadowLayer(1.2f * d, 1.2f * d, 1.2f * d, 0xD0000000);
    }

    /** A window: slate gradient, pale outer border, darker inner line. */
    private Drawable window(int top, int bottom, int cornerDp) {
        GradientDrawable fill = new GradientDrawable(GradientDrawable.Orientation.TL_BR, new int[] { top, bottom });
        fill.setCornerRadius(dp(cornerDp));
        fill.setStroke(dp(2), BORDER);
        GradientDrawable inner = new GradientDrawable();
        inner.setColor(0);
        inner.setCornerRadius(Math.max(0, dp(cornerDp) - dp(2)));
        inner.setStroke(Math.max(1, dp(1)), INNER);
        LayerDrawable l = new LayerDrawable(new Drawable[] { fill, inner });
        l.setLayerInset(1, dp(2), dp(2), dp(2), dp(2));
        return l;
    }

    private Drawable hot(int cornerDp) {
        StateListDrawable sl = new StateListDrawable();
        sl.addState(new int[] { android.R.attr.state_focused }, window(HOT_TOP, HOT_BOTTOM, cornerDp));
        sl.addState(new int[] {}, new android.graphics.drawable.ColorDrawable(0));
        return sl;
    }

    /** A command in the command window: plain text, the hand cursor points at it. */
    private Button menuItem(String label, String which) {
        Button b = new Button(this);
        b.setText(label);
        b.setAllCaps(false);
        b.setTextSize(16);
        b.setTextColor(new android.content.res.ColorStateList(
                new int[][] { { -android.R.attr.state_enabled }, { android.R.attr.state_selected }, {} }, new int[] { 0xFF7F8296, ACCENT, TEXT }));
        shadow(b);
        b.setGravity(Gravity.CENTER_VERTICAL | Gravity.START);
        b.setBackground(null);
        b.setDefaultFocusHighlightEnabled(false);
        b.setPadding(dp(4), 0, dp(4), 0);
        b.setCompoundDrawablesRelativeWithIntrinsicBounds(new HandCursor(getResources().getDisplayMetrics().density), null, null, null);
        b.setCompoundDrawablePadding(dp(6));
        b.setStateListAnimator(null);
        b.setFocusable(true);
        b.setMinHeight(0);
        b.setMinimumHeight(0);
        b.setOnClickListener(v -> { if (which == null) play(); else show(which); });
        // moving the cursor over a command shows its page; A goes into it
        if (which != null) b.setOnFocusChangeListener((v, has) -> {
            if (has && !page.equals(which) && !(page.equals("mod") && which.equals("mods"))) { page = which; refresh(); }
        });
        b.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(which == null ? 42 : 34)));
        if (which != null) nav.put(which, b);
        return b;
    }

    private Button button(String label, View.OnClickListener l) {
        Button b = new Button(this);
        b.setText(label);
        b.setAllCaps(false);
        b.setTextSize(16);
        b.setTextColor(new android.content.res.ColorStateList(new int[][] { { -android.R.attr.state_enabled }, {} }, new int[] { 0xFF8088A0, Color.WHITE }));
        shadow(b);
        b.setStateListAnimator(null);
        StateListDrawable sl = new StateListDrawable();
        sl.addState(new int[] { android.R.attr.state_focused }, window(HOT_TOP, HOT_BOTTOM, 6));
        sl.addState(new int[] { android.R.attr.state_pressed }, window(HOT_TOP, HOT_BOTTOM, 6));
        sl.addState(new int[] {}, window(BTN_TOP, BTN_BOTTOM, 6));
        b.setBackground(sl);
        b.setPadding(dp(16), 0, dp(16), 0);
        b.setMinHeight(dp(40));
        b.setMinimumHeight(dp(40));
        if (l != null) b.setOnClickListener(l);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(42));
        lp.setMarginEnd(dp(10));
        b.setLayoutParams(lp);
        return b;
    }

    /** A line in a list (mods, save states, a scale setting): full width, lit under the cursor. */
    private Button listRow(CharSequence label, View.OnClickListener l) {
        Button b = new Button(this);
        b.setText(label);
        b.setAllCaps(false);
        b.setTextSize(16);
        b.setTextColor(TEXT);
        shadow(b);
        b.setGravity(Gravity.CENTER_VERTICAL | Gravity.START);
        b.setStateListAnimator(null);
        b.setBackground(hot(6));
        b.setPadding(dp(10), 0, dp(10), 0);
        b.setMinHeight(dp(38));
        b.setMinimumHeight(dp(38));
        if (l != null) b.setOnClickListener(l);
        b.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return b;
    }

    /** Checkboxes and radio buttons in the window colours; the focused one lit like a button. */
    private void theme(View v) {
        if (v instanceof CheckBox || v instanceof RadioButton) {
            CompoundButton c = (CompoundButton) v;
            c.setButtonTintList(new android.content.res.ColorStateList(
                    new int[][] { { android.R.attr.state_checked }, {} }, new int[] { ACCENT, BORDER }));
            c.setTextColor(TEXT);
            c.setTextSize(16);
            shadow(c);
            c.setBackground(hot(6));
            c.setPadding(dp(4), dp(4), dp(8), dp(4));
            c.setFocusable(true);
        } else if (v instanceof ViewGroup) {
            ViewGroup g = (ViewGroup) v;
            for (int i = 0; i < g.getChildCount(); i++) theme(g.getChildAt(i));
        }
    }

    private AlertDialog styled(AlertDialog d) {
        if (d.getWindow() != null) d.getWindow().setBackgroundDrawable(new android.graphics.drawable.InsetDrawable(window(WIN_TOP, WIN_BOTTOM, 10), dp(16)));
        for (int which : new int[] { AlertDialog.BUTTON_POSITIVE, AlertDialog.BUTTON_NEGATIVE }) {
            Button b = d.getButton(which);
            if (b != null) b.setTextColor(ACCENT);
        }
        return d;
    }
}
