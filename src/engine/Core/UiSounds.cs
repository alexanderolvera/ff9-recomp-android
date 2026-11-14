// The launcher's menu sounds come from the player's own game (no game audio ships in the app):
// once per run, when FF9_UI_SOUNDS names a folder that lacks them, the game's cursor and confirm
// effects (sound ids 101-103, resolved through Memoria's SoundMetaData) are written there as
// Ogg files, the stream an .akb file carries after its header.
using System;
using System.IO;
using System.Reflection;

namespace FF9Engine.Core;

public static class UiSounds {
    static readonly string? dir = Environment.GetEnvironmentVariable("FF9_UI_SOUNDS");
    static bool done;
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    public static void Tick() {
        if (done || dir == null || Clock.Frame < 600 || Clock.Frame % 60 != 0) return;
        try {
            if (Export()) done = true;
        } catch (Exception e) {
            done = true;
            Log.Warn("ui sounds: " + e.Message);
        }
    }

    static bool Export() {
        var index = typeof(SoundMetaData).GetField("SoundEffectIndex", F)?.GetValue(null) as System.Collections.Generic.Dictionary<int, string>;
        if (index == null || index.Count == 0) return false;     // not loaded yet
        Directory.CreateDirectory(dir!);
        foreach (int id in new[] { 101, 102, 103 }) {
            string outPath = Path.Combine(dir!, id + ".ogg");
            if (File.Exists(outPath) || !index.TryGetValue(id, out var res)) continue;
            var load = typeof(AssetManager).GetMethod("LoadBytes", F, new[] { typeof(string) });
            byte[]? akb = null;
            if (load != null) akb = load.Invoke(null, new object[] { "Sounds/" + res + ".akb" }) as byte[];
            else {
                var m2 = Array.Find(typeof(AssetManager).GetMethods(F), m => m.Name == "LoadBytes");
                var ps = m2?.GetParameters();
                if (m2 != null && ps != null) {
                    var args = new object?[ps.Length];
                    args[0] = "Sounds/" + res + ".akb";
                    for (int i = 1; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                    akb = m2.Invoke(null, args) as byte[];
                }
            }
            if (akb == null) continue;
            int at = IndexOf(akb, "OggS"u8.ToArray());
            if (at < 0) continue;
            File.WriteAllBytes(outPath, akb[at..]);
            Log.Info($"ui sound {id} ({res}) -> {outPath}");
        }
        return true;
    }

    static int IndexOf(byte[] b, byte[] pat) {
        for (int i = 0; i + pat.Length <= b.Length; i++) {
            int k = 0;
            while (k < pat.Length && b[i + k] == pat[k]) k++;
            if (k == pat.Length) return i;
        }
        return -1;
    }
}
