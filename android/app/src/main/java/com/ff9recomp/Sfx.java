package com.ff9recomp;

import android.content.Context;
import android.media.AudioAttributes;
import android.media.SoundPool;

import java.io.File;

/**
 * The launcher's menu sounds, the game's own (FF7Recomp's idea): the engine writes FF9's cursor,
 * cancel and buzzer effects from the player's copy into files/ui-sounds on the first game run
 * (FF9Engine Core/UiSounds.cs). Nothing until then; Settings > Menu sounds turns them off.
 */
final class Sfx {
    static final int CURSOR = 103, CANCEL = 101, BUZZER = 102;
    private static SoundPool pool;
    private static final java.util.Map<Integer, Integer> ids = new java.util.HashMap<>();

    private Sfx() {}

    static File dir(Context c) { return new File(new GameInstall(c).files, "ui-sounds"); }

    static void load(Context c) {
        if (pool == null)
            pool = new SoundPool.Builder().setMaxStreams(3).setAudioAttributes(new AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_GAME).setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build()).build();
        for (int id : new int[] { CURSOR, CANCEL, BUZZER }) {
            if (ids.containsKey(id)) continue;
            File f = new File(dir(c), id + ".ogg");
            if (f.isFile()) ids.put(id, pool.load(f.getPath(), 1));
        }
    }

    static void play(Context c, int id) {
        if (!Settings.prefs(c).getBoolean("menu_sounds", true)) return;
        Integer s = ids.get(id);
        if (pool != null && s != null) pool.play(s, 0.8f, 0.8f, 1, 0, 1f);
    }
}
