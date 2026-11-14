# FF9Recomp roadmap

Goal: FINAL FANTASY IX (Steam) with Memoria and Moguri Mod running natively on Android
(AYN Thor), the way FF7Recomp runs FF7: one APK, no game code or data inside it, built and
installed from the user's own copy of the game.

Route (chosen 2026-11-13, see [SPIKE.md](SPIKE.md)): no Unity. The user's own
Memoria-installed .NET assemblies run unchanged on .NET 8 Mono (ARM64 JIT); this project
supplies the engine underneath them: the ~723 UnityEngine internal calls they reach, an
asset-bundle reader, a renderer (bgfx/Vulkan) and the native plugins (audio via Memoria's
SoLoud backend, battle effects via a pcrecomp lift of FF9SpecialEffectPlugin, Theora movies).

Each milestone ends with its exit test, a commit and a report. One milestone at a time.

| # | Milestone | Exit test |
|---|---|---|
| M0 ✅ | Spike: Mono on the Thor | 32,476 game methods JIT on the Thor; level0's `SplashScreen.Awake` runs to its first engine call |
| M1 | Engine core, headless | Scene graph + MonoBehaviour lifecycle; the game's own startup reaches `Application.LoadLevel("Bundle")` and keeps running frames with every stub hit logged by name |
| M2 | Assets | `resources.assets` and the p0data bundles load (textures, meshes, text, MonoBehaviour fields), checked against an independent reader on the PC |
| M3 | Title screen | bgfx renderer, D3D9 SM2/3 shader translator, Unity UI/NGUI text; FF9's title screen drawn on the Thor |
| M4 | Playable | Input (Thor controller), SoLoud audio, movies, field/world/battle rendering, battle effects plugin; new game through Alexandria to Evil Forest with saves |
| M5 | APK + launcher | Release-signed APK that sets up the game from the user's copy on the device, Memoria config UI |
| M6 | Moguri Mod | Moguri's textures and backgrounds load through Memoria in game on the Thor |
| M7 | Polish | Steady frame rate, widescreen, controller-driven launcher |

## M1: engine core
- Native objects behind `UnityEngine.Object.m_CachedPtr`: GameObject, Transform (TRS,
  hierarchy), components; `Object.FindObjectsOfType`, `GetComponent`, `AddComponent`,
  `DontDestroyOnLoad`, `Destroy`.
- Scenes from the build's `levelN` serialized files: create managed MonoBehaviour instances,
  fill their serialized fields from the file, run Awake/OnEnable/Start, then Update /
  LateUpdate / coroutines each frame, in Unity's order.
- The named-stop loop from FF7 (M1 there): run, stop at the first engine call that has no
  implementation, implement it, run again.
