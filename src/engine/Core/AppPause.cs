// The app in the background or the device asleep (Gfx/Device.ChangeWindow): the game loop waits,
// and Memoria's SoLoud mixer is muted meanwhile (its voices keep their own pause states).
using System;
using System.Reflection;

namespace FF9Engine.Core;

public static class AppPause {
    static float savedVolume = -1;

    public static void Set(bool paused) {
        try {
            var t = Type.GetType("Global.Sound.SoLoud.SdLibAPIWithSoloud, Assembly-CSharp");
            var so = t?.GetField("soloud", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null);
            if (so != null) {
                var st = so.GetType();
                if (paused) {
                    savedVolume = Convert.ToSingle(st.GetMethod("getGlobalVolume")?.Invoke(so, null) ?? 1f);
                    st.GetMethod("setGlobalVolume")?.Invoke(so, new object[] { 0f });
                } else if (savedVolume >= 0) {
                    st.GetMethod("setGlobalVolume")?.Invoke(so, new object[] { savedVolume });
                    savedVolume = -1;
                }
            }
        } catch (Exception e) { Log.Warn("audio pause: " + e.Message); }
        if (!paused) Clock.Resync();
    }
}
