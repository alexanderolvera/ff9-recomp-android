// The game's text fields (NGUI's UIInput: a character's name) on Android (GitHub issue 6). On PC
// the name is typed on the keyboard (Input.inputString); a phone has none. While a field is
// selected its text goes to the host (ff9_text_open), the APK shows Android's keyboard in a dialog
// when no controller is connected (GameActivity), and the typed text comes back (ff9_text_take):
// it goes into the field through its own validation, and the field lets go of the focus, so the
// next Confirm accepts the name as the game does. With a controller, Confirm twice keeps the
// default name and Menu brings it back, as on PC.
//
// FF9_TEST_NAMING=character (tests): opens the naming screen for that character (0 Zidane) once a
// field has been playable for five seconds, as the field script's naming event does.
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace FF9Engine.Core;

public static class TextInput {
    [DllImport("__Internal")] static extern void ff9_text_open([MarshalAs(UnmanagedType.LPUTF8Str)] string current);
    [DllImport("__Internal")] static extern void ff9_text_close();
    [DllImport("__Internal")] static extern int ff9_text_take(byte[] buf, int size);

    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type? uiInput;
    static FieldInfo? selection;
    static PropertyInfo? value;
    static MethodInfo? removeFocus;
    static object? shown;
    static bool failed;
    static readonly byte[] buf = new byte[1024];

    public static void Tick() {
        if (failed) return;
        try {
            TestNaming();
            if (uiInput == null) {
                uiInput = typeof(FF9StateSystem).Assembly.GetType("UIInput");
                if (uiInput == null) return;
                selection = uiInput.GetField("selection", F);
                value = uiInput.GetProperty("value", F);
                removeFocus = uiInput.GetMethod("RemoveFocus", F, Type.EmptyTypes);
            }
            object? sel = selection!.GetValue(null);
            if (!ReferenceEquals(sel, shown)) {
                shown = sel;
                if (sel != null) ff9_text_open(value!.GetValue(sel) as string ?? "");
                else ff9_text_close();
            }
            if (sel == null) return;
            int n = ff9_text_take(buf, buf.Length);
            if (n < 0) return;
            string text = Encoding.UTF8.GetString(buf, 0, n);
            value!.SetValue(sel, text);
            Log.Info($"text field: \"{value.GetValue(sel)}\" from the keyboard");
            removeFocus?.Invoke(sel, null);
        } catch (Exception e) {
            failed = true;
            Log.Warn("text input: " + e);
        }
    }

    static readonly string? namingTest = Environment.GetEnvironmentVariable("FF9_TEST_NAMING");
    static long hudSince = -1;
    static bool namingDone;

    static void TestNaming() {
        if (namingTest == null || namingDone) return;
        var ui = typeof(PersistenSingleton<>).MakeGenericType(typeof(FF9StateSystem).Assembly.GetType("UIManager")!).GetProperty("Instance")!.GetValue(null);
        string? state = ui?.GetType().GetProperty("State", F)?.GetValue(ui)?.ToString();
        if (Player.LastSceneLoaded != "FieldMap" || state != "FieldHUD") { hudSince = -1; return; }
        if (hudSince < 0) { hudSince = Clock.Frame; return; }
        if (Clock.Frame - hudSince < 300) return;
        namingDone = true;
        var svc = typeof(FF9StateSystem).Assembly.GetType("EventService")!;
        var open = svc.GetMethod("OpenNameMenu", F)!;
        open.Invoke(null, new[] { Enum.ToObject(open.GetParameters()[0].ParameterType, int.Parse(namingTest)) });
        Log.Info("test: naming screen for character " + namingTest);
    }
}
