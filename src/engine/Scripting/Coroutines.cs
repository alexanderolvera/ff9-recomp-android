// Coroutines, Invoke/InvokeRepeating, as Unity runs them: StartCoroutine runs the iterator to
// its first yield at once; "yield return null" resumes after the next Update; WaitForSeconds
// uses scaled time; WaitForEndOfFrame after rendering; AsyncOperation/Coroutine when done.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FF9Engine.Core;
using UnityEngine;

namespace FF9Engine.Scripting;

[StructLayout(LayoutKind.Sequential)]
sealed class PtrLayout { public IntPtr Ptr; }

[StructLayout(LayoutKind.Sequential)]
sealed class FloatLayout { public float Value; }

public sealed class CoroutineState {
    public int Id;
    public NMonoBehaviour Owner = null!;
    public readonly Stack<IEnumerator> Stack = new();
    public IEnumerator Root = null!;
    public object? Wait;
    public float WaitUntil;
    public bool Done;
    public Coroutine? Handle;
    public string? Name;
    public readonly List<CoroutineState> Waiters = new();
}

public static class Coroutines {
    static readonly List<CoroutineState> active = new();
    static readonly Dictionary<int, CoroutineState> byId = new();
    static int nextId = 1;

    public static Coroutine Start(NMonoBehaviour owner, IEnumerator e) {
        var c = new CoroutineState { Id = nextId++, Owner = owner, Root = e };
        c.Stack.Push(e);
        byId[c.Id] = c;
        c.Handle = (Coroutine)RuntimeHelpers.GetUninitializedObject(typeof(Coroutine));
        Unsafe.As<PtrLayout>(c.Handle).Ptr = c.Id;
        active.Add(c);
        Step(c);
        return c.Handle;
    }

    public static CoroutineState? Of(Coroutine? h) =>
        h is null ? null : byId.GetValueOrDefault((int)Unsafe.As<PtrLayout>(h).Ptr);

    public static void Stop(NMonoBehaviour owner, Func<CoroutineState, bool>? which = null) {
        foreach (var c in active)
            if (c.Owner == owner && !c.Done && (which == null || which(c))) Finish(c);
    }

    static void Finish(CoroutineState c) {
        c.Done = true;
        byId.Remove(c.Id);
    }

    /// Advances one coroutine until it yields something that is not yet ready.
    static void Step(CoroutineState c) {
        while (!c.Done && c.Stack.Count > 0) {
            var e = c.Stack.Peek();
            bool more;
            try {
                more = e.MoveNext();
            } catch (Exception ex) {
                Log.Exception(ex, $"coroutine {c.Owner.Managed?.GetType().Name}");
                Finish(c);
                return;
            }
            if (!more) { c.Stack.Pop(); continue; }
            object? y = e.Current;
            switch (y) {
                case IEnumerator nested:
                    c.Stack.Push(nested);
                    continue;
                case WaitForSeconds w:
                    c.Wait = w;
                    c.WaitUntil = Clock.Now + Unsafe.As<FloatLayout>(w).Value;
                    return;
                default:
                    c.Wait = y;     // null, WaitForEndOfFrame, WaitForFixedUpdate, Coroutine, AsyncOperation, WWW...
                    return;
            }
        }
        if (c.Stack.Count == 0) Finish(c);
    }

    static bool Ready(CoroutineState c, Phase phase) {
        switch (c.Wait) {
            case null: return phase == Phase.Update;
            case WaitForSeconds: return phase == Phase.Update && Clock.Now >= c.WaitUntil;
            case WaitForEndOfFrame: return phase == Phase.EndOfFrame;
            case WaitForFixedUpdate: return phase == Phase.Fixed;
            case Coroutine h: return phase == Phase.Update && (Of(h) is not { } o || o.Done);
            case AsyncOperation op: return phase == Phase.Update && AsyncOps.IsDone(op);
            default:
                if (Waits.TryGetValue(c.Wait.GetType(), out var f)) return phase == Phase.Update && f(c.Wait);
                return phase == Phase.Update;
        }
    }

    /// Ready checks for other yield instructions (WWW, ...), by type.
    public static readonly Dictionary<Type, Func<object, bool>> Waits = new();

    public enum Phase { Update, Fixed, EndOfFrame }

    public static void Run(Phase phase) {
        int n = active.Count;       // coroutines started during this pass wait for the next one
        for (int i = 0; i < n; i++) {
            var c = active[i];
            if (c.Done) continue;
            if (c.Owner.Managed is null || Registry.Get(c.Owner.Managed) == null) { Finish(c); continue; }
            if (!c.Owner.GameObject.ActiveInHierarchy) continue;
            if (Ready(c, phase)) Step(c);
        }
        active.RemoveAll(c => c.Done);
    }

    // ---- Invoke / InvokeRepeating ----
    sealed class Invocation { public NMonoBehaviour Owner = null!; public string Method = ""; public float At, Repeat; public bool Done; }
    static readonly List<Invocation> invokes = new();

    public static void Invoke(NMonoBehaviour mb, string name, float delay, float repeat) =>
        invokes.Add(new Invocation { Owner = mb, Method = name, At = Clock.Now + delay, Repeat = repeat });

    public static void CancelInvoke(NMonoBehaviour mb, string? name) {
        foreach (var i in invokes) if (i.Owner == mb && (name == null || i.Method == name)) i.Done = true;
    }

    public static bool IsInvoking(NMonoBehaviour mb, string? name) =>
        invokes.Exists(i => !i.Done && i.Owner == mb && (name == null || i.Method == name));

    public static void RunInvokes() {
        int n = invokes.Count;
        for (int k = 0; k < n; k++) {
            var i = invokes[k];
            if (i.Done || Clock.Now < i.At) continue;
            if (Registry.Get(i.Owner.Managed) == null) { i.Done = true; continue; }
            if (i.Owner.Enabled && i.Owner.GameObject.ActiveInHierarchy || i.Repeat <= 0)
                ScriptMethods.Call(i.Owner, ScriptMethods.Find(i.Owner.Managed!.GetType(), i.Method));
            if (i.Repeat > 0) i.At += i.Repeat; else i.Done = true;
        }
        invokes.RemoveAll(i => i.Done);
    }
}
