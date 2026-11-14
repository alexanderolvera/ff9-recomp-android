// AsyncOperation (level loads, Resources.LoadAsync, asset bundle requests): the managed object's
// m_Ptr holds the id of an Op here.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FF9Engine.Scripting;

public static class AsyncOps {
    public sealed class Op {
        public int Id;
        public bool Done;
        public float Progress;
        public int Priority;
        public bool AllowActivation = true;
        public Action? OnComplete;
        public object? Result;
        public int CompleteAtFrame = -1;     // complete automatically at this frame

        public void Complete() {
            if (Done) return;
            Done = true;
            Progress = 1;
            OnComplete?.Invoke();
        }
    }

    static readonly Dictionary<int, Op> ops = new();
    static int nextId = 1;

    public static T Create<T>(out Op op, int completeAtFrame = -1) where T : AsyncOperation {
        op = new Op { Id = nextId++, CompleteAtFrame = completeAtFrame };
        ops[op.Id] = op;
        var m = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        Unsafe.As<PtrLayout>(m).Ptr = op.Id;
        return m;
    }

    public static Op? Of(AsyncOperation? m) => m is null ? null : ops.GetValueOrDefault((int)Unsafe.As<PtrLayout>(m).Ptr);

    public static bool IsDone(AsyncOperation m) => Of(m) is not { } o || o.Done;

    public static void Tick() {
        foreach (var o in ops.Values)
            if (!o.Done && o.CompleteAtFrame >= 0 && Core.Clock.Frame >= o.CompleteAtFrame) o.Complete();
    }

    [Icall("UnityEngine.AsyncOperation::get_isDone")]
    static bool IsDoneIcall(AsyncOperation self) => IsDone(self);

    [Icall("UnityEngine.AsyncOperation::get_progress")]
    static float Progress(AsyncOperation self) => Of(self)?.Progress ?? 1;

    [Icall("UnityEngine.AsyncOperation::get_priority")]
    static int GetPriority(AsyncOperation self) => Of(self)?.Priority ?? 0;

    [Icall("UnityEngine.AsyncOperation::set_priority")]
    static void SetPriority(AsyncOperation self, int v) { if (Of(self) is { } o) o.Priority = v; }

    [Icall("UnityEngine.AsyncOperation::get_allowSceneActivation")]
    static bool GetAllow(AsyncOperation self) => Of(self)?.AllowActivation ?? true;

    [Icall("UnityEngine.AsyncOperation::set_allowSceneActivation")]
    static void SetAllow(AsyncOperation self, bool v) { if (Of(self) is { } o) o.AllowActivation = v; }

    [Icall("UnityEngine.AsyncOperation::InternalDestroy")]
    static void InternalDestroy(AsyncOperation self) { }

    [Icall("UnityEngine.Coroutine::ReleaseCoroutine")]
    static void ReleaseCoroutine(Coroutine self) { }
}
