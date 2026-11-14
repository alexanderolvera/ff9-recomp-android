package com.ff9recomp;

import android.content.ContentResolver;
import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.provider.DocumentsContract;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;

/**
 * Files on the device: the game in app storage (files/game), imported once from the folder the
 * player picks; the runtime (.NET class libraries, FF9Engine.dll) extracted from the APK into
 * files/runtime.
 */
final class GameInstall {
    interface Progress { void update(String what); }

    final Context ctx;
    final File files, game, runtime;

    GameInstall(Context ctx) {
        this.ctx = ctx;
        File ext = ctx.getExternalFilesDir(null);
        files = ext != null ? ext : ctx.getFilesDir();
        game = new File(files, "game");
        runtime = new File(files, "runtime");
    }

    boolean hasGame() {
        return new File(game, "x64/FF9_Data/mainData").isFile();
    }

    boolean hasMoguri() { return moguriFolder() != null; }

    /** The Moguri Mod folder (Memoria's mod manager installs it as "MoguriMain"). */
    String moguriFolder() {
        if (new File(game, "MoguriMain").isDirectory()) return "MoguriMain";
        File[] l = game.listFiles();
        if (l != null) for (File f : l) if (f.isDirectory() && f.getName().toLowerCase().startsWith("moguri")) return f.getName();
        return null;
    }

    static final String[] REQUIRED = {
        "x64/FF9_Data/mainData", "x64/FF9_Data/resources.assets", "x64/FF9_Data/Managed/Assembly-CSharp.dll",
        "x64/FF9_Data/Managed/Memoria.Prime.dll", "StreamingAssets/p0data11.bin", "StreamingAssets/p0data5.bin", "Memoria.ini",
    };

    String missingMessage() {
        List<String> m = new ArrayList<>();
        for (String r : REQUIRED) { File f = new File(game, r); if (!f.isFile() || f.length() == 0) m.add(r); }
        if (m.isEmpty()) return null;
        if (m.contains("x64/FF9_Data/Managed/Memoria.Prime.dll"))
            return "Memoria isn't installed in the imported copy. Install Memoria on the PC (and Moguri Mod if you want it), then import the folder again.";
        return "Files missing from the imported copy: " + String.join(", ", m);
    }

    // ---------------------------------------------------------------- import from a folder
    void importFrom(Uri tree, Progress p) throws IOException {
        ContentResolver cr = ctx.getContentResolver();
        // the game may be anywhere below the picked folder (Steam's common/, a copy in Downloads,
        // a folder named differently): find the one that holds StreamingAssets and x64
        p.update("Looking for FINAL FANTASY IX in the picked folder...");
        String rootId = findGameRoot(cr, tree, DocumentsContract.getTreeDocumentId(tree), 0);
        if (rootId == null)
            throw new IOException("this folder doesn't hold FINAL FANTASY IX (no StreamingAssets and x64 folders found in it or below it). "
                    + "Pick the game's folder, the one with FF9_Launcher.exe in it.");
        game.mkdirs();
        long[] count = {0, 0};
        copyTree(cr, tree, rootId, game, "", p, count);
        configureMemoria();
        p.update("Imported " + count[0] + " files (" + (count[1] >> 20) + " MB).");
    }

    /** The document id of the folder (this one or up to 3 levels below) holding StreamingAssets and x64, or null. */
    private String findGameRoot(ContentResolver cr, Uri tree, String docId, int depth) {
        List<String[]> dirs = new ArrayList<>();
        boolean sa = false, x64 = false;
        try (Cursor c = cr.query(DocumentsContract.buildChildDocumentsUriUsingTree(tree, docId), new String[] {
                DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                DocumentsContract.Document.COLUMN_MIME_TYPE }, null, null, null)) {
            if (c == null) return null;
            while (c.moveToNext()) {
                if (!DocumentsContract.Document.MIME_TYPE_DIR.equals(c.getString(2))) continue;
                String name = c.getString(1);
                if (name.equalsIgnoreCase("StreamingAssets")) sa = true;
                else if (name.equalsIgnoreCase("x64")) x64 = true;
                else dirs.add(new String[] { c.getString(0), name });
            }
        } catch (Exception e) {
            return null;
        }
        if (sa && x64) return docId;
        if (depth >= 3) return null;
        // likely names first (the Steam folder's own name), then the rest
        dirs.sort((a, b) -> Boolean.compare(!a[1].toLowerCase().contains("fantasy"), !b[1].toLowerCase().contains("fantasy")));
        for (String[] d : dirs) {
            String r = findGameRoot(cr, tree, d[0], depth + 1);
            if (r != null) return r;
        }
        return null;
    }

    private void copyTree(ContentResolver cr, Uri tree, String docId, File dst, String rel, Progress p, long[] count) throws IOException {
        Uri children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, docId);
        try (Cursor c = cr.query(children, new String[] {
                DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                DocumentsContract.Document.COLUMN_MIME_TYPE, DocumentsContract.Document.COLUMN_SIZE }, null, null, null)) {
            if (c == null) return;
            while (c.moveToNext()) {
                String id = c.getString(0), name = c.getString(1), mime = c.getString(2);
                long size = c.getLong(3);
                String r = rel.isEmpty() ? name : rel + "/" + name;
                // Windows-only parts of the install are not needed on Android, except the battle
                // effects plugin, which the app runs in its x86-64 interpreter
                if (r.equalsIgnoreCase("x86") || r.toLowerCase().endsWith(".exe") || r.equalsIgnoreCase("x64/FF9_Data/Mono")) continue;
                if (rel.equalsIgnoreCase("x64/FF9_Data/Plugins") && !name.equalsIgnoreCase("FF9SpecialEffectPlugin.dll")) continue;
                File out = new File(dst, name);
                if (DocumentsContract.Document.MIME_TYPE_DIR.equals(mime)) {
                    out.mkdirs();
                    copyTree(cr, tree, id, out, r, p, count);
                } else {
                    if (out.isFile() && out.length() == size) continue;    // resumable
                    p.update("Copying " + r);
                    try (InputStream in = cr.openInputStream(DocumentsContract.buildDocumentUriUsingTree(tree, id));
                         OutputStream o = new FileOutputStream(out)) {
                        byte[] buf = new byte[1 << 20];
                        int n;
                        while ((n = in.read(buf)) > 0) o.write(buf, 0, n);
                    }
                    count[0]++;
                    count[1] += size;
                }
            }
        }
    }

    /** Memoria settings the Android build needs: the SoLoud audio backend. */
    void configureMemoria() throws IOException {
        File ini = new File(game, "Memoria.ini");
        if (!ini.isFile()) return;
        // byte for byte: the file is not valid UTF-8, and a UTF-8 round trip would damage it
        String s = new String(Files.readAllBytes(ini.toPath()), StandardCharsets.ISO_8859_1);
        s = s.replaceAll("(?m)^Backend\\s*=\\s*\\d+", "Backend = 2");
        // a Moguri Mod folder copied in but not enabled: enable it as Memoria's mod manager does
        String moguri = moguriFolder();
        if (moguri != null && java.util.regex.Pattern.compile("(?m)^FolderNames\\s*=\\s*$").matcher(s).find()) {
            s = s.replaceFirst("(?m)^FolderNames\\s*=\\s*$", "FolderNames = \"" + moguri + "\"");
            s = s.replaceFirst("(?m)^TileSize\\s*=\\s*32\\s*$", "TileSize = 64");
        }
        Files.write(ini.toPath(), s.getBytes(StandardCharsets.ISO_8859_1));
    }

    // ---------------------------------------------------------------- runtime from the APK
    void extractRuntime() throws IOException {
        File stamp = new File(runtime, ".version");
        String ver = String.valueOf(ctx.getPackageManager().getPackageArchiveInfo(ctx.getPackageCodePath(), 0) != null
                ? new File(ctx.getPackageCodePath()).lastModified() : 0);
        if (stamp.isFile() && new String(Files.readAllBytes(stamp.toPath()), StandardCharsets.UTF_8).equals(ver)) return;
        runtime.mkdirs();
        try (ZipInputStream z = new ZipInputStream(ctx.getAssets().open("runtime.zip"))) {
            ZipEntry e;
            byte[] buf = new byte[1 << 16];
            while ((e = z.getNextEntry()) != null) {
                File out = new File(runtime, e.getName());
                if (e.isDirectory()) { out.mkdirs(); continue; }
                out.getParentFile().mkdirs();
                try (OutputStream o = new FileOutputStream(out)) { int n; while ((n = z.read(buf)) > 0) o.write(buf, 0, n); }
            }
        }
        Files.write(stamp.toPath(), ver.getBytes(StandardCharsets.UTF_8));
    }
}
