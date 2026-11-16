// Battles and the world map in 16:9 after a field with a narrow camera (GitHub issue 5).
// Memoria's PSXCameraAspect narrows the camera to 4:3 for the field cameras in
// NarrowMapList.RestrictedCams, keyed by FF9.fldMapNo and EventEngine.fieldmap.camIdx. In a battle
// both still name the field it began in, so the first battle (begun in the Prima Vista's cargo
// room, map 50 camera 1) was drawn 4:3. That FieldMap is left over from the field scene (the next
// field makes a new one: HonoluluFieldMain), so its camera number is cleared once a battle or the
// world map is up, and no field camera matches.
using System;
using System.Reflection;

namespace FF9Engine.Core;

public static class Widescreen {
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static PropertyInfo? instance;
    static FieldInfo? fieldmap, camIdx;
    static bool failed;

    public static void Tick() {
        if (failed || Player.LastSceneLoaded is not ("BattleMap" or "WorldMap")) return;
        try {
            if (instance == null) {
                var ee = typeof(FF9StateSystem).Assembly.GetType("EventEngine")!;
                instance = typeof(PersistenSingleton<>).MakeGenericType(ee).GetProperty("Instance")!;
                fieldmap = ee.GetField("fieldmap", F)!;
                camIdx = typeof(FF9StateSystem).Assembly.GetType("FieldMap")!.GetField("camIdx", F)!;
            }
            object? map = fieldmap!.GetValue(instance.GetValue(null));
            if (map == null) return;
            int cam = Convert.ToInt32(camIdx!.GetValue(map));
            if (cam == -1) return;
            camIdx.SetValue(map, Convert.ChangeType(-1, camIdx.FieldType));
            Log.Info($"widescreen: {Player.LastSceneLoaded} after field camera {cam}");
        } catch (Exception e) {
            failed = true;
            Log.Warn("widescreen: " + e);
        }
    }
}
