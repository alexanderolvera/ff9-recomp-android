package com.ff9recomp;

import android.util.Log;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.RandomAccessFile;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicLong;

import in.dragonbra.javasteam.enums.EDepotFileFlag;
import in.dragonbra.javasteam.enums.EResult;
import in.dragonbra.javasteam.steam.cdn.Server;
import in.dragonbra.javasteam.steam.handlers.steamapps.PICSProductInfo;
import in.dragonbra.javasteam.steam.handlers.steamapps.PICSRequest;
import in.dragonbra.javasteam.steam.handlers.steamapps.SteamApps;
import in.dragonbra.javasteam.steam.handlers.steamapps.callback.DepotKeyCallback;
import in.dragonbra.javasteam.steam.handlers.steamapps.callback.PICSProductInfoCallback;
import in.dragonbra.javasteam.steam.handlers.steamapps.callback.PICSTokensCallback;
import in.dragonbra.javasteam.steam.handlers.steamcontent.SteamContent;
import in.dragonbra.javasteam.types.ChunkData;
import in.dragonbra.javasteam.types.DepotManifest;
import in.dragonbra.javasteam.types.FileData;
import in.dragonbra.javasteam.types.KeyValue;
import kotlinx.coroutines.GlobalScope;
import kotlinx.coroutines.future.FutureKt;

/**
 * The game from Steam, over SteamLink's signed-in connection (FF7Recomp's downloader).
 *
 * FINAL FANTASY IX (app 377840): the files the folder import keeps (everything but the x86
 * build, programs, the Mono runtime and Windows plugins) come from the account's Windows
 * depots, each checked against Steam's SHA-1. Files already here and intact are kept, so a
 * download stopped half way goes on where it was, and running it again repairs damaged files.
 * Then Memoria (MemoriaInstall) goes on top; the files Memoria replaces are not taken from Steam.
 */
final class SteamFiles {
    private static final String TAG = "FF9SteamFiles";

    static {
        // JavaSteam decrypts depot chunks with the provider named "BC" and loads
        // org.bouncycastle's class for it. Android's own "BC" lacks AES since Android 9, so the
        // full BouncyCastle (bcprov) takes the name.
        try {
            java.security.Security.removeProvider("BC");
            java.security.Security.addProvider(new org.bouncycastle.jce.provider.BouncyCastleProvider());
        } catch (Throwable e) {
            Log.w(TAG, "BouncyCastle", e);
        }
    }

    interface Progress { void update(String what); }

    static final String NOT_OWNED = "this Steam account does not own FINAL FANTASY IX";

    private final SteamLink link;

    SteamFiles(SteamLink link) { this.link = link; }

    /** A file of the depot and where it goes under the game folder. */
    private static final class Want { FileData f; String to; int depot; }

    /**
     * Downloads the game into game, then installs Memoria. Returns null when done, else what
     * stopped it ("stopped" when cancel was set; what was done stays).
     */
    String downloadGame(GameInstall install, File game, Progress p, AtomicBoolean cancel) throws Exception {
        MemoriaInstall memoria = new MemoriaInstall(install);
        p.update("Downloading Memoria " + MemoriaInstall.VERSION);
        File exe;
        try { exe = memoria.fetch(p::update, cancel); }
        catch (InterruptedException e) { return "stopped"; }
        Set<String> memoriaFiles = MemoriaInstall.paths(exe);

        p.update("Connecting to Steam");
        try (SteamLink.Session s = link.open()) {
            int app = SteamLink.APP_FF9;
            if (!SteamLink.owns(s, app)) return NOT_OWNED;
            p.update("Reading the game's file list from Steam");
            KeyValue depots = appInfo(s, app).get("depots");
            SteamApps apps = s.client.getHandler(SteamApps.class);
            SteamContent content = s.client.getHandler(SteamContent.class);
            List<Server> servers = new ArrayList<>();
            for (Server sv : FutureKt.asCompletableFuture(content.getServersForSteamPipe(s.cell, 20, GlobalScope.INSTANCE)).get(30, TimeUnit.SECONDS))
                if ("CDN".equals(sv.getType()) || "SteamCache".equals(sv.getType())) servers.add(sv);
            if (servers.isEmpty()) throw new IOException("Steam named no download server");

            Map<Integer, byte[]> keys = new HashMap<>();
            Map<String, Want> wants = new LinkedHashMap<>();
            try (in.dragonbra.javasteam.steam.cdn.Client cdn = new in.dragonbra.javasteam.steam.cdn.Client(s.client)) {
                for (KeyValue d : depots.getChildren()) {
                    int id;
                    try { id = Integer.parseInt(d.getName()); } catch (NumberFormatException e) { continue; }
                    String os = d.get("config").get("oslist").getValue(), lang = d.get("config").get("language").getValue();
                    KeyValue pub = d.get("manifests").get("public");
                    String gid = pub.get("gid").getValue() != null ? pub.get("gid").getValue() : pub.getValue();
                    Log.i(TAG, "depot " + id + " os=" + os + " lang=" + lang + " manifest=" + gid);
                    if (os != null && !os.isEmpty() && !os.contains("windows")) continue;
                    if (lang != null && !lang.isEmpty() && !lang.equals("english")) continue;
                    if (gid == null) continue;                              // shared redistributables
                    long manifestId = Long.parseUnsignedLong(gid);
                    DepotKeyCallback k = apps.getDepotDecryptionKey(id, app).toFuture().get(30, TimeUnit.SECONDS);
                    if (k.getResult() != EResult.OK) { Log.w(TAG, "depot " + id + " key: " + k.getResult()); continue; }
                    long code = FutureKt.asCompletableFuture(content.getManifestRequestCode(id, app, manifestId, "public", null, GlobalScope.INSTANCE))
                            .get(30, TimeUnit.SECONDS);
                    DepotManifest m = null;
                    Exception last = null;
                    for (int t = 0; t < Math.min(4, servers.size()) && m == null; t++) {
                        try { m = cdn.downloadManifestFuture(id, manifestId, code, servers.get(t), k.getDepotKey()).get(120, TimeUnit.SECONDS); }
                        catch (Exception e) { last = e; }
                    }
                    if (m == null) throw last;
                    if (m.getFilenamesEncrypted()) m.decryptFilenames(k.getDepotKey());
                    keys.put(id, k.getDepotKey());
                    // the game folder inside the depot: the one holding x64/FF9_Data/mainData
                    String root = null;
                    for (FileData f : m.getFiles()) {
                        String l = f.getFileName().replace('\\', '/').toLowerCase(Locale.ROOT);
                        if (l.equals("x64/ff9_data/maindata")) { root = ""; break; }
                        if (l.endsWith("/x64/ff9_data/maindata")) root = l.substring(0, l.length() - "x64/ff9_data/maindata".length());
                    }
                    if (root == null) root = "";
                    int taken = 0;
                    for (FileData f : m.getFiles()) {
                        if (f.getFlags().contains(EDepotFileFlag.Directory)) continue;
                        String n = f.getFileName().replace('\\', '/');
                        if (!n.toLowerCase(Locale.ROOT).startsWith(root)) continue;
                        String rel = n.substring(root.length());
                        String low = rel.toLowerCase(Locale.ROOT);
                        if (rel.isEmpty() || GameInstall.skipped(rel) || memoriaFiles.contains(low) || wants.containsKey(low)) continue;
                        Want w = new Want();
                        w.f = f;
                        w.to = rel;
                        w.depot = id;
                        wants.put(low, w);
                        taken++;
                    }
                    Log.i(TAG, "depot " + id + ": " + m.getFiles().size() + " entries, root '" + root + "', " + taken + " taken");
                }
                if (!wants.containsKey("x64/ff9_data/maindata")) throw new IOException("Steam's copy of the game has no x64/FF9_Data/mainData");

                // what is already here and intact stays
                game.mkdirs();
                List<Want> todo = new ArrayList<>();
                long total = 0, have = 0, need = 0;
                int checked = 0;
                for (Want w : wants.values()) {
                    if (cancel.get()) return "stopped";
                    if ((++checked & 31) == 0) p.update("Checking the files already here: " + checked + " of " + wants.size());
                    File dst = new File(game, w.to);
                    total += w.f.getTotalSize();
                    if (dst.isFile() && dst.length() == w.f.getTotalSize() && Arrays.equals(sha1(dst), w.f.getFileHash())) {
                        have += w.f.getTotalSize();
                        continue;
                    }
                    need += w.f.getTotalSize() - (dst.isFile() ? dst.length() : 0);
                    todo.add(w);
                }
                Log.i(TAG, wants.size() + " files, " + todo.size() + " to download, " + (total - have) + " bytes");
                long free = game.getUsableSpace();
                if (need + (256L << 20) > free)
                    throw new IOException("not enough free space: the download needs " + gb(need + (256L << 20)) + " GB more and "
                            + gb(free) + " GB is free");

                AtomicLong done = new AtomicLong(have);
                AtomicInteger next = new AtomicInteger(), failed = new AtomicInteger();
                final List<String> errors = Collections.synchronizedList(new ArrayList<>());
                final long tot = total;
                ExecutorService pool = Executors.newFixedThreadPool(8);
                List<Future<?>> jobs = new ArrayList<>();
                for (Want w : todo) {
                    jobs.add(pool.submit(() -> {
                        if (cancel.get() || failed.get() > 0) return;
                        try {
                            fetch(cdn, servers, next, w.depot, keys.get(w.depot), w, new File(game, w.to), done, cancel);
                        } catch (Exception e) {
                            Log.e(TAG, "download " + w.to, e);
                            errors.add(w.to + ": " + SteamLink.message(e));
                            failed.incrementAndGet();
                        }
                    }));
                }
                pool.shutdown();
                while (!pool.awaitTermination(500, TimeUnit.MILLISECONDS)) {
                    if (tot > 0) JobService.percent = (int) (done.get() * 100 / tot);
                    p.update("Downloading from Steam: " + gb(done.get()) + " of " + gb(tot) + " GB");
                }
                if (cancel.get()) return "stopped";
                if (!errors.isEmpty()) throw new IOException(errors.get(0));
            }
        }
        p.update("Installing Memoria " + MemoriaInstall.VERSION);
        memoria.install(game, p::update);
        if (game.equals(install.game)) install.configureMemoria();      // the SoLoud backend
        else configureCopy(game);
        return null;
    }

    /** The Android settings for a test download outside the app's game folder. */
    private static void configureCopy(File game) throws IOException {
        File ini = new File(game, "Memoria.ini");
        if (!ini.isFile()) return;
        String t = new String(java.nio.file.Files.readAllBytes(ini.toPath()), java.nio.charset.StandardCharsets.ISO_8859_1);
        t = t.replaceAll("(?m)^Backend\\s*=\\s*\\d+", "Backend = 2");
        java.nio.file.Files.write(ini.toPath(), t.getBytes(java.nio.charset.StandardCharsets.ISO_8859_1));
    }

    private static String gb(long b) { return String.format(Locale.ROOT, "%.2f", b / 1e9); }

    /** One file: its chunks, each from the next server (another on failure), then its SHA-1. */
    private static void fetch(in.dragonbra.javasteam.steam.cdn.Client cdn, List<Server> servers, AtomicInteger next, int depot, byte[] key,
                              Want w, File dst, AtomicLong done, AtomicBoolean cancel) throws Exception {
        File part = new File(dst.getPath() + ".part");
        dst.getParentFile().mkdirs();
        long got = 0;
        try (RandomAccessFile raf = new RandomAccessFile(part, "rw")) {
            raf.setLength(w.f.getTotalSize());
            for (ChunkData c : w.f.getChunks()) {
                if (cancel.get()) return;
                // JavaSteam 1.8.0 wants room for the compressed chunk when a depot key is given
                // (a small file that does not compress is bigger compressed)
                byte[] buf = new byte[Math.max(c.getUncompressedLength(), c.getCompressedLength())];
                int n = -1;
                Exception last = null;
                for (int t = 0; t < 6 && n < 0; t++) {
                    Server sv = servers.get(Math.floorMod(next.getAndIncrement(), servers.size()));
                    try { n = cdn.downloadDepotChunkFuture(depot, c, sv, buf, key).get(120, TimeUnit.SECONDS); }
                    catch (Exception e) { last = e; Thread.sleep(500L * (t + 1)); }
                }
                if (n < 0) throw last;
                raf.seek(c.getOffset());
                raf.write(buf, 0, n);
                done.addAndGet(n);
                got += n;
            }
        } catch (Exception e) {
            done.addAndGet(-got);
            throw e;
        }
        if (cancel.get()) return;
        if (!Arrays.equals(sha1(part), w.f.getFileHash())) {
            part.delete();
            done.addAndGet(-got);
            throw new IOException("the downloaded file did not match Steam's checksum");
        }
        if (dst.exists() && !dst.delete()) throw new IOException("cannot replace " + dst);
        if (!part.renameTo(dst)) throw new IOException("cannot write " + dst);
    }

    static KeyValue appInfo(SteamLink.Session s, int app) throws Exception {
        SteamApps apps = s.client.getHandler(SteamApps.class);
        PICSTokensCallback t = apps.picsGetAccessTokens(app, null).toFuture().get(30, TimeUnit.SECONDS);
        Long token = t.getAppTokens().get(app);
        PICSRequest req = new PICSRequest(app, token != null ? token : 0L);
        List<PICSProductInfoCallback> res = apps.picsGetProductInfo(Collections.singletonList(req), Collections.emptyList())
                .toFuture().get(60, TimeUnit.SECONDS).getResults();
        for (PICSProductInfoCallback cb : res) {
            PICSProductInfo pi = cb.getApps().get(app);
            if (pi != null) return pi.getKeyValues();
        }
        throw new IOException("Steam returned no details for app " + app);
    }

    static byte[] sha1(File f) throws IOException {
        try (InputStream in = new FileInputStream(f)) {
            MessageDigest md = MessageDigest.getInstance("SHA-1");
            byte[] buf = new byte[1 << 20];
            for (int n; (n = in.read(buf)) > 0; ) md.update(buf, 0, n);
            return md.digest();
        } catch (java.security.NoSuchAlgorithmException e) {
            throw new IOException(e);
        }
    }
}
