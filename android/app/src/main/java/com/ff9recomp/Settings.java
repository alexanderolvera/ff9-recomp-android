package com.ff9recomp;

import android.content.Context;
import android.content.SharedPreferences;

import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;

/**
 * The launcher's game settings. They live in the launcher's preferences (so a reset or replaced
 * Memoria.ini does not lose them) and are written into the imported game's Memoria.ini each time
 * the game starts; Memoria reads them from there. Only the keys below are touched: comments and
 * every other line of the file stay as they are.
 */
final class Settings {
    /** One setting: its preference key, the launcher's label and note, its choices, and what each writes. */
    static final class Option {
        final String key, label, note;
        final String[] choices;
        final int def;
        Option(String key, String label, String note, int def, String... choices) {
            this.key = key; this.label = label; this.note = note; this.def = def; this.choices = choices;
        }
        boolean isBool() { return choices.length == 0; }
    }

    static final Option[] OPTIONS = {
        new Option("fps", "Frame rate", "Original: fields 30, battles 15, as the game was made. Smooth: Memoria fills in frames.", 0,
                "Original", "Smooth 60", "Smooth 120"),
        new Option("widescreen", "Widescreen", "Fields and battles fill a 16:9 screen.", 1),
        new Option("skip_intros", "Skip logos", "Straight to the title screen.", 1),
        new Option("speed", "Speed-up booster", "How fast the game runs while the High Speed booster is on.", 1,
                "2x", "3x", "4x", "5x"),
        new Option("boosters", "Boosters", "The game's own boosters: High Speed, Battle Assistance, No Encounters and more (in-game menu).", 1),
        new Option("turbo_dialog", "Fast dialog", "Holding confirm speeds text along.", 1),
        new Option("swap_confirm", "Swap confirm and cancel", "B confirms and A cancels (Japanese layout).", 0),
        new Option("analog_run", "Analog walk and run", "Walk or run by how far the stick is pushed.", 1),
        new Option("music_volume", "Music volume", null, 4, "0%", "25%", "50%", "75%", "100%"),
        new Option("sound_volume", "Sound volume", null, 4, "0%", "25%", "50%", "75%", "100%"),
    };

    static final List<String> INTS = new ArrayList<>(), BOOLS = new ArrayList<>();
    static {
        for (Option o : OPTIONS) (o.isBool() ? BOOLS : INTS).add(o.key);
    }

    private Settings() {}

    static SharedPreferences prefs(Context c) { return c.getSharedPreferences("launcher", Context.MODE_PRIVATE); }

    static int get(Context c, Option o) {
        SharedPreferences p = prefs(c);
        if (o.isBool()) return p.getBoolean(o.key, o.def != 0) ? 1 : 0;
        return Math.max(0, Math.min(o.choices.length - 1, p.getInt(o.key, o.def)));
    }

    static void set(Context c, Option o, int v) {
        if (o.isBool()) prefs(c).edit().putBoolean(o.key, v != 0).apply();
        else prefs(c).edit().putInt(o.key, v).apply();
    }

    static String valueText(Context c, Option o) {
        int v = get(c, o);
        return o.isBool() ? (v != 0 ? "On" : "Off") : o.choices[v];
    }

    static Option find(String key) {
        for (Option o : OPTIONS) if (o.key.equals(key)) return o;
        return null;
    }

    /** Writes the settings into game/Memoria.ini (before the game starts). */
    static void apply(Context c, File game) throws IOException {
        File ini = new File(game, "Memoria.ini");
        if (!ini.isFile()) return;
        List<String> lines = new ArrayList<>(Files.readAllLines(ini.toPath(), StandardCharsets.ISO_8859_1));
        int fps = get(c, find("fps"));
        String f = fps == 0 ? "-1" : fps == 1 ? "60" : "120";
        put(lines, "Graphics", "BattleFPS", f);
        put(lines, "Graphics", "FieldFPS", f);
        put(lines, "Graphics", "WorldFPS", f);
        put(lines, "Graphics", "WidescreenSupport", on(c, "widescreen"));
        put(lines, "Graphics", "SkipIntros", get(c, find("skip_intros")) != 0 ? "3" : "0");
        put(lines, "Cheats", "Enabled", on(c, "boosters"));
        put(lines, "Cheats", "SpeedFactor", String.valueOf(get(c, find("speed")) + 2));
        put(lines, "Control", "TurboDialog", on(c, "turbo_dialog"));
        put(lines, "Control", "SwapConfirmCancel", on(c, "swap_confirm"));
        put(lines, "AnalogControl", "Enabled", on(c, "analog_run"));
        put(lines, "Audio", "MusicVolume", String.valueOf(get(c, find("music_volume")) * 25));
        put(lines, "Audio", "SoundVolume", String.valueOf(get(c, find("sound_volume")) * 25));
        Files.write(ini.toPath(), lines, StandardCharsets.ISO_8859_1);
    }

    private static String on(Context c, String key) { return get(c, find(key)) != 0 ? "1" : "0"; }

    /** Sets "Key = value" in [section] (the first such line), adding it at the section's end if absent. */
    static void put(List<String> lines, String section, String key, String value) {
        int start = -1, end = lines.size();
        for (int i = 0; i < lines.size(); i++) {
            String t = lines.get(i).trim();
            if (t.startsWith("[") && t.endsWith("]")) {
                if (start >= 0) { end = i; break; }
                if (t.substring(1, t.length() - 1).trim().equalsIgnoreCase(section)) start = i;
            }
        }
        if (start < 0) {
            lines.add("");
            lines.add("[" + section + "]");
            lines.add(key + " = " + value);
            return;
        }
        for (int i = start + 1; i < end; i++) {
            String t = lines.get(i).trim();
            if (t.startsWith(";") || t.startsWith("#")) continue;
            int eq = t.indexOf('=');
            if (eq > 0 && t.substring(0, eq).trim().equalsIgnoreCase(key)) {
                lines.set(i, key + " = " + value);
                return;
            }
        }
        int at = end;
        while (at > start + 1 && lines.get(at - 1).trim().isEmpty()) at--;
        lines.add(at, key + " = " + value);
    }
}
