/* FF9 Mono test host (headless, run over adb shell).
 *
 *   ff9host <dir> jit            load every game assembly and JIT-compile all its methods
 *   ff9host <dir> run <Type> <Method>  call a static method / MonoBehaviour entry
 *
 * <dir>: bcl/ (.NET 8 Mono BCL + native libs), managed/ (the game's DLLs),
 *        tokens/<asm>.txt (from gen_host_tables.py).
 * Every UnityEngine internal call is a named stub: the first one reached prints its name and
 * the managed stack, then exits 93 (the deliberate STOP, as in FF7Recomp's bring-up).
 */
#include <dirent.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include <mono/jit/jit.h>
#include <mono/jit/mono-private-unstable.h>
#include <mono/metadata/appdomain.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/reflection.h>
#include <mono/utils/mono-publib.h>
#include <mono/metadata/class.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/metadata/exception.h>
#include <mono/metadata/image.h>
#include <mono/metadata/loader.h>
#include <mono/metadata/object.h>

/* exported by libmonosgen, not in the public headers: the host thread must be in a GC-unsafe
   (managed-running) region to call the object APIs that allocate */
void *mono_threads_enter_gc_unsafe_region(void **stackdata);
void mono_threads_exit_gc_unsafe_region(void *cookie, void **stackdata);
/* cooperative = 1: Mono calls the function without a GC-safe transition (our engine functions
   never block and may allocate managed objects) */
void mono_add_internal_call_with_flags(const char *name, const void *method, int cooperative);
#define ICALL(name, fn) mono_add_internal_call_with_flags(name, (const void *)(fn), 1)

extern const int ue_icall_count;
extern const char *const ue_icall_names[];
extern const void *const ue_icall_fns[];

static MonoDomain *g_domain;

static double now(void) {
    struct timespec t;
    clock_gettime(CLOCK_MONOTONIC, &t);
    return t.tv_sec + t.tv_nsec / 1e9;
}

static void print_managed_stack(void) {
    MonoImage *corlib = mono_get_corlib();
    MonoClass *env = mono_class_from_name(corlib, "System", "Environment");
    MonoMethod *get = mono_class_get_method_from_name(env, "get_StackTrace", 0);
    MonoObject *exc = NULL;
    MonoObject *s = mono_runtime_invoke(get, NULL, NULL, &exc);
    if (s && !exc) {
        char *u = mono_string_to_utf8((MonoString *)s);
        printf("%s\n", u);
        mono_free(u);
    }
}

static int g_stop_on_icall = 1;      /* jit mode: log each engine call once, return 0, carry on */
static unsigned char g_icall_seen[8192];

void ue_icall_hit(int index) {
    if (!g_stop_on_icall) {
        if (!g_icall_seen[index]++) printf("  engine call (stubbed): %s\n", ue_icall_names[index]);
        return;
    }
    printf("STOP: UnityEngine internal call %s\n", ue_icall_names[index]);
    print_managed_stack();
    fflush(stdout);
    exit(93);
}

static char *join_dir(const char *dir, const char *ext, const char *const *skip, char *acc) {
    DIR *d = opendir(dir);
    struct dirent *e;
    if (!d) return acc;
    while ((e = readdir(d))) {
        size_t n = strlen(e->d_name);
        if (n < 5 || strcmp(e->d_name + n - 4, ext)) continue;
        int skipped = 0;
        for (const char *const *s = skip; s && *s; s++)
            if (!strcmp(e->d_name, *s)) skipped = 1;
        if (skipped) continue;
        size_t l = acc ? strlen(acc) : 0;
        acc = realloc(acc, l + strlen(dir) + n + 3);
        sprintf(acc + l, "%s%s/%s", l ? ":" : "", dir, e->d_name);
    }
    closedir(d);
    return acc;
}

static void unhandled(MonoObject *exc, void *ud) {
    MonoString *s = mono_object_to_string(exc, NULL);
    char *u = s ? mono_string_to_utf8(s) : NULL;
    printf("UNHANDLED: %s\n", u ? u : "?");
    fflush(stdout);
    exit(2);
}

static const char *exc_text(MonoObject *exc) {
    MonoObject *e2 = NULL;
    MonoString *s = mono_object_to_string(exc, &e2);
    return s && !e2 ? mono_string_to_utf8(s) : "(exception without text)";
}

/* Try the JIT; on failure ask RuntimeHelpers.PrepareMethod for the reason. */
static const char *prepare(MonoMethod *m) {
    if (mono_compile_method(m)) return NULL;
    if (!getenv("FF9_WHY")) return "mono_compile_method returned NULL";
    static MonoMethod *prep, *handle_get;
    if (!prep) {
        MonoImage *corlib = mono_get_corlib();
        MonoClass *rh = mono_class_from_name(corlib, "System.Runtime.CompilerServices", "RuntimeHelpers");
        MonoClass *mb = mono_class_from_name(corlib, "System.Reflection", "MethodBase");
        MonoMethodDesc *d = mono_method_desc_new(":PrepareMethod(System.RuntimeMethodHandle)", 0);
        prep = mono_method_desc_search_in_class(d, rh);
        handle_get = mono_class_get_method_from_name(mb, "get_MethodHandle", 0);
    }
    MonoReflectionMethod *rm = mono_method_get_object(g_domain, m, NULL);
    MonoObject *exc = NULL;
    MonoObject *h = mono_runtime_invoke(handle_get, rm, NULL, &exc);
    if (exc) return exc_text(exc);
    void *args[1] = {mono_object_unbox(h)};
    mono_runtime_invoke(prep, NULL, args, &exc);
    return exc ? exc_text(exc) : "compile failed, PrepareMethod succeeded";
}

static const char *ASMS[] = {"Mono.Security", "UnityEngine", "UnityEngine.UI", "UnityEngine.Networking",
                             "Assembly-CSharp-firstpass", "XInputDotNetPure", "ICSharpCode.SharpZipLib",
                             "Newtonsoft.Json", "Memoria.Prime", "Assembly-CSharp", NULL};

static int cmd_jit(const char *dir) {
    int total = 0, fails = 0;
    double t0 = now();
    for (const char *const *an = ASMS; *an; an++) {
        MonoAssemblyName *aname = mono_assembly_name_new(*an);
        MonoImageOpenStatus st;
        MonoAssembly *a = mono_assembly_load(aname, NULL, &st);
        if (!a) { printf("[%s] load FAILED (status %d)\n", *an, st); fails++; continue; }
        MonoImage *img = mono_assembly_get_image(a);
        char path[1024];
        snprintf(path, sizeof path, "%s/tokens/%s.txt", dir, *an);
        FILE *f = fopen(path, "r");
        if (!f) { printf("[%s] no token list\n", *an); continue; }
        unsigned tok;
        int n = 0, bad = 0;
        double t = now();
        const char *only = getenv("FF9_ONLY");        /* FF9_ONLY=<asm>[:<token>] */
        if (only && strncmp(only, *an, strlen(*an))) { fclose(f); continue; }
        unsigned only_tok = only && strchr(only, ':') ? strtoul(strchr(only, ':') + 1, NULL, 16) : 0;
        while (fscanf(f, "%x", &tok) == 1) {
            if (only_tok && tok != only_tok) continue;
            n++;
            if (getenv("FF9_TRACE")) { printf("  > %08x\n", tok); fflush(stdout); }
            MonoMethod *m = mono_get_method(img, tok, NULL);
            const char *why = m ? prepare(m) : "mono_get_method failed";
            if (why) {
                bad++;
                if (bad <= 25 || getenv("FF9_ALLFAILS")) {
                    char *nm = m ? mono_method_full_name(m, 1) : NULL;
                    printf("  FAIL %08x %s: %.300s\n", tok, nm ? nm : "?", why);
                }
            }
        }
        fclose(f);
        printf("[%s] %d methods, %d failed, %.1f s\n", *an, n, bad, now() - t);
        fflush(stdout);
        total += n;
        fails += bad;
    }
    printf("JIT total: %d methods, %d failed, %.1f s\n", total, fails, now() - t0);
    return fails ? 1 : 0;
}

static int cmd_run(const char *type, const char *method) {
    MonoAssemblyName *aname = mono_assembly_name_new("Assembly-CSharp");
    MonoImageOpenStatus st;
    MonoAssembly *a = mono_assembly_load(aname, NULL, &st);
    MonoImage *img = mono_assembly_get_image(a);
    const char *dot = strrchr(type, '.');
    char ns[256] = "";
    if (dot) { memcpy(ns, type, dot - type); ns[dot - type] = 0; }
    MonoClass *c = mono_class_from_name(img, ns, dot ? dot + 1 : type);
    if (!c) { printf("no type %s\n", type); return 1; }
    MonoMethod *m = mono_class_get_method_from_name(c, method, -1);
    if (!m) { printf("no method %s\n", method); return 1; }
    MonoObject *obj = NULL;
    if (!(mono_method_get_flags(m, NULL) & 0x10 /* static */)) {
        obj = mono_object_new(g_domain, c);      /* a MonoBehaviour as Unity makes it: no ctor run yet */
        mono_runtime_object_init(obj);
    }
    MonoObject *exc = NULL;
    printf("calling %s::%s\n", type, method);
    fflush(stdout);
    mono_runtime_invoke(m, obj, NULL, &exc);
    if (exc) { printf("EXCEPTION: %s\n", exc_text(exc)); return 1; }
    printf("returned\n");
    return 0;
}

/* ---- the first real engine calls: Application ---- */
static char g_root[1024];

static MonoString *str(const char *s) { return mono_string_new(g_domain, s); }
static MonoString *app_dataPath(void) {
    char p[1100]; snprintf(p, sizeof p, "%s/x64/FF9_Data", g_root); return str(p);
}
static MonoString *app_streamingAssetsPath(void) {
    char p[1100]; snprintf(p, sizeof p, "%s/x64/FF9_Data/StreamingAssets", g_root); return str(p);
}
static MonoString *app_persistentDataPath(void) {
    char p[1100]; snprintf(p, sizeof p, "%s/persistent", g_root); return str(p);
}
static MonoString *app_temporaryCachePath(void) {
    char p[1100]; snprintf(p, sizeof p, "%s/cache", g_root); return str(p);
}
static int app_platform(void) { return 2; }   /* RuntimePlatform.WindowsPlayer: Memoria's PC paths */
static MonoString *app_unityVersion(void) { return str("5.2.3p2"); }
static int app_isPlaying(void) { return 1; }

static void mb_ctor(MonoObject *self) { (void)self; }   /* native object made by the scene loader */
static void debug_log(int level, MonoString *msg, MonoObject *ctx) {
    char *u = msg ? mono_string_to_utf8(msg) : NULL;
    printf("[Unity log %d] %s\n", level, u ? u : "");
    mono_free(u);
}

static void register_engine(void) {
    ICALL("UnityEngine.MonoBehaviour::.ctor", mb_ctor);
    ICALL("UnityEngine.Debug::Internal_Log", debug_log);
    ICALL("UnityEngine.Application::get_dataPath", app_dataPath);
    ICALL("UnityEngine.Application::get_streamingAssetsPath", app_streamingAssetsPath);
    ICALL("UnityEngine.Application::get_persistentDataPath", app_persistentDataPath);
    ICALL("UnityEngine.Application::get_temporaryCachePath", app_temporaryCachePath);
    ICALL("UnityEngine.Application::get_platform", app_platform);
    ICALL("UnityEngine.Application::get_unityVersion", app_unityVersion);
    ICALL("UnityEngine.Application::get_isPlaying", app_isPlaying);
}

int main(int argc, char **argv) {
    if (argc < 3) { fprintf(stderr, "usage: ff9host <dir> jit | run <Type> <Method>\n"); return 64; }
    setvbuf(stdout, NULL, _IOLBF, 0);
    const char *dir = argv[1];
    char bcl[1024], managed[1024];
    snprintf(bcl, sizeof bcl, "%s/bcl", dir);
    snprintf(managed, sizeof managed, "%s/managed", dir);
    /* the game's own mscorlib/System/System.Core are Unity's .NET 3.5 profile: .NET 8's facades replace them */
    static const char *const skip_game[] = {"mscorlib.dll", "System.dll", "System.Core.dll",
                                            "System.Runtime.Serialization.dll", NULL};
    char *tpa = join_dir(bcl, ".dll", NULL, NULL);
    tpa = join_dir(managed, ".dll", skip_game, tpa);
    const char *keys[] = {"TRUSTED_PLATFORM_ASSEMBLIES", "NATIVE_DLL_SEARCH_DIRECTORIES",
                          "System.Globalization.Invariant", "APP_CONTEXT_BASE_DIRECTORY",
                          "System.Resources.UseSystemResourceKeys"};
    const char *vals[] = {tpa, bcl, "true", managed, "true"};
    double t0 = now();
    int r = monovm_initialize(5, keys, vals);
    if (r) { printf("monovm_initialize failed %d\n", r); return 1; }
    g_domain = mono_jit_init_version("ff9", "v4.0.30319");
    if (!g_domain) { printf("mono_jit_init failed\n"); return 1; }
    mono_install_unhandled_exception_hook(unhandled, NULL);
    for (int i = 0; i < ue_icall_count; i++)
        ICALL(ue_icall_names[i], ue_icall_fns[i]);
    printf("mono up in %.2f s, %d UnityEngine internal calls registered as stubs\n", now() - t0, ue_icall_count);

    /* the game runs from its root folder (Memoria reads Memoria.ini, StreamingAssets/... relative) */
    snprintf(g_root, sizeof g_root, "%s/game", dir);
    if (chdir(g_root)) { printf("no game folder %s\n", g_root); return 1; }
    register_engine();

    if (!strcmp(argv[2], "jit")) { g_stop_on_icall = 0; return cmd_jit(dir); }
    if (!strcmp(argv[2], "run") && argc >= 5) return cmd_run(argv[3], argv[4]);
    fprintf(stderr, "unknown command\n");
    return 64;
}
