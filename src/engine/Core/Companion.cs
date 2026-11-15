// State for the app's second-screen panels (AYN Thor's bottom screen, CompanionView.java): a small
// JSON snapshot, ten times a second, handed to the host (ff9_companion_set) for Java to poll.
// Read through reflection so the engine does not bind to Memoria's types at compile time.
using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace FF9Engine.Core;

public static class Companion {
    [DllImport("__Internal")] static extern void ff9_companion_set([MarshalAs(UnmanagedType.LPUTF8Str)] string json);

    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly bool enabled = Environment.GetEnvironmentVariable("FF9_COMPANION") != null;
    static float next;
    static bool failedOnce;

    static object? Get(object? o, string name) {
        if (o == null) return null;
        var t = o as Type ?? o.GetType();
        var target = o is Type ? null : o;
        return t.GetField(name, F)?.GetValue(target) ?? t.GetProperty(name, F)?.GetValue(target);
    }

    static long L(object? v) => v == null ? 0 : Convert.ToInt64(v);

    public static void Tick() {
        if (!enabled || Clock.Unscaled < next) return;
        next = Clock.Unscaled + 0.1f;
        try {
            ff9_companion_set(Build());
        } catch (Exception e) {
            if (!failedOnce) { failedOnce = true; Log.Warn("companion: " + e); }
        }
    }

    // ---- the world map on the panels: the game's own full map, saved from the player's copy
    // (EmbeddedAsset/UI/Sprites/world_map_full_*) into FF9_WORLDMAP_DIR, and where the party is
    // on it (WMUIData.MainCharacterPosition, as the game's mini-map uses it)
    static readonly string? mapDir = Environment.GetEnvironmentVariable("FF9_WORLDMAP_DIR");
    static readonly System.Collections.Generic.HashSet<string> exported = new();

    static void WorldMapInfo(StringBuilder b) {
        var ui = Type.GetType("WMUIData, Assembly-CSharp");
        if (ui == null) return;
        try {
            int active = Convert.ToInt32(Get(ui, "ActiveMapNo") ?? 1);
            string name = active == 1 ? "world_map_full_all" : "world_map_full_mistcontinent";
            b.Append(",\"wmap\":"); Str(b, name);
            if (Get(ui, "MainCharacterPosition") is UnityEngine.Vector2 pos) b.Append($",\"wx\":{pos.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"wy\":{pos.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            b.Append(",\"wrot\":").Append(Convert.ToSingle(Get(ui, "MainCharacterRotationY") ?? 0f).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (mapDir != null && exported.Add(name)) ExportMap(name);
        } catch (Exception e) { if (!failedOnce) { failedOnce = true; Log.Warn("companion world map: " + e.Message); } }
    }

    static void ExportMap(string name) {
        string outPath = System.IO.Path.Combine(mapDir!, name + ".png");
        if (System.IO.File.Exists(outPath)) return;
        var am = Type.GetType("AssetManager, Assembly-CSharp")!;
        var load = Array.Find(am.GetMethods(F), m => m.Name == "Load" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)!
            .MakeGenericMethod(typeof(UnityEngine.Sprite));
        var sprite = load.Invoke(null, new object[] { "EmbeddedAsset/UI/Sprites/" + name, false }) as UnityEngine.Sprite;
        var ns = Gfx.Sprites.Of(sprite);
        var tex = ns?.Texture == null ? null : Gfx.Textures.OfOrNull(ns.Texture);
        if (ns == null || tex?.Pixels == null) { Log.Warn($"world map {name}: no pixels"); return; }
        int x0 = (int)ns.TextureRect.x, y0 = (int)ns.TextureRect.y, w = (int)ns.TextureRect.width, h = (int)ns.TextureRect.height;
        if (w <= 0 || h <= 0) { x0 = 0; y0 = 0; w = tex.Width; h = tex.Height; }
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++) Buffer.BlockCopy(tex.Pixels, ((y0 + y) * tex.Width + x0) * 4, px, y * w * 4, w * 4);
        System.IO.Directory.CreateDirectory(mapDir!);
        Gfx.Png.Write(outPath, px, w, h, flipY: true);
        Log.Info($"world map {name} {w}x{h} -> {outPath}");
    }

    static void Str(StringBuilder b, string s) {
        b.Append('"');
        foreach (char c in s) {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < 0x20) b.Append(' ');
            else b.Append(c);
        }
        b.Append('"');
    }

    static string Build() {
        var b = new StringBuilder(512);
        b.Append('{');
        var ssType = typeof(FF9StateSystem);
        object? ff9 = Get(Get(ssType, "Common"), "FF9");
        var inst = typeof(PersistenSingleton<>).MakeGenericType(ssType).GetProperty("Instance")?.GetValue(null);
        int mode = (int)L(Get(inst, "mode"));
        object? party = Get(ff9, "party");
        bool ready = party != null && Get(party, "member") is Array mem && mem.Length > 0 && mem.GetValue(0) != null;
        string m = mode == 2 ? "battle" : mode == 3 ? "world" : mode == 1 || mode == 5 ? "field" : "other";
        b.Append("\"ready\":").Append(ready ? 1 : 0).Append(",\"mode\":"); Str(b, m);
        if (ff9 != null) {
            b.Append(",\"gil\":").Append(L(Get(party, "gil")));
            string loc = Get(ff9, "mapNameStr") as string ?? "";
            if (mode == 3) {
                // on the world map: the region's name, as Memoria's window title shows it
                try { loc = Type.GetType("ff9, Assembly-CSharp")?.GetMethod("w_worldLocationName", F)?.Invoke(null, null) as string ?? ""; } catch { loc = ""; }
            }
            b.Append(",\"loc\":"); Str(b, loc);
            b.Append(",\"map\":").Append(L(Get(ff9, "fldMapNo")));
        }
        // a movie playing (MBG): the panels dim
        bool cine = false;
        try {
            var mbg = typeof(MBG).GetProperty("Instance", F)?.GetValue(null) ?? typeof(MBG).GetField("instance", F)?.GetValue(null);
            // "played": set while a movie (FMV or a field's movie background) runs, cleared at Stop
            cine = mbg != null && Get(mbg, "played") is true && mode != 2;
        } catch { }
        b.Append(",\"cine\":").Append(cine ? 1 : 0);
        if (mode == 3) WorldMapInfo(b);
        // the party (outside battle) from the save data; in battle, from the battle units
        if (mode == 2) {
            object? battle = Get(Get(ssType, "Battle"), "FF9Battle");
            var en = battle?.GetType().GetMethod("EnumerateBattleUnits", F)?.Invoke(battle, null) as IEnumerable;
            var pl = new StringBuilder(); var em = new StringBuilder();
            if (en != null) foreach (var u in en) {
                bool isPlayer = Get(u, "IsPlayer") is true;
                var sb = isPlayer ? pl : em;
                if (sb.Length > 0) sb.Append(',');
                sb.Append("{\"name\":"); Str(sb, Get(u, "Name") as string ?? "");
                sb.Append(",\"hp\":").Append(L(Get(u, "CurrentHp"))).Append(",\"mhp\":").Append(L(Get(u, "MaximumHp")));
                if (isPlayer) {
                    sb.Append(",\"mp\":").Append(L(Get(u, "CurrentMp"))).Append(",\"mmp\":").Append(L(Get(u, "MaximumMp")));
                    sb.Append(",\"atb\":").Append(L(Get(u, "CurrentAtb"))).Append(",\"matb\":").Append(Math.Max(1, L(Get(u, "MaximumAtb"))));
                    sb.Append(",\"lv\":").Append(L(Get(u, "Level")));
                    sb.Append(",\"trance\":").Append(Get(u, "HasTrance") is true ? L(Get(u, "Trance")) : -1);
                } else sb.Append(",\"target\":").Append(Get(u, "IsTargetable") is true ? 1 : 0);
                sb.Append('}');
            }
            b.Append(",\"party\":[").Append(pl).Append("],\"enemies\":[").Append(em).Append(']');
        } else if (party != null && Get(party, "member") is Array members) {
            b.Append(",\"party\":[");
            bool first = true;
            foreach (var p in members) {
                if (p == null) continue;
                if (!first) b.Append(',');
                first = false;
                object? cur = Get(p, "cur"), max = Get(p, "max");
                b.Append("{\"name\":"); Str(b, Get(p, "Name") as string ?? "");
                b.Append(",\"lv\":").Append(L(Get(p, "level")));
                b.Append(",\"hp\":").Append(L(Get(cur, "hp"))).Append(",\"mhp\":").Append(L(Get(max, "hp")));
                b.Append(",\"mp\":").Append(L(Get(cur, "mp"))).Append(",\"mmp\":").Append(L(Get(max, "mp")));
                b.Append(",\"trance\":").Append(L(Get(p, "trance")));
                b.Append('}');
            }
            b.Append(']');
        }
        b.Append('}');
        return b.ToString();
    }
}
