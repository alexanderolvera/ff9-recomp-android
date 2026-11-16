package com.ff9recomp;

import android.content.Context;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

/**
 * Save states for FF9 (FF7Recomp's idea, a different mechanism): Memoria saves the game each
 * time the party enters a new place (its autosave). A slot keeps a copy of that autosave and the
 * place's name; resuming a slot puts the copy back and starts the game with Memoria's
 * FF9_AUTOLOAD, which loads it from the title screen. So a slot resumes at the start of the place
 * the party was in, not at the exact moment (Mono's memory cannot be snapshotted the way FF7's
 * can). In game: Select+R1 keeps the current place in the next slot.
 */
final class SaveStates {
    static final int SLOTS = 10;
    static final String AUTOSAVE = "SavedData_ww_Memoria_Autosave.dat";

    private SaveStates() {}

    static File dir(Context c) { return new File(new GameInstall(c).files, "savestates"); }
    static File autosave(Context c) { return new File(Reports.savesDir(c), AUTOSAVE); }
    static File data(Context c, int slot) { return new File(dir(c), "slot" + slot + ".dat"); }
    private static File info(Context c, int slot) { return new File(dir(c), "slot" + slot + ".txt"); }

    static boolean used(Context c, int slot) { return data(c, slot).isFile(); }

    /** "Prima Vista/Meeting Rm, 14 Nov 18:02", or null for an empty slot. */
    static String describe(Context c, int slot) {
        if (!used(c, slot)) return null;
        String place = "";
        try { place = new String(Files.readAllBytes(info(c, slot).toPath()), StandardCharsets.UTF_8).trim(); } catch (IOException ignored) { }
        String when = new java.text.SimpleDateFormat("d MMM HH:mm", java.util.Locale.US).format(new java.util.Date(data(c, slot).lastModified()));
        return (place.isEmpty() ? "Saved" : place) + ", " + when;
    }

    /** Copies the current autosave into a slot: the first empty one, else the oldest. Returns the slot. */
    static int keep(Context c, String place) throws IOException {
        File src = autosave(c);
        if (!src.isFile()) throw new IOException("no autosave yet (Memoria writes one when the party enters a place)");
        int slot = -1;
        long oldest = Long.MAX_VALUE;
        for (int i = 1; i <= SLOTS; i++) {
            if (!used(c, i)) { slot = i; break; }
            long t = data(c, i).lastModified();
            if (t < oldest) { oldest = t; slot = i; }
        }
        dir(c).mkdirs();
        copy(src, data(c, slot));
        Files.write(info(c, slot).toPath(), (place == null ? "" : place).getBytes(StandardCharsets.UTF_8));
        return slot;
    }

    /** Puts a slot back as Memoria's autosave (the game then loads it from the title: FF9_AUTOLOAD). */
    static void restore(Context c, int slot) throws IOException {
        File to = autosave(c);
        to.getParentFile().mkdirs();
        copy(data(c, slot), to);
    }

    static void delete(Context c, int slot) {
        data(c, slot).delete();
        info(c, slot).delete();
    }

    private static void copy(File from, File to) throws IOException {
        File tmp = new File(to.getPath() + ".tmp");
        try (InputStream in = new FileInputStream(from); OutputStream o = new FileOutputStream(tmp)) {
            byte[] b = new byte[1 << 16];
            for (int n; (n = in.read(b)) > 0; ) o.write(b, 0, n);
        }
        if (!tmp.renameTo(to)) { to.delete(); if (!tmp.renameTo(to)) throw new IOException("cannot write " + to.getName()); }
    }
}
