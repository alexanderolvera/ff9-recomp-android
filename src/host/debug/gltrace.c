/* Debug build only (GLTRACE=1 bash tools/build_host.sh): counts calls to the GL entry points
   that allocate driver memory, so leaks can be traced to a call. The engine binds GL to the
   host when FF9_GL_TRACE is set; ff9_gl_stats prints and resets the counts. */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <stdio.h>
#include <string.h>
#include <GLES3/gl3.h>

#define N 32
static const char *names[N];
static long counts[N];
static int used;

static int slot(const char *n) {
    for (int i = 0; i < used; i++) if (names[i] == n) return i;
    names[used] = n;
    return used++;
}

#define WRAP(ret, name, params, args) \
    ret name params { \
        static ret (*real) params; \
        static int s = -1; \
        if (!real) { real = dlsym(RTLD_NEXT, #name); s = slot(#name); } \
        counts[s]++; \
        return real args; \
    }
#define WRAPV(name, params, args) \
    void name params { \
        static void (*real) params; \
        static int s = -1; \
        if (!real) { real = dlsym(RTLD_NEXT, #name); s = slot(#name); } \
        counts[s]++; \
        real args; \
    }

WRAPV(glTexImage2D, (GLenum a, GLint b, GLint c, GLsizei d, GLsizei e, GLint f, GLenum g, GLenum h, const void *i), (a, b, c, d, e, f, g, h, i))
WRAPV(glTexSubImage2D, (GLenum a, GLint b, GLint c, GLint d, GLsizei e, GLsizei f, GLenum g, GLenum h, const void *i), (a, b, c, d, e, f, g, h, i))
WRAPV(glBufferData, (GLenum a, GLsizeiptr b, const void *c, GLenum d), (a, b, c, d))
WRAPV(glGenTextures, (GLsizei a, GLuint *b), (a, b))
WRAPV(glGenBuffers, (GLsizei a, GLuint *b), (a, b))
WRAPV(glGenFramebuffers, (GLsizei a, GLuint *b), (a, b))
WRAPV(glGenRenderbuffers, (GLsizei a, GLuint *b), (a, b))
WRAPV(glGenVertexArrays, (GLsizei a, GLuint *b), (a, b))
WRAPV(glDeleteTextures, (GLsizei a, const GLuint *b), (a, b))
WRAPV(glDeleteBuffers, (GLsizei a, const GLuint *b), (a, b))
WRAPV(glRenderbufferStorage, (GLenum a, GLenum b, GLsizei c, GLsizei d), (a, b, c, d))
WRAPV(glGenerateMipmap, (GLenum a), (a))
WRAPV(glReadPixels, (GLint a, GLint b, GLsizei c, GLsizei d, GLenum e, GLenum f, void *g), (a, b, c, d, e, f, g))
WRAPV(glDrawElements, (GLenum a, GLsizei b, GLenum c, const void *d), (a, b, c, d))
WRAPV(glClear, (GLbitfield a), (a))
WRAPV(glBindFramebuffer, (GLenum a, GLuint b), (a, b))
WRAPV(glFlush, (void), ())
WRAPV(glFinish, (void), ())
WRAPV(glCompressedTexImage2D, (GLenum a, GLint b, GLenum c, GLsizei d, GLsizei e, GLint f, GLsizei g, const void *h), (a, b, c, d, e, f, g, h))
WRAP(GLuint, glCreateShader, (GLenum a), (a))
WRAP(GLuint, glCreateProgram, (void), ())

void ff9_gl_stats(void) {
    char line[1024]; int o = 0;
    for (int i = 0; i < used; i++) { o += snprintf(line + o, sizeof line - o, "%s %ld, ", names[i] + 2, counts[i]); counts[i] = 0; }
    printf("   gl: %s\n", line);
    fflush(stdout);
}
