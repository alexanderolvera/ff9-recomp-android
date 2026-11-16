package com.ff9recomp;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;

/**
 * The launcher's download queue (FF7Recomp's): catalog mods are installed one at a time, in the
 * order they were added, on a worker thread, while the launcher stays usable. One queue per
 * process. Waiting and running items are kept in files/mods/queue.txt (one folder name per
 * line), so a queue cut short carries on the next time the launcher opens; the running
 * download resumes from its partial file.
 */
final class DownloadQueue {
    private static final String TAG = "FF9Mods";

    enum State { QUEUED, RUNNING, DONE, FAILED, CANCELLED }

    static final class Item {
        final ModCatalog.Mod mod;
        final boolean update;
        volatile State state = State.QUEUED;
        volatile String phase = "Waiting";
        volatile long got, total;
        volatile String error;
        Item(ModCatalog.Mod mod, boolean update) { this.mod = mod; this.update = update; }

        /** 0..100, or -1 when the size is unknown. */
        int percent() { return total > 0 ? (int) Math.min(100, got * 100 / total) : -1; }
    }

    private static DownloadQueue instance;

    static synchronized DownloadQueue get(Context c) {
        if (instance == null) {
            instance = new DownloadQueue(c.getApplicationContext());
            instance.restore();
        }
        return instance;
    }

    private final Context ctx;
    private final File saveFile;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final List<Item> items = new ArrayList<>();      // guarded by this
    private Thread worker;
    private Runnable listener;                               // main thread only
    private boolean notifyPosted;

    private DownloadQueue(Context c) {
        ctx = c;
        saveFile = new File(ModCatalog.dir(c), "queue.txt");
    }

    private void restore() {
        java.util.Set<String> keep = new java.util.HashSet<>();
        try {
            if (saveFile.isFile())
                for (String line : new String(Files.readAllBytes(saveFile.toPath()), StandardCharsets.UTF_8).split("\n")) {
                    String folder = line.trim();
                    if (folder.isEmpty()) continue;
                    ModCatalog.Mod m = ModCatalog.byFolder(ctx, folder);
                    if (m == null) continue;
                    keep.add(ModDownloader.archive(ctx, m).getName());
                    add(m, ModCatalog.installed(ctx, m));
                }
        } catch (Exception e) {
            Log.w(TAG, "download queue unreadable: " + e);
        }
        // partial downloads nothing waits for any more (cancelled, then the launcher closed)
        File[] old = new File(ModCatalog.dir(ctx), "downloads").listFiles();
        if (old != null) for (File f : old) if (!keep.contains(f.getName()) && f.delete()) Log.i(TAG, "removed the unfinished download " + f.getName());
    }

    /** Main-thread callback run (coalesced) whenever an item changes. */
    void setListener(Runnable r) { listener = r; }

    synchronized List<Item> snapshot() { return new ArrayList<>(items); }

    /** The waiting or running item for this mod, or null. */
    synchronized Item pending(ModCatalog.Mod m) {
        for (Item it : items)
            if (it.mod.folder.equalsIgnoreCase(m.folder) && (it.state == State.QUEUED || it.state == State.RUNNING)) return it;
        return null;
    }

    synchronized int pendingCount() {
        int n = 0;
        for (Item it : items) if (it.state == State.QUEUED || it.state == State.RUNNING) n++;
        return n;
    }

    synchronized Item running() {
        for (Item it : items) if (it.state == State.RUNNING) return it;
        return null;
    }

    /** Queue a mod (no-op when it is already waiting or running). Returns true when added. */
    synchronized boolean add(ModCatalog.Mod m, boolean update) {
        if (pending(m) != null) return false;
        for (int i = items.size() - 1; i >= 0; i--) if (items.get(i).mod.folder.equalsIgnoreCase(m.folder)) items.remove(i);
        items.add(new Item(m, update));
        save();
        changed();
        if (worker == null) {
            worker = new Thread(this::work, "mod-downloads");
            worker.start();
        }
        return true;
    }

    /** Cancel a waiting item, or stop the running one. */
    synchronized void cancel(Item it) {
        if (it.state == State.QUEUED) { it.state = State.CANCELLED; it.phase = "Cancelled"; save(); changed(); }
        else if (it.state == State.RUNNING) ModDownloader.cancel();
    }

    /** Move a waiting item up (-1) or down (+1) among the waiting ones. */
    synchronized void move(Item it, int dir) {
        int i = items.indexOf(it), j = i + dir;
        if (i < 0 || j < 0 || j >= items.size() || it.state != State.QUEUED || items.get(j).state != State.QUEUED) return;
        items.set(i, items.get(j));
        items.set(j, it);
        save();
        changed();
    }

    synchronized void retry(Item it) {
        if (it.state == State.FAILED || it.state == State.CANCELLED) add(it.mod, it.update);
    }

    /** Drop finished, failed and cancelled entries from the list, and the partial downloads of the last two. */
    synchronized void clearFinished() {
        for (int i = items.size() - 1; i >= 0; i--) {
            State st = items.get(i).state;
            // a stopped or failed download's partial file goes too (it can be large)
            if (st == State.CANCELLED || st == State.FAILED) ModDownloader.archive(ctx, items.get(i).mod).delete();
            if (st != State.QUEUED && st != State.RUNNING) items.remove(i);
        }
        changed();
    }

    private Item next() {
        synchronized (this) {
            for (Item it : items) if (it.state == State.QUEUED) { it.state = State.RUNNING; it.phase = "Starting"; changed(); return it; }
            worker = null;          // the next add() starts a new worker
            return null;
        }
    }

    private void work() {
        Item it;
        while ((it = next()) != null) {
            final Item cur = it;
            ModCatalog.Mod m = cur.mod;
            if (ModCatalog.installed(ctx, m) && !ModCatalog.updatable(ctx, m) && !ModCatalog.installedVersion(ctx, m).isEmpty()) {
                finish(cur, State.DONE, null);
                continue;
            }
            // the game must not be reading the folder being replaced
            while (TestControl.gamePid(ctx) > 0) {
                cur.phase = "Waiting for the game to close";
                changed();
                try { Thread.sleep(2000); } catch (InterruptedException e) { return; }
            }
            try {
                ModDownloader.install(ctx, m, (phase, got, total) -> {
                    cur.phase = phase;
                    cur.got = got;
                    cur.total = total;
                    changed();
                });
                finish(cur, State.DONE, null);
            } catch (Throwable e) {        // incl. OutOfMemoryError: fail the item, never the app
                String msg = e.getMessage() != null ? e.getMessage() : String.valueOf(e);
                finish(cur, "cancelled".equals(msg) ? State.CANCELLED : State.FAILED, "cancelled".equals(msg) ? null : msg);
            }
        }
    }

    private void finish(Item it, State st, String error) {
        synchronized (this) {
            it.error = error;
            it.state = st;
            if (error != null) Log.w(TAG, it.mod.name + ": " + error);
            it.phase = st == State.DONE ? "Installed" : st == State.CANCELLED ? "Cancelled" : "Failed";
            save();
            changed();
        }
    }

    private void save() {
        StringBuilder b = new StringBuilder();
        for (Item it : items)
            if (it.state == State.QUEUED || it.state == State.RUNNING) b.append(it.mod.folder).append('\n');
        try {
            if (b.length() == 0) { saveFile.delete(); return; }
            saveFile.getParentFile().mkdirs();
            try (FileOutputStream o = new FileOutputStream(saveFile)) { o.write(b.toString().getBytes(StandardCharsets.UTF_8)); }
        } catch (Exception e) {
            Log.w(TAG, "cannot save the download queue: " + e);
        }
    }

    /** Tells the launcher, at most every 100 ms. */
    private void changed() {
        synchronized (this) {
            if (notifyPosted) return;
            notifyPosted = true;
        }
        main.postDelayed(() -> {
            synchronized (DownloadQueue.this) { notifyPosted = false; }
            if (listener != null) listener.run();
        }, 100);
    }
}
