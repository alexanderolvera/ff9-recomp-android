/* JNI entry points for the APK (com.ff9recomp.GameActivity), built only into libff9.so
 * (FF9_LIBRARY). The game runs on the thread that calls nativeRun. */
#ifdef FF9_LIBRARY
#include <android/log.h>
#include <android/native_window_jni.h>
#include <jni.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

int ff9_main(int argc, char **argv);
extern void *ff9_native_window;
void ff9_pad_set(uint16_t buttons, uint8_t lt, uint8_t rt, int16_t lx, int16_t ly, int16_t rx, int16_t ry);

/* stdout/stderr -> logcat (tag FF9) and a log file next to the game */
static int log_pipe[2];
static void *log_thread(void *arg) {
    char buf[1024];
    FILE *f = (FILE *)arg;
    ssize_t n;
    char line[2048];
    size_t len = 0;
    while ((n = read(log_pipe[0], buf, sizeof buf)) > 0) {
        for (ssize_t i = 0; i < n; i++) {
            if (buf[i] == '\n' || len == sizeof line - 1) {
                line[len] = 0;
                __android_log_write(ANDROID_LOG_INFO, "FF9", line);
                if (f) { fputs(line, f); fputc('\n', f); fflush(f); }
                len = 0;
            } else line[len++] = buf[i];
        }
    }
    return NULL;
}

JNIEXPORT void JNICALL Java_com_ff9recomp_GameActivity_nativeSetSurface(JNIEnv *env, jclass cls, jobject surface) {
    if (ff9_native_window) { ANativeWindow_release(ff9_native_window); ff9_native_window = NULL; }
    if (surface) ff9_native_window = ANativeWindow_fromSurface(env, surface);
}

JNIEXPORT jint JNICALL Java_com_ff9recomp_GameActivity_nativeRun(JNIEnv *env, jclass cls, jstring jdir, jstring jlog) {
    const char *dir = (*env)->GetStringUTFChars(env, jdir, NULL);
    const char *logp = (*env)->GetStringUTFChars(env, jlog, NULL);
    FILE *lf = fopen(logp, "w");
    setvbuf(stdout, NULL, _IOLBF, 0);
    pipe(log_pipe);
    dup2(log_pipe[1], 1);
    dup2(log_pipe[1], 2);
    pthread_t t;
    pthread_create(&t, NULL, log_thread, lf);
    char *args[] = {"ff9", strdup(dir), "play", NULL};
    (*env)->ReleaseStringUTFChars(env, jdir, dir);
    (*env)->ReleaseStringUTFChars(env, jlog, logp);
    return ff9_main(3, args);
}

JNIEXPORT void JNICALL Java_com_ff9recomp_GameActivity_nativePad(JNIEnv *env, jclass cls, jint buttons, jint lt, jint rt, jint lx, jint ly, jint rx, jint ry) {
    ff9_pad_set((uint16_t)buttons, (uint8_t)lt, (uint8_t)rt, (int16_t)lx, (int16_t)ly, (int16_t)rx, (int16_t)ry);
}
#endif
