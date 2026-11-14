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
