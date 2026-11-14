# FF9Recomp roadmap

Goal: FINAL FANTASY IX (Steam) with Memoria and Moguri Mod running natively on Android
(AYN Thor), the way FF7Recomp runs FF7: one APK, no game code or data inside it, built and
installed from the user's own copy of the game.

Route (chosen 2026-11-13, see [SPIKE.md](SPIKE.md)): no Unity. The user's own
Memoria-installed .NET assemblies run unchanged on .NET 8 Mono (ARM64 JIT); this project
supplies the engine underneath them: the ~723 UnityEngine internal calls they reach, an
asset-bundle reader, a GLES 3.2 renderer and the native plugins (audio via Memoria's SoLoud
backend, Theora movies via FFmpeg, and the battle effects plugin run from the player's own DLL
in an x86-64 interpreter).

Each milestone ends with its exit test, a commit and a report. One milestone at a time.

| # | Milestone | Exit test |
|---|---|---|
| M0 ✅ | Spike: Mono on the Thor | 32,476 game methods JIT on the Thor; level0's `SplashScreen.Awake` runs to its first engine call |
| M1 ✅ | Engine core, headless | Scene graph + MonoBehaviour lifecycle; the game's own startup reaches `Application.LoadLevel("Bundle")` and keeps running frames with every stub hit logged by name |
| M2 ✅ | Assets | `resources.assets` and the p0data bundles load (textures, meshes, text, MonoBehaviour fields), checked against an independent reader on the PC |
| M3 ✅ | Title screen | bgfx renderer, D3D9 SM2/3 shader translator, Unity UI/NGUI text; FF9's title screen drawn on the Thor |
| M4 (in progress) | Playable | Input (Thor controller), SoLoud audio, movies, field/world/battle rendering, battle effects plugin; new game through Alexandria to Evil Forest with saves |
| M5 | APK + launcher | Release-signed APK that sets up the game from the user's copy on the device, Memoria config UI |
| M6 ✅ | Moguri Mod | Moguri's textures and backgrounds load through Memoria in game on the Thor |
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

## Status (2026-11-14, evening)
- On the Thor (headless host, GLES 3.2 pbuffer) with Moguri Mod 9.0 enabled through Memoria:
  title (Moguri's HD art) -> New Game -> opening FMV -> the Prima Vista cargo hold: dialogue, the
  controls tutorial, walking, the candle, the naming screen, the Tantalus scene with Zidane,
  Blank, Cinna and Marcus (skinned, animated, face texture animation) on Moguri's HD backgrounds.
- Battles: `-memoria-battle 1` (FF9_ARGS) runs a full battle: plugin camera, HUD, ATB, enemy
  abilities and effects, game over. FF9SpecialEffectPlugin.dll is loaded from the player's
  install and run by src/host/x64emu.c (nothing from it is stored or shipped).
- Memory flat over 24,000 frames (GPU quad leak in Graphics.DrawTexture and per-call material
  copies fixed).
- Engine: ~1000 UnityEngine internal calls.
- Not yet checked: field-to-field travel beyond the cargo hold, menus (party, items, save),
  world map, saving/loading, the APK with a real controller.
- Next: APK on the Thor with the controller, then saves, menus and the world map.
