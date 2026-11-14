// Unity "messages" (Awake, Start, Update, ...) found by name on a MonoBehaviour class, any
// access level, including base classes.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace FF9Engine.Scripting;

public sealed class ScriptMethods {
    public MethodInfo? Awake, OnEnable, Start, Update, LateUpdate, FixedUpdate, OnDisable, OnDestroy,
                       OnGUI, OnLevelWasLoaded, OnApplicationQuit, OnApplicationPause, OnApplicationFocus,
                       OnPreRender, OnPostRender, OnRenderObject, OnWillRenderObject, OnPreCull,
                       OnBecameVisible, OnBecameInvisible, OnTransformParentChanged, OnTransformChildrenChanged,
                       OnRectTransformDimensionsChange;

    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    static readonly Dictionary<Type, ScriptMethods> cache = new();

    public static ScriptMethods For(Type t) {
        if (cache.TryGetValue(t, out var m)) return m;
        m = new ScriptMethods();
        foreach (var f in typeof(ScriptMethods).GetFields())
            f.SetValue(m, Find(t, f.Name));
        cache[t] = m;
        return m;
    }

    public static MethodInfo? Find(Type t, string name) {
        for (var c = t; c != null && c != typeof(UnityEngine.MonoBehaviour); c = c.BaseType)
            foreach (var mi in c.GetMethods(Inst))
                if (mi.Name == name && !mi.IsGenericMethodDefinition && mi.GetParameters().Length <= 1)
                    return mi;
        return null;
    }

    /// Calls a message; exceptions are logged and swallowed, as Unity does. A message that
    /// returns IEnumerator (e.g. "IEnumerator Start()") is started as a coroutine.
    public static void Call(Core.NMonoBehaviour mb, MethodInfo? mi, object? arg = null) {
        if (mi == null) return;
        try {
            object? r = mi.Invoke(mb.Managed, mi.GetParameters().Length == 1 ? new[] { arg } : null);
            if (r is IEnumerator e) Coroutines.Start(mb, e);
        } catch (TargetInvocationException ex) {
            Log.Exception(ex.InnerException ?? ex, $"{mb.Managed!.GetType().Name}.{mi.Name}");
        }
    }
}
