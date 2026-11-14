# Notes

## Layout
- `src/host/` the native host: embeds Mono, implements UnityEngine internal calls.
- `tools/` PC-side scripts: `cil.py` (ECMA-335 reader), `unity_surface.py` (engine-call survey),
  `gen_host_tables.py` (stub table + JIT token lists), `unity_sf.py` (Unity 5.2 serialized
  files), `dll_seeds.py` (export seeds for FF7Recomp's pcrecomp lift), `build_host.sh`.
- Not in git (game-derived or downloaded): `work/FF9-memoria` (copy of the game with Memoria
  installed), `out/` (generated tables, logs), `dl/` (Memoria Patcher, .NET 8 Mono runtime
  nupkg), `ref/` (Memoria source files for reading).

## Thor
- Wi-Fi adb; `$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe` with `MSYS_NO_PATHCONV=1`.
- Device staging: `/data/local/tmp/ff9` (`bcl/` .NET 8 libraries and native .so files,
  `managed/` game DLLs, `tokens/`, `game/` = working directory with Memoria.ini,
  StreamingAssets and `x64/FF9_Data`).
- Before using it: check FF7Recomp isn't running (the user may be playing). Put it back to
  sleep afterwards.

## Mono embedding (.NET 8.0.30, android-arm64)
- `monovm_initialize` properties: TRUSTED_PLATFORM_ASSEMBLIES, NATIVE_DLL_SEARCH_DIRECTORIES,
  APP_CONTEXT_BASE_DIRECTORY, `System.Globalization.Invariant=true`,
  `System.Resources.UseSystemResourceKeys=true` (avoids a resource cctor crash).
- The host thread must be inside `mono_threads_enter_gc_unsafe_region` to use object APIs.
- Engine functions are registered cooperative: `mono_add_internal_call_with_flags(name, fn, 1)`.
- Exception `ToString` from native code can abort; only ask for it when debugging (`FF9_WHY`).

## Unity 5.2 serialized files (format 15)
- Big-endian header (metaSize, fileSize, version, dataOffset), endianness byte at 16 (0 =
  little), metadata from 20: version string, platform, typetree flag (0 in this build),
  types (classID, 16-byte script hash if classID < 0, 16-byte hash), objects (aligned:
  pathID i64, offset, size, typeIndex, classID u16, scriptIndex i16, stripped u8),
  script types, externals.
- level0 = splash scene: `Camera_SplashTitle` (Camera + `SplashScreen`), `SplashTitle`
  (SpriteRenderer).

## Engine notes (M1-M3)
- Engine calls: every UnityEngine internal call is a jump thunk (out/host/ue_thunks.S) through
  ue_icall_table; FF9Engine fills slots with its [Icall] methods' compiled code. Registering
  managed methods with Mono directly broke when a caller got compiled (and the icall resolved)
  before registration finished.
- Debug switches (FF9_ENV): FF9_FRAMES, FF9_SHOT=f1,f2:dir, FF9_DRAWLOG=frame, FF9_DUMP_FRAME=frames
  (+FF9_INSPECT=Types or FF9_INSPECT_GO=Name_With_Underscores), FF9_TRACE_FIELDS=Type,
  FF9_DUMP_TREE=classId, FF9_DUMP_GLSL, FF9_SKIP_SHADER, FF9_ONLY_Q/FF9_SKIP_Q, FF9_DUMP_TEX,
  FF9_TRACE_MESHCOL, FF9_STOP_ON_EXCEPTION.
- Memoria's own log (game/Memoria.log on the device) explains most startup failures.
- Gotchas found: serialized 1/2-byte script fields align to 4 except inside arrays; format-15
  objects' type ID is the class ID (match type trees by it); AnimationCurve wrap modes are
  Unity's internal enum (0 ping-pong, 1 repeat, 2 clamp); CharacterInfo uv is bottom-left with
  positive height and vert.y is relative to the ascent; Memoria casts SoLoud handles to int (C++
  objects are allocated below 2 GB, src/host/lowmem.cpp); .NET 8 rejects relative
  Assembly.LoadFile (detour in Compat.cs); Android .NET crypto needs a JVM (src/host/crypto.c).
- Memoria.ini on the device: [Audio] Backend = 2 (SoLoud).

## APK (M5)
- `bash tools/build_apk.sh` -> `android/app/build/outputs/apk/release/app-release.apk`, signed with the
  key in ~/.ff9recomp/keystore.properties (or FF7Recomp's). Contents: libff9.so, Mono's .so files,
  assets/runtime.zip (.NET 8 class libraries + FF9Engine.dll). No game files.
- On the device: Launcher -> Import game folder (SAF) copies the player's Memoria-installed FF9
  folder to Android/data/com.ff9recomp/files/game (skips x86, *.exe, Mono, Plugins) and sets
  [Audio] Backend = 2. The game runs in its own process (:game); log in files/ff9.log and logcat FF9.
- UnityEngine's internal calls are enumerated at run time from the player's UnityEngine.dll and
  bound to numbered thunks (tools/gen_thunks.py), so nothing game-derived is compiled in.
- Moguri Mod (~5 GB) is installed with Memoria's Mod Manager on the PC before importing; Memoria
  loads it from the game folder as on PC.

## Battle effects plugin (FF9SpecialEffectPlugin.dll)
- Memoria's SFXRework still calls the plugin every battle frame (`SFX_UpdateCamera`: the PSX
  battle camera, 13 floats) and for camera sequences, so battles need it.
- src/host/sfx_plugin.c loads the player's DLL from `x64/FF9_Data/Plugins` at run time:
  sections into an mmap, DIR64 relocations, each IAT slot pointed at a stub address that the
  interpreter hands to `host_call` (msvcr120/msvcp120/kernel32 calls implemented there), then
  DllMain. The game's `SFX.Callback` is a native function pointer and is called directly.
- src/host/x64emu.c: an x86-64 interpreter (guest addresses = host addresses; Windows x64
  calling convention; flags computed eagerly; SSE scalar/packed). Unknown opcodes stop the
  call with a log line (`x64emu: unknown ... at`), never crash.
- Nothing derived from the DLL is generated, stored or shipped.

## More debug switches (2026-11-14)
- `FF9_ARGS="-memoria-battle+1"`: extra game arguments (`+` separates; Memoria's launch options:
  `-memoria-battle N`, `-memoria-party a,b`, `-memoria-seed N`, `-memoria-autoload`).
- `FF9_MEMSTAT=n` (+`FF9_MEMSTAT_MAPS`, `FF9_MEMSTAT_TYPES`): memory, mappings, objects by type.
- `GLTRACE=1` build + `FF9_GL_TRACE`: GL call counts per report.
- `FF9_EXC_OFFSETS`: IL offsets in exception stacks. `FF9_MONO_OPTS="--optimize=-inline"`.
- `FF9_DUMP_RT=name` with `FF9_DUMP_FRAME`: render texture contents. `FF9_TRACE_DRAWTEX`.
- `FF9_SKIP_CAMERA=Name@depth`, `FF9_SKIP_SHADER`, `FF9_NO_RENDER_FROM`, `FF9_TRACE_SCENE`.
- `FF9_SFX_TRACE`, `FF9_SFX_STUB`, `FF9_SFX_PLUGIN=path`.

## Gotchas found
- Prefab assets must not run scripts or render (the MBG prefab's camera wiped the field).
- Shader values the shader doesn't declare in Properties come from the globals even when the
  material has a saved copy (field fades, `_MatrixRT`).
- Matrix4x4 is serialized by row (e00 e01 ...); UnityEngine.Matrix4x4's fields go by column.
- Screenshots must force alpha 255 (cameras clear with alpha 0.02).
- Renderer.material / MeshFilter.mesh copy once per renderer, not per call.
- Known: during the opening FMV (field 70, which has no walk mesh by design) the player's
  FieldMapActorController throws in MovePC every frame (walkMesh is null); the game carries on
  to the cargo hold after the movie. Not yet known whether Unity avoids it. Logged once (repeats
  of the same exception are collapsed: first 3, then one line per 1000).
- App: Mono on Android renames DllImport "__Internal" to "libmonodroid.so" (map it to the host).
- App input: a press stays visible to the game for at least 80 ms (taps shorter than a frame,
  several pad readers per frame).
- A component's name is its GameObject's (`transform.name`). Memoria finds bones with
  `GetChildByName` (battle targeting, smooth animation); components now copy the GameObject's
  name when created, cloned or renamed. Returning `g.Name` from the `Object::get_name` icall
  instead corrupted strings after a GC (not understood; avoid adding logic to that icall).
- `-memoria-battle 1` is too hard for a level-1 Zidane (enemies hit for 600-800); use
  `-memoria-seed 1` to get one turn. 302 is a scripted battle (no party input).
