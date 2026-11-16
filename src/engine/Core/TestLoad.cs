// FF9_LOAD_FILE=frame:slot:file (tests): at that frame (on the title screen) load a save the way
// the Load Game screen does (FF9StateSystem.Serializer.Load, then SaveLoadUI's finish steps).
// slot and file are 0-based (the screen's "slot 2" / "FILE03" are 1 / 2). Reflection: the game's
// delegate and enum types are built against the old mscorlib.
using System;
using System.Linq.Expressions;
using System.Reflection;

namespace FF9Engine.Core;

public static class TestLoad {
    static readonly string? spec = Environment.GetEnvironmentVariable("FF9_LOAD_FILE");
    static bool done;
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    static Type T(string n) => typeof(FF9StateSystem).Assembly.GetType(n)!;

    // FF9_AUTOLOAD=1 (the launcher's "Play from here" and Start: Continue): Memoria's autosave,
    // loaded from the title screen once it is up. Memoria's own -memoria-autoload loads it while
    // the title skips its intro, then waits for a fade that the skip has taken over, so the
    // screen stayed black.
    static readonly bool autoload = Environment.GetEnvironmentVariable("FF9_AUTOLOAD") == "1";
    static bool autoloadDone;
    static long titleSince = -1;

    public static void Tick() {
        if (autoload && !autoloadDone) {
            if (Player.LastSceneLoaded != "Title") titleSince = -1;
            else if (titleSince < 0) titleSince = Clock.Frame;
            else if (Clock.Frame - titleSince >= 120) {
                autoloadDone = true;
                try { LoadAutosaveNow(); } catch (Exception e) { Log.Warn("autoload failed: " + e); }
            }
        }
        if (spec == null || done) return;
        var p = spec.Split(':');
        if (p.Length < 3 || !int.TryParse(p[0], out int at) || Clock.Frame < at) return;
        // on the title screen (the app reaches it later than the headless host)
        if (Player.LastSceneLoaded != "Title") return;
        done = true;
        int slot = int.Parse(p[1]), file = int.Parse(p[2]);
        Log.Info($"test load: slot {slot} file {file}");
        try {
            object ser = typeof(FF9StateSystem).GetProperty("Serializer", F)!.GetValue(null)!;
            var load = Array.Find(ser.GetType().GetMethods(F), m => m.Name == "Load" && m.GetParameters().Length == 4)!;
            var finishType = load.GetParameters()[3].ParameterType;
            var ps = Array.ConvertAll(finishType.GetMethod("Invoke")!.GetParameters(), q => Expression.Parameter(q.ParameterType, q.Name));
            var call = Expression.Call(typeof(TestLoad).GetMethod(nameof(Finished), F)!,
                Expression.Convert(ps[0], typeof(object)), ps[1], ps[2], ps[3]);
            var del = Expression.Lambda(finishType, call, ps).Compile();
            load.Invoke(ser, new object?[] { slot, file, null, del });
        } catch (Exception e) { Log.Warn("test load failed: " + e); }
    }

    /// The panels' Load: Memoria's autosave (a save state put there by the app) loaded now, in game.
    public static void LoadAutosaveNow() {
        object ser = typeof(FF9StateSystem).GetProperty("Serializer", F)!.GetValue(null)!;
        var load = Array.Find(ser.GetType().GetMethods(F), m => m.Name == "Autoload" && m.GetParameters().Length == 2)!;
        var finishType = load.GetParameters()[1].ParameterType;
        var ps = Array.ConvertAll(finishType.GetMethod("Invoke")!.GetParameters(), q => Expression.Parameter(q.ParameterType, q.Name));
        var call = Expression.Call(typeof(TestLoad).GetMethod(nameof(AutoloadFinished), F)!, Expression.Convert(ps[0], typeof(object)), ps[1]);
        load.Invoke(ser, new object?[] { null, Expression.Lambda(finishType, call, ps).Compile() });
        Log.Info("panel: loading the autosave");
    }

    static void AutoloadFinished(object err, bool ok) => Finished(err, 0, 0, ok);

    static void Finished(object err, int slot, int file, bool ok) {
        Log.Info($"test load finished: {err} ok={ok}");
        if (!ok || err.ToString() != "Success") return;
        try {
            T("SoundLib").GetMethod("StopMovieMusic", F)?.Invoke(null, new object[] { "FMV000", true });
            var settings = typeof(FF9StateSystem).GetProperty("Settings", F)!.GetValue(null)!;
            settings.GetType().GetMethod("SetSound", F, Type.EmptyTypes)?.Invoke(settings, null);
            settings.GetType().GetMethod("SetSoundEffect", F, Type.EmptyTypes)?.Invoke(settings, null);
            var ee = typeof(PersistenSingleton<>).MakeGenericType(T("EventEngine")).GetProperty("Instance")!.GetValue(null)!;
            ee.GetType().GetMethod("ReplaceLoadMap", F)!.Invoke(ee, null);
            var ui = typeof(PersistenSingleton<>).MakeGenericType(T("UIManager")).GetProperty("Instance")!.GetValue(null)!;
            var title = ui.GetType().GetProperty("TitleScene", F)?.GetValue(ui);
            title?.GetType().GetProperty("SplashScreenEnabled", F)?.SetValue(title, true);
            settings.GetType().GetMethod("UpdateSetting", F, Type.EmptyTypes)?.Invoke(settings, null);
            T("ff9").GetMethod("w_frameNewSession", F)?.Invoke(null, null);
        } catch (Exception e) { Log.Warn("test load finish failed: " + e); }
    }
}
