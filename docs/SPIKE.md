# FF9 Android feasibility spike (2026-11-13)

Goal: decide how FINAL FANTASY IX (Steam, with Memoria + Moguri Mod) can run natively on
Android (AYN Thor), the way FF7Recomp runs FF7. Inputs: `Downloads\FINAL FANTASY IX`
(vanilla) and a copy of it with Memoria v2026.11.09 installed (`work/FF9-memoria`).
Everything under `out/` and `work/` is derived from the game: never commit or publish it.

## The game
- Unity 5.2.3p2, Mono scripting backend, Windows x86 and x64 builds.
- Data: 7.6 GB of Unity asset bundles (`StreamingAssets/p0data*.bin`, "UnityRaw" 5.x, uncompressed),
  590 MB `resources.assets`, Ogg Theora movies (`StreamingAssets/ma`).
- Shaders: 143 ShaderLab shaders, compiled for **Direct3D 9 only** (shader model 2.0/3.0;
  672 sub-programs, no D3D11/GL). Mostly simple PSX blend-mode and unlit shaders. Memoria also
  ships all 139 as text (`StreamingAssets/Shaders/*.txt`, D3D9 assembly with binding tables).

## Memoria changes the picture
Memoria does not patch Assembly-CSharp: it replaces it with its own build (11.5 k -> 19 k
methods) and adds Memoria.Prime, Newtonsoft.Json, Memoria.Scripts (runtime-compiled C#), and
three native plugins: `soloud.dll`, `SaXAudio.dll`, `JoyShockLibrary.dll`.
- Audio: Memoria has a **SoLoud backend** (`[Audio] Backend = 2`), and SoLoud is open source and
  builds for Android. So SdLib (Square Enix's closed sound library) can be skipped entirely.
- Moguri Mod is Memoria content (textures, backgrounds) loaded through Memoria; not installed yet.

## Measured: what the game code needs from the engine
`tools/unity_surface.py` (own ECMA-335 reader, `tools/cil.py`): roots = every method of the game
assemblies, call graph through Unity's managed DLLs (with virtual overrides), stopping at the
.NET framework. It over-approximates (all game code counts as reachable).

| | Vanilla | Memoria |
|---|---|---|
| Game methods | 13 k | 25 k |
| UnityEngine native entry points reached (internal calls) | 646 of 3788 | **723** of 3788 |
| UnityEngine types used directly by Assembly-CSharp | 149 | 164 |
| MonoBehaviour classes in the game | 362 | 366 |
| .NET framework members used | 647 | 1488 |

Memoria's 723 engine calls by area: rendering 261 (Camera 48, Mesh 31, Material, RenderTexture,
GL immediate mode, Graphics), scene/objects/asset bundles 116, IMGUI 95, input/platform 93,
Unity UI + text 56, animation 33 (legacy Animation + some Animator), physics 16, audio 5, other 48.

Native plugins the managed code calls (P/Invoke): FF9SpecialEffectPlugin 13, theorawrapper 24,
SdLib 21, soloud 170, SaXAudio 53, JoyShockLibrary 6, steamwrapper 13, XInputInterface 2,
Win32 user32/kernel32 28 (window tricks, key state, named pipes).

## Measured: the battle-effects plugin recompiles cleanly
`FF9SpecialEffectPlugin.dll` (x86; 217 KB code, 6 MB data; imports only 52 CRT/kernel32
functions) through FF7Recomp's pcrecomp pipeline (`FF7Recomp/tools/run_lift.py catalog/lift`):
- 1302 functions catalogued in 24 s from the 13 exports; lifted in 11 s, **0 failures**,
  5 unimplemented instructions (int/lock/repz in CRT code at 0x10035xxx).
- The 1.88 M lines of C **compile for ARM64** (NDK clang, -O1, FF7's flags) in 32 s, no errors.
- Not yet linked or run: needs the 52 CRT imports (win32hle has most) and a bridge for the 13
  exports, since 64-bit managed code passes pointers to a 32-bit guest (or lift the x64 build).

## Not measured
- Mono on the Thor (the runtime download was declined): whether Mono's ARM64 JIT runs Unity 5.2's
  .NET 3.5-era assemblies. Low risk (Mono runs them on PC), but unverified.
- Reading the 5.2 asset bundles and serialized MonoBehaviour fields (known format; AssetStudio
  and UnityPy read it), and the size of the scene/prefab work.

## Routes
1. **Unity player, rebuilt with a modern Unity** (IL2CPP to ARM64). The engine work is done by
   Unity; the D3D9 shaders need rewriting for GLES/Vulkan (139 small ones, Memoria has them as
   text), and the 5.2 bundles must load in the new Unity or be re-imported. Game code ends up in
   the APK (IL2CPP only compiles on a PC), which breaks FF7Recomp's no-game-code rule unless the
   player is built on the device. Needs the Unity Editor (proprietary).
2. **No Unity: Mono + our own engine for the 723 calls.** The user's own (Memoria-installed)
   assemblies run unchanged on Mono; we write the engine underneath: asset-bundle loader, scene
   graph and MonoBehaviour lifecycle, a renderer (bgfx, as in FF7) with a D3D9 SM2/3 -> SPIR-V/GLSL
   shader translator, IMGUI, Unity UI text (FreeType), legacy animation, input. Audio via
   Memoria's SoLoud backend; effects via the recompiled plugin; movies via FFmpeg/libtheora.
   Keeps the no-game-code rule and lets Memoria, Moguri and Memoria's mod catalog work as on PC.
   Size: larger than the FFNx port (FFNx was ~600 game structs and 785 hooks, but its renderer
   existed; here the engine underneath does not).

## Route 2, step 1: Mono on the Thor (2026-11-13)
Host: `src/host/ff9host.c` (embeds .NET 8.0.30 Mono for android-arm64, from nuget.org), built with
NDK clang, run over adb in `/data/local/tmp/ff9` (`bash tools/build_host.sh jit | run <Type> <Method>`).
The game's DLLs come from the Memoria-installed copy; Unity's own mscorlib/System/System.Core are
replaced by .NET 8's. Every UnityEngine internal call (3788, `tools/gen_host_tables.py`) is a named
stub: in `run` mode the first one reached prints its name and the managed stack and exits 93.

- **JIT:** all 10 assemblies load; 32,476 methods JIT-compiled to ARM64 in 4.0 s. 826 fail:
  822 in 30 Assembly-CSharp classes (ff9, Dialog, FieldMap, UIManager, ...) whose static
  constructors run at compile time and break on stubbed engine calls returning null (whole
  classes fail together; not JIT errors), plus 4 security/Unity-internal methods with no
  equivalent on .NET 8 (Newtonsoft's JsonTypeReflector trust checks, Mono.Security TLS
  callback, UnityEngine.ClassLibraryInitializer.Init).
- **Startup:** `tools/unity_sf.py` reads Unity 5.2 serialized files without type trees. level0 is
  the splash scene: `Camera_SplashTitle` (Camera + MonoBehaviour `SplashScreen`) and `SplashTitle`
  (SpriteRenderer). `SplashScreen.Awake` runs on the Thor until `Component::get_transform`
  (it needs the scene graph); before that `MonoBehaviour::.ctor` was the first stop.
- Host lessons: the host thread must be in a GC-unsafe region to use the object APIs, engine
  functions are registered as cooperative icalls (`mono_add_internal_call_with_flags(..., 1)`),
  and `System.Resources.UseSystemResourceKeys=true` avoids loading .NET resource strings.

Next: the scene graph (GameObject/Transform/Component as native objects behind `m_CachedPtr`),
built from level0/level1 by the serialized-file reader, with the MonoBehaviour lifecycle
(Awake/OnEnable/Start/Update) driving FF9's real startup through `Application.LoadLevel("Bundle")`.

## Recommendation
Route 2, built in this order so each step has an exit test:
1. Mono on the Thor loads the Memoria assemblies and runs FF9's own startup to the first engine
   call (headless, every unimplemented engine call logged by name: the FF7 "named STOP" loop).
2. Asset bundles: read `resources.assets` and one p0data bundle into objects (textures, meshes,
   MonoBehaviours) on the PC, compared against AssetStudio/UnityPy.
3. Engine calls by area, driven by what step 1 hits first, up to the title screen through bgfx.
4. The effects plugin linked and run (pcrecomp + win32hle), SoLoud audio, Theora movies.
