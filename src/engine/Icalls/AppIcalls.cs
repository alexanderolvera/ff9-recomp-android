// Application, Time, Screen, SystemInfo basics.
using System;
using System.IO;
using FF9Engine.Core;
using FF9Engine.Scripting;
using UnityEngine;

namespace FF9Engine.Icalls;

static class AppIcalls {
    static string Root => Directory.GetCurrentDirectory();

    [Icall("UnityEngine.Application::get_dataPath")] static string DataPath() => Path.Combine(Root, "x64", "FF9_Data");
    [Icall("UnityEngine.Application::get_streamingAssetsPath")] static string Streaming() => Path.Combine(Root, "x64", "FF9_Data", "StreamingAssets");
    [Icall("UnityEngine.Application::get_persistentDataPath")] static string Persistent() => Dir(Path.Combine(Root, "persistent"));
    [Icall("UnityEngine.Application::get_temporaryCachePath")] static string Cache() => Dir(Path.Combine(Root, "cache"));
    static string Dir(string p) { Directory.CreateDirectory(p); return p; }

    [Icall("UnityEngine.Application::get_platform")] static RuntimePlatform Platform() => RuntimePlatform.WindowsPlayer;
    [Icall("UnityEngine.Application::get_unityVersion")] static string UnityVersion() => "5.2.3p2";
    [Icall("UnityEngine.Application::get_version")] static string Version() => "1.0";
    [Icall("UnityEngine.Application::get_productName")] static string Product() => "FINAL FANTASY IX";
    [Icall("UnityEngine.Application::get_companyName")] static string Company() => "SQUARE ENIX CO., LTD.";
    [Icall("UnityEngine.Application::get_bundleIdentifier")] static string Bundle() => "com.squareenix.ff9";
    [Icall("UnityEngine.Application::get_cloudProjectId")] static string Cloud() => "";
    [Icall("UnityEngine.Application::get_isPlaying")] static bool IsPlaying() => true;
    [Icall("UnityEngine.Application::get_isEditor")] static bool IsEditor() => false;
    [Icall("UnityEngine.Application::get_isWebPlayer")] static bool IsWebPlayer() => false;
    [Icall("UnityEngine.Application::get_isBatchmode")] static bool IsBatch() => false;
    [Icall("UnityEngine.Application::get_isShowingSplashScreen")] static bool Splash() => false;
    [Icall("UnityEngine.Application::get_genuine")] static bool Genuine() => true;
    [Icall("UnityEngine.Application::get_genuineCheckAvailable")] static bool GenuineAvail() => false;
    [Icall("UnityEngine.Application::get_runInBackground")] static bool GetRunInBg() => true;
    [Icall("UnityEngine.Application::set_runInBackground")] static void SetRunInBg(bool v) { }
    [Icall("UnityEngine.Application::get_targetFrameRate")] static int GetFps() => Clock.TargetFrameRate;
    [Icall("UnityEngine.Application::set_targetFrameRate")] static void SetFps(int v) { if (v != Clock.TargetFrameRate) Log.Info($"target frame rate {v}"); Clock.TargetFrameRate = v; }
    [Icall("UnityEngine.Application::get_systemLanguage")] static SystemLanguage Lang() => SystemLanguage.English;
    [Icall("UnityEngine.Application::get_internetReachability")] static NetworkReachability Net() => NetworkReachability.NotReachable;
    [Icall("UnityEngine.Application::SetLogCallbackDefined")] static void LogCb(bool d, bool t) { }
    [Icall("UnityEngine.Application::get_stackTraceLogType")] static StackTraceLogType GetSt() => StackTraceLogType.None;
    [Icall("UnityEngine.Application::set_stackTraceLogType")] static void SetSt(StackTraceLogType v) { }
    [Icall("UnityEngine.Application::get_backgroundLoadingPriority")] static UnityEngine.ThreadPriority GetPri() => UnityEngine.ThreadPriority.Normal;
    [Icall("UnityEngine.Application::set_backgroundLoadingPriority")] static void SetPri(UnityEngine.ThreadPriority v) { }
    [Icall("UnityEngine.Application::HasProLicense")] static bool Pro() => true;
    [Icall("UnityEngine.Application::HasAdvancedLicense")] static bool Adv() => true;
    [Icall("UnityEngine.Application::get_submitAnalytics")] static bool Analytics() => false;
    [Icall("UnityEngine.Application::HasUserAuthorization")] static bool HasAuth(UserAuthorization m) => false;
    [Icall("UnityEngine.Application::OpenURL")] static void OpenURL(string url) => Log.Info($"OpenURL {url}");
    [Icall("UnityEngine.Application::Quit")] static void Quit() { Log.Info("Application.Quit\n" + Environment.StackTrace); Player.Quit = true; }
    [Icall("UnityEngine.Application::CancelQuit")] static void CancelQuit() => Player.Quit = false;
    [Icall("UnityEngine.Application::get_loadedLevel")] static int LoadedLevel() => Player.ActiveScene?.Index ?? 0;
    [Icall("UnityEngine.Application::get_loadedLevelName")] static string LoadedLevelName() => Player.ActiveScene?.Name ?? "";
    [Icall("UnityEngine.Application::get_levelCount")] static int LevelCount() => Player.SceneNames.Length;
    [Icall("UnityEngine.Application::get_isLoadingLevel")] static bool IsLoading() => false;
    [Icall("UnityEngine.Application::CanStreamedLevelBeLoaded")] static bool CanLoad(int i) => i >= 0 && i < Player.SceneNames.Length;
    [Icall("UnityEngine.Application::CanStreamedLevelBeLoadedByName")] static bool CanLoadName(string n) => Player.SceneIndex(n) >= 0;
    [Icall("UnityEngine.Application::GetStreamProgressForLevel")] static float Progress(int i) => 1;
    [Icall("UnityEngine.Application::GetStreamProgressForLevelByName")] static float ProgressName(string n) => 1;

    [Icall("UnityEngine.Application::LoadLevelAsync")]
    static AsyncOperation? LoadLevelAsync(string name, int index, bool additive, bool mustCompleteNextFrame) {
        if (name != null) index = Player.SceneIndex(name);
        if (index < 0 || index >= Player.SceneNames.Length) {
            Log.Info($"[log error] Level '{name}' (index {index}) couldn't be loaded because it has not been added to the build settings.");
            return null;
        }
        var m = AsyncOps.Create<AsyncOperation>(out var op);
        Player.RequestLevel(index, additive, op);
        return m;
    }

    [Icall("UnityEngine.Application::UnloadLevel")] static bool UnloadLevel(string p, int i) { Log.Once("UnloadLevel"); return true; }

    // ---- Time ----
    [Icall("UnityEngine.Time::get_time")] static float T() => Clock.Now;
    [Icall("UnityEngine.Time::get_timeSinceLevelLoad")] static float TL() => Clock.Now - Clock.LevelLoadTime;
    [Icall("UnityEngine.Time::get_deltaTime")] static float DT() => Clock.Delta;
    [Icall("UnityEngine.Time::get_fixedTime")] static float FT() => Clock.FixedTime;
    [Icall("UnityEngine.Time::get_unscaledTime")] static float UT() => Clock.Unscaled;
    [Icall("UnityEngine.Time::get_unscaledDeltaTime")] static float UDT() => Clock.UnscaledDelta;
    [Icall("UnityEngine.Time::get_fixedDeltaTime")] static float FDT() => Clock.FixedDelta;
    [Icall("UnityEngine.Time::set_fixedDeltaTime")] static void SFDT(float v) => Clock.FixedDelta = Math.Max(0.0001f, v);
    [Icall("UnityEngine.Time::get_maximumDeltaTime")] static float MDT() => Clock.MaxDelta;
    [Icall("UnityEngine.Time::set_maximumDeltaTime")] static void SMDT(float v) => Clock.MaxDelta = v;
    [Icall("UnityEngine.Time::get_smoothDeltaTime")] static float SDT() => Clock.SmoothDelta;
    [Icall("UnityEngine.Time::get_timeScale")] static float TS() => Clock.TimeScale;
    [Icall("UnityEngine.Time::set_timeScale")] static void STS(float v) => Clock.TimeScale = v;
    [Icall("UnityEngine.Time::get_frameCount")] static int FC() => Clock.Frame;
    [Icall("UnityEngine.Time::get_renderedFrameCount")] static int RFC() => Clock.Frame;
    [Icall("UnityEngine.Time::get_realtimeSinceStartup")] static float RT() => Clock.Realtime;
    [Icall("UnityEngine.Time::get_captureFramerate")] static int CF() => 0;
    [Icall("UnityEngine.Time::set_captureFramerate")] static void SCF(int v) { }
}
