package com.ff9recomp;

import android.content.Context;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Achievements the game unlocks on this device: the host's Steam wrapper stand-in writes each
 * one's Steam API name to files/achievements.txt (src/host/win32.c, FF9_ACHIEVEMENTS). The launcher
 * lists them; SteamLink sends them to the player's Steam account when they have signed in.
 */
final class Achievements {
    private Achievements() {}

    static File file(Context c) { return new File(new GameInstall(c).files, "achievements.txt"); }

    static List<String> unlocked(Context c) {
        List<String> out = new ArrayList<>();
        try {
            for (String l : Files.readAllLines(file(c).toPath(), StandardCharsets.UTF_8)) {
                l = l.trim();
                if (!l.isEmpty() && !out.contains(l)) out.add(l);
            }
        } catch (Exception ignored) { }
        return out;
    }

    /** "FF9_ACH_THE_PLAN" -> "The plan": the API name made readable (no game text is shipped). */
    static String pretty(String id) {
        String s = id.replaceFirst("(?i)^(ff9_)?(ach(ievement)?_)?", "").replace('_', ' ').trim().toLowerCase(Locale.ROOT);
        return s.isEmpty() ? id : Character.toUpperCase(s.charAt(0)) + s.substring(1);
    }

    static String steamNote(Context c) {
        SteamLink s = new SteamLink(c);
        if (!s.active()) return "not signed in (Settings > Steam)";
        int w = s.waiting();
        return "signed in as " + s.accountName() + (w > 0 ? ", " + w + " waiting to be sent" : ", all sent");
    }
}
