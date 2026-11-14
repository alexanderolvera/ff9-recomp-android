// UnityEngine.Input from the gamepad (the same state the game also reads through XInput) and
// touch. Keyboard and mouse are absent on the Thor.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FF9Engine.Icalls;

public static class Pad {
    [StructLayout(LayoutKind.Sequential)]
    public struct Raw { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }

    [DllImport("__Internal", EntryPoint = "XInputGamePadGetState")]
    static extern uint GetState(uint player, ref Raw s);

    public static Raw Now, Prev;

    public static void Poll() {
        Prev = Now;
        var s = new Raw();
        if (GetState(0, ref s) == 0) Now = s; else Now = default;
    }

    // XInput button bits
    public const ushort Up = 0x1, Down = 0x2, Left = 0x4, Right = 0x8, Start = 0x10, Back = 0x20, LThumb = 0x40, RThumb = 0x80,
        LB = 0x100, RB = 0x200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

    public static bool Held(ushort b) => (Now.Buttons & b) != 0;
    public static bool Pressed(ushort b) => (Now.Buttons & b) != 0 && (Prev.Buttons & b) == 0;
    public static bool Released(ushort b) => (Now.Buttons & b) == 0 && (Prev.Buttons & b) != 0;
    public static float Axis(short v) => Math.Abs(v) < 7849 ? 0 : Math.Clamp(v / 32767f, -1, 1);
}

static class InputIcalls {
    // Unity joystick button numbering for an Xbox pad on Windows
    static readonly ushort[] joyButtons = { Pad.A, Pad.B, Pad.X, Pad.Y, Pad.LB, Pad.RB, Pad.Back, Pad.Start, Pad.LThumb, Pad.RThumb };

    static ushort? KeyButton(string name) {
        name = name.ToLowerInvariant();
        if (name.StartsWith("joystick button ") || name.StartsWith("joystick 1 button ")) {
            int n = int.Parse(name[(name.LastIndexOf(' ') + 1)..]);
            return n < joyButtons.Length ? joyButtons[n] : null;
        }
        return null;
    }

    static float AxisValue(string axis) => axis switch {
        "Horizontal" => Pad.Axis(Pad.Now.LX) + (Pad.Held(Pad.Right) ? 1 : 0) - (Pad.Held(Pad.Left) ? 1 : 0),
        "Vertical" => Pad.Axis(Pad.Now.LY) + (Pad.Held(Pad.Up) ? 1 : 0) - (Pad.Held(Pad.Down) ? 1 : 0),
        _ => 0,
    };

    [Icall("UnityEngine.Input::GetAxis")]
    [Icall("UnityEngine.Input::GetAxisRaw")] static float GetAxis(string a) => Math.Clamp(AxisValue(a), -1, 1);
    [Icall("UnityEngine.Input::GetButton")] static bool GetButton(string b) => false;
    [Icall("UnityEngine.Input::GetButtonDown")] static bool GetButtonDown(string b) => false;
    [Icall("UnityEngine.Input::GetButtonUp")] static bool GetButtonUp(string b) => false;
    [Icall("UnityEngine.Input::GetKeyString")] static bool GetKey(string k) => KeyButton(k) is { } b && Pad.Held(b);
    [Icall("UnityEngine.Input::GetKeyDownString")] static bool GetKeyDown(string k) => KeyButton(k) is { } b && Pad.Pressed(b);
    [Icall("UnityEngine.Input::GetKeyUpString")] static bool GetKeyUp(string k) => KeyButton(k) is { } b && Pad.Released(b);

    static ushort? KeyCodeButton(int key) => key >= (int)KeyCode.JoystickButton0 && key <= (int)KeyCode.JoystickButton9
        ? joyButtons[key - (int)KeyCode.JoystickButton0]
        : key >= (int)KeyCode.Joystick1Button0 && key <= (int)KeyCode.Joystick1Button9 ? joyButtons[key - (int)KeyCode.Joystick1Button0] : null;

    [Icall("UnityEngine.Input::GetKeyInt")] static bool GetKeyInt(int k) => KeyCodeButton(k) is { } b && Pad.Held(b);
    [Icall("UnityEngine.Input::GetKeyDownInt")] static bool GetKeyDownInt(int k) => KeyCodeButton(k) is { } b && Pad.Pressed(b);
    [Icall("UnityEngine.Input::GetKeyUpInt")] static bool GetKeyUpInt(int k) => KeyCodeButton(k) is { } b && Pad.Released(b);
    [Icall("UnityEngine.Input::GetJoystickNames")] static string[] Joysticks() => new[] { "Controller (XBOX 360 For Windows)" };
    [Icall("UnityEngine.Input::GetMouseButton")] static bool Mouse(int b) => b == 0 && Touches.Down;
    [Icall("UnityEngine.Input::GetMouseButtonDown")] static bool MouseDown(int b) => b == 0 && Touches.Down && !Touches.WasDown;
    [Icall("UnityEngine.Input::GetMouseButtonUp")] static bool MouseUp(int b) => b == 0 && !Touches.Down && Touches.WasDown;
    [Icall("UnityEngine.Input::ResetInputAxes")] static void Reset() { }
    [Icall("UnityEngine.Input::INTERNAL_get_mousePosition")] static void MousePos(ref Vector3 v) => v = new Vector3(Touches.X, Touches.Y, 0);
    [Icall("UnityEngine.Input::INTERNAL_get_mouseScrollDelta")] static void Scroll(ref Vector2 v) => v = default;
    [Icall("UnityEngine.Input::get_anyKey")] static bool AnyKey() => Pad.Now.Buttons != 0;
    [Icall("UnityEngine.Input::get_anyKeyDown")] static bool AnyKeyDown() => (Pad.Now.Buttons & ~Pad.Prev.Buttons) != 0;
    [Icall("UnityEngine.Input::get_inputString")] static string InputString() => "";
    [Icall("UnityEngine.Input::get_touchCount")] static int TouchCount() => 0;
    [Icall("UnityEngine.Input::GetTouch")] static Touch GetTouch(int i) => default;
    [Icall("UnityEngine.Input::set_imeCompositionMode")] static void Ime(IMECompositionMode m) { }
    [Icall("UnityEngine.Input::get_compositionString")] static string Composition() => "";
    [Icall("UnityEngine.Input::INTERNAL_set_compositionCursorPos")] static void CompPos(ref Vector2 v) { }
    [Icall("UnityEngine.Input::get_mousePresent")] static bool MousePresent() => false;
    [Icall("UnityEngine.Input::get_touchSupported")] static bool TouchSupported() => true;
    [Icall("UnityEngine.Input::get_multiTouchEnabled")] static bool MultiTouch() => false;
    [Icall("UnityEngine.Input::set_multiTouchEnabled")] static void SetMultiTouch(bool v) { }
}

public static class Touches {
    public static bool Down, WasDown;
    public static float X, Y;
}
