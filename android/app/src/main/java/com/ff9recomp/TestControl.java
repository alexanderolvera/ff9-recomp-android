package com.ff9recomp;

import android.app.ActivityManager;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Bundle;
import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.util.HashMap;
import java.util.Map;

/**
 * A test API over adb (ported from FF7Recomp). Only adb can call it: the receiver requires
 * android.permission.DUMP, which the shell holds and apps cannot get. The answer (JSON) is the
 * broadcast's result data, which `am broadcast` prints.
 *
 *   am broadcast -a com.ff9recomp.test.GET    -n com.ff9recomp/.TestControl
 *   am broadcast -a com.ff9recomp.test.SET    -n com.ff9recomp/.TestControl --ez widescreen true --ei speed 2
 *   am broadcast -a com.ff9recomp.test.STOP   -n com.ff9recomp/.TestControl
 *   am broadcast -a com.ff9recomp.test.REPORT -n com.ff9recomp/.TestControl [--ez saves true] [--ez state true]
 *   am start -n com.ff9recomp/.LauncherActivity -f 0x24000000 --ez test_play true
 *   am broadcast -a com.ff9recomp.test.PAD -p com.ff9recomp --ei buttons 0x1000 --ei ms 100   (GameActivity)
 *
 * SET takes typed extras (--ei / --ez / --es); unknown names are refused.
 */
public class TestControl extends BroadcastReceiver {
    static final String PREFIX = "com.ff9recomp.test.";
    static final String EXTRA_PLAY = "test_play";

    /** The launcher's settings and their types (Settings.java applies them to Memoria.ini). */
    static final Map<String, Class<?>> TYPES = new HashMap<>();
    static {
        for (String k : Settings.INTS) TYPES.put(k, Integer.class);
        for (String k : Settings.BOOLS) TYPES.put(k, Boolean.class);
        TYPES.put("guide_url", String.class);
    }

    @Override
    public void onReceive(Context ctx, Intent intent) {
        String a = intent.getAction();
        if (a == null || !a.startsWith(PREFIX)) return;
        SharedPreferences prefs = ctx.getSharedPreferences("launcher", Context.MODE_PRIVATE);
        JSONObject out = new JSONObject();
        try {
            switch (a.substring(PREFIX.length())) {
            case "GET":
                break;
            case "SET": {
                Bundle b = intent.getExtras();
                SharedPreferences.Editor e = prefs.edit();
                JSONObject changed = new JSONObject();
                for (String k : b == null ? new java.util.HashSet<String>() : b.keySet()) {
                    Class<?> t = TYPES.get(k);
                    Object v = b.get(k);
                    if (t == null) { out.put("error", "unknown setting " + k); continue; }
                    if (t == Integer.class) { int i = v instanceof Number ? ((Number) v).intValue() : Integer.parseInt(String.valueOf(v).trim()); e.putInt(k, i); changed.put(k, i); }
                    else if (t == String.class) { String str = v == null ? "" : String.valueOf(v); e.putString(k, str); changed.put(k, str); }
                    else { boolean z = v instanceof Boolean ? (Boolean) v : Boolean.parseBoolean(String.valueOf(v).trim()); e.putBoolean(k, z); changed.put(k, z); }
                }
                e.commit();
                out.put("set", changed);
                break;
            }
            case "STOP": {
                int pid = gamePid(ctx);
                if (pid > 0) android.os.Process.killProcess(pid);
                out.put("stopped", pid > 0);
                break;
            }
            case "REPORT": {
                File zip = Reports.build(ctx, intent.getBooleanExtra("saves", false), intent.getBooleanExtra("state", false));
                File to = new File(new GameInstall(ctx).files, "report-test.zip");
                try (java.io.InputStream in = new java.io.FileInputStream(zip); java.io.OutputStream o = new java.io.FileOutputStream(to)) {
                    byte[] buf = new byte[1 << 16];
                    for (int n; (n = in.read(buf)) > 0; ) o.write(buf, 0, n);
                }
                JSONArray names = new JSONArray();
                try (java.util.zip.ZipFile z = new java.util.zip.ZipFile(to)) {
                    for (java.util.Enumeration<? extends java.util.zip.ZipEntry> en = z.entries(); en.hasMoreElements(); ) {
                        java.util.zip.ZipEntry ze = en.nextElement();
                        names.put(ze.getName() + " " + ze.getSize());
                    }
                }
                out.put("report", to.getAbsolutePath());
                out.put("entries", names);
                break;
            }
            case "STEAM_DOWNLOAD": {
                // tests download into a scratch folder only, never the player's game:
                // --es op download|memoria|stop|status --es dir ABS (logcat tags FF9SteamFiles, FF9Memoria)
                String op = intent.getStringExtra("op") != null ? intent.getStringExtra("op") : "status";
                String dir = intent.getStringExtra("dir");
                GameInstall gi = new GameInstall(ctx);
                if (op.equals("download") || op.equals("memoria")) {
                    if (dir == null || !dir.startsWith("/")) { out.put("error", "dir: an absolute scratch folder"); break; }
                    File d = new File(dir).getCanonicalFile();
                    if (d.equals(gi.game.getCanonicalFile()) || d.getPath().startsWith(gi.game.getCanonicalPath() + "/")) {
                        out.put("error", "not the player's game folder"); break;
                    }
                    if (op.equals("download")) SteamFiles.start(ctx, d);
                    else if (!SteamFiles.busy) {
                        SteamFiles.busy = true;
                        SteamFiles.cancel.set(false);
                        new Thread(() -> {
                            try {
                                MemoriaInstall m = new MemoriaInstall(gi);
                                m.fetch(w -> SteamFiles.status = w, SteamFiles.cancel);
                                m.install(d, w -> SteamFiles.status = w);
                                SteamFiles.status = "memoria installed";
                            } catch (Throwable e) {
                                android.util.Log.e("FF9Memoria", "test install", e);
                                SteamFiles.status = "memoria failed: " + e;
                            }
                            SteamFiles.busy = false;
                        }).start();
                    }
                } else if (op.equals("stop")) SteamFiles.cancel.set(true);
                out.put("busy", SteamFiles.busy);
                out.put("status", SteamFiles.status);
                break;
            }
            default:
                out.put("error", "unknown action " + a);
            }
            JSONObject all = new JSONObject();
            for (Map.Entry<String, ?> en : prefs.getAll().entrySet())
                if (TYPES.containsKey(en.getKey())) all.put(en.getKey(), en.getValue());
            out.put("settings", all);
            out.put("game_running", gamePid(ctx) > 0);
        } catch (Exception ex) {
            try { out.put("error", ex.toString()); } catch (Exception ignored) { }
        }
        setResultCode(out.has("error") ? 1 : 0);
        setResultData(out.toString());
    }

    static boolean isSetting(String key) { return TYPES.containsKey(key); }

    /** The game's process (":game"), or 0. */
    static int gamePid(Context ctx) {
        ActivityManager am = (ActivityManager) ctx.getSystemService(Context.ACTIVITY_SERVICE);
        if (am.getRunningAppProcesses() == null) return 0;
        for (ActivityManager.RunningAppProcessInfo p : am.getRunningAppProcesses())
            if (p.processName.endsWith(":game")) return p.pid;
        return 0;
    }
}
