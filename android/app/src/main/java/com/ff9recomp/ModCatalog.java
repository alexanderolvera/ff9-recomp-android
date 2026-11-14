package com.ff9recomp;

import android.content.Context;
import android.util.Xml;

import org.xmlpull.v1.XmlPullParser;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * Memoria's mod catalog (the list Memoria's own launcher shows): fetched from Memoria's GitHub,
 * kept in files/mods/catalog.xml for offline use. Mods are installed into the imported game's
 * folder (game/<InstallationPath>) and turned on by listing them in Memoria.ini's FolderNames,
 * highest priority first, as Memoria's launcher does. The FF7Recomp mod manager's idea, for
 * Memoria's catalog.
 */
final class ModCatalog {
    static final String URL_CATALOG = "https://raw.githubusercontent.com/Albeoris/Memoria/mod-catalog/MemoriaCatalog.xml";

    static final class Mod {
        String name = "", version = "", folder = "", author = "", description = "", category = "", url = "", website = "", format = "";
        int priority;
    }

    private ModCatalog() {}

    static File dir(Context c) { return new File(new GameInstall(c).files, "mods"); }
    static File cached(Context c) { return new File(dir(c), "catalog.xml"); }

    /** Downloads the catalog (on the caller's thread). */
    static void refresh(Context c) throws IOException {
        HttpURLConnection h = (HttpURLConnection) new URL(URL_CATALOG).openConnection();
        h.setRequestProperty("User-Agent", "FF9Recomp/" + Updater.installedVersion(c));
        h.setConnectTimeout(15000);
        h.setReadTimeout(30000);
        if (h.getResponseCode() != 200) throw new IOException("the catalog answered " + h.getResponseCode());
        dir(c).mkdirs();
        File tmp = new File(dir(c), "catalog.tmp");
        try (InputStream in = h.getInputStream(); OutputStream o = new FileOutputStream(tmp)) {
            byte[] b = new byte[1 << 16];
            for (int n; (n = in.read(b)) > 0; ) o.write(b, 0, n);
        }
        if (parse(tmp).isEmpty()) throw new IOException("the catalog came back empty");
        if (!tmp.renameTo(cached(c))) throw new IOException("cannot save the catalog");
    }

    /** The cached catalog's mods (empty when it was never fetched). */
    static List<Mod> mods(Context c) {
        try { return parse(cached(c)); } catch (Exception e) { return new ArrayList<>(); }
    }

    private static List<Mod> parse(File f) throws IOException {
        List<Mod> out = new ArrayList<>();
        if (!f.isFile()) return out;
        try (InputStream in = new FileInputStream(f)) {
            XmlPullParser p = Xml.newPullParser();
            p.setInput(in, "UTF-8");
            Mod m = null;
            int depth = 0, modDepth = -1;
            String tag = null;
            for (int ev = p.getEventType(); ev != XmlPullParser.END_DOCUMENT; ev = p.next()) {
                if (ev == XmlPullParser.START_TAG) {
                    depth++;
                    if (p.getName().equals("Mod") && m == null) { m = new Mod(); modDepth = depth; }
                    tag = p.getName();
                } else if (ev == XmlPullParser.END_TAG) {
                    if (m != null && depth == modDepth && p.getName().equals("Mod")) {
                        if (!m.folder.isEmpty() && !m.url.isEmpty()) out.add(m);
                        m = null;
                    }
                    depth--;
                    tag = null;
                } else if (ev == XmlPullParser.TEXT && m != null && tag != null && depth == modDepth + 1) {
                    String t = p.getText().trim();
                    switch (tag) {
                        case "Name": m.name = t; break;
                        case "Version": m.version = t; break;
                        case "InstallationPath": m.folder = t; break;
                        case "Author": m.author = t; break;
                        case "Description": m.description = t; break;
                        case "Category": m.category = t; break;
                        case "DownloadUrl": m.url = t; break;
                        case "Website": m.website = t; break;
                        case "DownloadFormat": m.format = t; break;
                        case "Priority": try { m.priority = Integer.parseInt(t); } catch (NumberFormatException ignored) { } break;
                    }
                }
            }
        } catch (org.xmlpull.v1.XmlPullParserException e) {
            throw new IOException("the catalog could not be read: " + e.getMessage());
        }
        return out;
    }

    // ------------------------------------------------------------------ installed mods

    static File folder(Context c, Mod m) { return new File(new GameInstall(c).game, m.folder); }
    static boolean installed(Context c, Mod m) { return folder(c, m).isDirectory(); }

    /** The version installed by this app (files/mods/<folder>.version), or "" when unknown. */
    static String installedVersion(Context c, Mod m) {
        try { return new String(Files.readAllBytes(new File(dir(c), safe(m.folder) + ".version").toPath()), StandardCharsets.UTF_8).trim(); }
        catch (IOException e) { return ""; }
    }

    static void setInstalledVersion(Context c, Mod m) throws IOException {
        dir(c).mkdirs();
        Files.write(new File(dir(c), safe(m.folder) + ".version").toPath(), m.version.getBytes(StandardCharsets.UTF_8));
    }

    static boolean updatable(Context c, Mod m) {
        String v = installedVersion(c, m);
        return installed(c, m) && !v.isEmpty() && Updater.compare(m.version, v) > 0;
    }

    private static String safe(String s) { return s.replaceAll("[^A-Za-z0-9._-]", "_"); }

    // ------------------------------------------------------------------ Memoria.ini FolderNames

    /** The mod folders Memoria loads (FolderNames), highest priority first. */
    static List<String> enabled(Context c) {
        List<String> out = new ArrayList<>();
        for (String line : iniLines(c)) {
            String t = line.trim();
            if (t.toLowerCase(java.util.Locale.ROOT).startsWith("foldernames")) {
                int eq = t.indexOf('=');
                if (eq < 0) break;
                for (String part : t.substring(eq + 1).split(",")) {
                    String f = part.trim();
                    if (f.startsWith("\"") && f.endsWith("\"") && f.length() >= 2) f = f.substring(1, f.length() - 1);
                    if (!f.isEmpty()) out.add(f);
                }
                break;
            }
        }
        return out;
    }

    static boolean isEnabled(Context c, Mod m) {
        for (String f : enabled(c)) if (f.equalsIgnoreCase(m.folder)) return true;
        return false;
    }

    /** Turns a mod on or off; the list is kept in catalog priority order (highest first). */
    static void setEnabled(Context c, Mod m, boolean on) throws IOException {
        List<String> folders = enabled(c);
        folders.removeIf(f -> f.equalsIgnoreCase(m.folder));
        if (on) folders.add(m.folder);
        List<Mod> all = mods(c);
        Collections.sort(folders, (a, b) -> Integer.compare(priorityOf(all, b), priorityOf(all, a)));
        StringBuilder names = new StringBuilder(), prios = new StringBuilder();
        for (String f : folders) {
            if (names.length() > 0) { names.append(", "); prios.append(", "); }
            names.append('"').append(f).append('"');
            prios.append('"').append(priorityOf(all, f)).append('"');
        }
        File ini = new File(new GameInstall(c).game, "Memoria.ini");
        List<String> lines = iniLines(c);
        Settings.put(lines, "Mod", "FolderNames", names.toString());
        Settings.put(lines, "Mod", "Priorities", prios.toString());
        Files.write(ini.toPath(), lines, StandardCharsets.ISO_8859_1);
    }

    private static int priorityOf(List<Mod> all, String folder) {
        for (Mod m : all) if (m.folder.equalsIgnoreCase(folder)) return m.priority;
        return 0;
    }

    private static List<String> iniLines(Context c) {
        File ini = new File(new GameInstall(c).game, "Memoria.ini");
        try { return new ArrayList<>(Files.readAllLines(ini.toPath(), StandardCharsets.ISO_8859_1)); }
        catch (IOException e) { return new ArrayList<>(); }
    }
}
