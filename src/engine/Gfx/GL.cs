// OpenGL ES 3.0 and EGL bindings (only what the renderer uses).
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FF9Engine.Gfx;

public static unsafe class GL {
    const string L = "libGLESv3.so";

    public const uint DEPTH_BUFFER_BIT = 0x100, STENCIL_BUFFER_BIT = 0x400, COLOR_BUFFER_BIT = 0x4000;
    public const uint POINTS = 0, LINES = 1, LINE_STRIP = 3, TRIANGLES = 4, TRIANGLE_STRIP = 5;
    public const uint ZERO = 0, ONE = 1, SRC_COLOR = 0x300, ONE_MINUS_SRC_COLOR = 0x301, SRC_ALPHA = 0x302,
        ONE_MINUS_SRC_ALPHA = 0x303, DST_ALPHA = 0x304, ONE_MINUS_DST_ALPHA = 0x305, DST_COLOR = 0x306,
        ONE_MINUS_DST_COLOR = 0x307, SRC_ALPHA_SATURATE = 0x308;
    public const uint FUNC_ADD = 0x8006, FUNC_SUBTRACT = 0x800A, FUNC_REVERSE_SUBTRACT = 0x800B, MIN = 0x8007, MAX = 0x8008;
    public const uint NEVER = 0x200, LESS = 0x201, EQUAL = 0x202, LEQUAL = 0x203, GREATER = 0x204, NOTEQUAL = 0x205, GEQUAL = 0x206, ALWAYS = 0x207;
    public const uint FRONT = 0x404, BACK = 0x405, FRONT_AND_BACK = 0x408, CW = 0x900, CCW = 0x901;
    public const uint CULL_FACE = 0xB44, DEPTH_TEST = 0xB71, STENCIL_TEST = 0xB90, BLEND = 0xBE2, SCISSOR_TEST = 0xC11,
        POLYGON_OFFSET_FILL = 0x8037;
    public const uint TEXTURE_2D = 0xDE1, TEXTURE_CUBE_MAP = 0x8513, TEXTURE0 = 0x84C0;
    public const uint TEXTURE_MAG_FILTER = 0x2800, TEXTURE_MIN_FILTER = 0x2801, TEXTURE_WRAP_S = 0x2802, TEXTURE_WRAP_T = 0x2803;
    public const uint NEAREST = 0x2600, LINEAR = 0x2601, LINEAR_MIPMAP_LINEAR = 0x2703, NEAREST_MIPMAP_NEAREST = 0x2700, LINEAR_MIPMAP_NEAREST = 0x2701;
    public const uint REPEAT = 0x2901, CLAMP_TO_EDGE = 0x812F;
    public const uint RGBA = 0x1908, RGB = 0x1907, ALPHA = 0x1906, LUMINANCE = 0x1909, RED = 0x1903, RGBA8 = 0x8058, DEPTH_COMPONENT = 0x1902,
        DEPTH_COMPONENT24 = 0x81A6, DEPTH24_STENCIL8 = 0x88F0, DEPTH_STENCIL = 0x84F9, R8 = 0x8229;
    public const uint UNSIGNED_BYTE = 0x1401, UNSIGNED_SHORT = 0x1403, UNSIGNED_INT = 0x1405, FLOAT = 0x1406, UNSIGNED_INT_24_8 = 0x84FA;
    public const uint ARRAY_BUFFER = 0x8892, ELEMENT_ARRAY_BUFFER = 0x8893, STREAM_DRAW = 0x88E0, STATIC_DRAW = 0x88E4, DYNAMIC_DRAW = 0x88E8;
    public const uint FRAGMENT_SHADER = 0x8B30, VERTEX_SHADER = 0x8B31, COMPILE_STATUS = 0x8B81, LINK_STATUS = 0x8B82, INFO_LOG_LENGTH = 0x8B84;
    public const uint FRAMEBUFFER = 0x8D40, RENDERBUFFER = 0x8D41, COLOR_ATTACHMENT0 = 0x8CE0, DEPTH_ATTACHMENT = 0x8D00,
        DEPTH_STENCIL_ATTACHMENT = 0x821A, FRAMEBUFFER_COMPLETE = 0x8CD5, READ_FRAMEBUFFER = 0x8CA8, DRAW_FRAMEBUFFER = 0x8CA9;
    public const uint UNPACK_ALIGNMENT = 0xCF5, PACK_ALIGNMENT = 0xD05;
    public const uint KEEP = 0x1E00, REPLACE = 0x1E01, INCR = 0x1E02, DECR = 0x1E03, INVERT = 0x150A;

    [DllImport(L)] public static extern void glClear(uint mask);
    [DllImport(L)] public static extern void glClearColor(float r, float g, float b, float a);
    [DllImport(L)] public static extern void glClearDepthf(float d);
    [DllImport(L)] public static extern void glClearStencil(int s);
    [DllImport(L)] public static extern void glViewport(int x, int y, int w, int h);
    [DllImport(L)] public static extern void glScissor(int x, int y, int w, int h);
    [DllImport(L)] public static extern void glEnable(uint cap);
    [DllImport(L)] public static extern void glDisable(uint cap);
    [DllImport(L)] public static extern void glBlendFuncSeparate(uint sc, uint dc, uint sa, uint da);
    [DllImport(L)] public static extern void glBlendEquationSeparate(uint c, uint a);
    [DllImport(L)] public static extern void glDepthFunc(uint f);
    [DllImport(L)] public static extern void glDepthMask(byte m);
    [DllImport(L)] public static extern void glColorMask(byte r, byte g, byte b, byte a);
    [DllImport(L)] public static extern void glCullFace(uint m);
    [DllImport(L)] public static extern void glFrontFace(uint m);
    [DllImport(L)] public static extern void glPolygonOffset(float f, float u);
    [DllImport(L)] public static extern void glStencilFunc(uint f, int r, uint m);
    [DllImport(L)] public static extern void glStencilOp(uint sf, uint df, uint dp);
    [DllImport(L)] public static extern void glStencilMask(uint m);
    [DllImport(L)] public static extern uint glGetError();
    [DllImport(L)] public static extern void glFinish();
    [DllImport(L)] public static extern void glFlush();
    [DllImport(L)] public static extern void glPixelStorei(uint p, int v);
    [DllImport(L)] public static extern void glReadPixels(int x, int y, int w, int h, uint fmt, uint type, void* data);
    [DllImport(L)] static extern byte* glGetString(uint name);
    public static string GetString(uint n) => Marshal.PtrToStringAnsi((IntPtr)glGetString(n)) ?? "";

    [DllImport(L)] static extern void glGenTextures(int n, uint* t);
    [DllImport(L)] static extern void glDeleteTextures(int n, uint* t);
    [DllImport(L)] public static extern void glBindTexture(uint target, uint tex);
    [DllImport(L)] public static extern void glActiveTexture(uint unit);
    [DllImport(L)] public static extern void glTexImage2D(uint target, int level, int ifmt, int w, int h, int border, uint fmt, uint type, void* data);
    [DllImport(L)] public static extern void glTexSubImage2D(uint target, int level, int x, int y, int w, int h, uint fmt, uint type, void* data);
    [DllImport(L)] public static extern void glTexParameteri(uint target, uint p, int v);
    [DllImport(L)] public static extern void glGenerateMipmap(uint target);
    [DllImport(L)] public static extern void glCopyTexSubImage2D(uint target, int level, int xo, int yo, int x, int y, int w, int h);
    public static uint GenTexture() { uint t; glGenTextures(1, &t); return t; }
    public static void DeleteTexture(uint t) { if (t != 0) glDeleteTextures(1, &t); }

    [DllImport(L)] static extern void glGenBuffers(int n, uint* b);
    [DllImport(L)] static extern void glDeleteBuffers(int n, uint* b);
    [DllImport(L)] public static extern void glBindBuffer(uint target, uint b);
    [DllImport(L)] public static extern void glBufferData(uint target, IntPtr size, void* data, uint usage);
    [DllImport(L)] public static extern void glBufferSubData(uint target, IntPtr offset, IntPtr size, void* data);
    public static uint GenBuffer() { uint b; glGenBuffers(1, &b); return b; }
    public static void DeleteBuffer(uint b) { if (b != 0) glDeleteBuffers(1, &b); }

    [DllImport(L)] static extern void glGenVertexArrays(int n, uint* a);
    [DllImport(L)] public static extern void glBindVertexArray(uint a);
    public static uint GenVertexArray() { uint a; glGenVertexArrays(1, &a); return a; }
    [DllImport(L)] public static extern void glEnableVertexAttribArray(uint i);
    [DllImport(L)] public static extern void glDisableVertexAttribArray(uint i);
    [DllImport(L)] public static extern void glVertexAttribPointer(uint i, int size, uint type, byte norm, int stride, IntPtr offset);
    [DllImport(L)] public static extern void glVertexAttrib4f(uint i, float x, float y, float z, float w);
    [DllImport(L)] public static extern void glDrawArrays(uint mode, int first, int count);
    [DllImport(L)] public static extern void glDrawElements(uint mode, int count, uint type, IntPtr offset);

    [DllImport(L)] public static extern uint glCreateShader(uint type);
    [DllImport(L)] static extern void glShaderSource(uint s, int count, byte** str, int* len);
    [DllImport(L)] public static extern void glCompileShader(uint s);
    [DllImport(L)] static extern void glGetShaderiv(uint s, uint p, int* v);
    [DllImport(L)] static extern void glGetShaderInfoLog(uint s, int max, int* len, byte* log);
    [DllImport(L)] public static extern void glDeleteShader(uint s);
    [DllImport(L)] public static extern uint glCreateProgram();
    [DllImport(L)] public static extern void glAttachShader(uint p, uint s);
    [DllImport(L)] static extern void glBindAttribLocation(uint p, uint i, byte* name);
    [DllImport(L)] public static extern void glLinkProgram(uint p);
    [DllImport(L)] static extern void glGetProgramiv(uint p, uint pn, int* v);
    [DllImport(L)] static extern void glGetProgramInfoLog(uint p, int max, int* len, byte* log);
    [DllImport(L)] public static extern void glUseProgram(uint p);
    [DllImport(L)] public static extern void glDeleteProgram(uint p);
    [DllImport(L)] static extern int glGetUniformLocation(uint p, byte* name);
    [DllImport(L)] public static extern void glUniform1i(int loc, int v);
    [DllImport(L)] public static extern void glUniform4fv(int loc, int count, float* v);
    [DllImport(L)] public static extern void glUniform4iv(int loc, int count, int* v);
    [DllImport(L)] public static extern void glUniform1iv(int loc, int count, int* v);

    [DllImport(L)] static extern void glGenFramebuffers(int n, uint* f);
    [DllImport(L)] static extern void glDeleteFramebuffers(int n, uint* f);
    [DllImport(L)] public static extern void glBindFramebuffer(uint target, uint f);
    [DllImport(L)] public static extern void glFramebufferTexture2D(uint target, uint att, uint textarget, uint tex, int level);
    [DllImport(L)] public static extern void glFramebufferRenderbuffer(uint target, uint att, uint rbt, uint rb);
    [DllImport(L)] public static extern uint glCheckFramebufferStatus(uint target);
    [DllImport(L)] static extern void glGenRenderbuffers(int n, uint* r);
    [DllImport(L)] static extern void glDeleteRenderbuffers(int n, uint* r);
    [DllImport(L)] public static extern void glBindRenderbuffer(uint target, uint r);
    [DllImport(L)] public static extern void glRenderbufferStorage(uint target, uint fmt, int w, int h);
    [DllImport(L)] public static extern void glBlitFramebuffer(int sx0, int sy0, int sx1, int sy1, int dx0, int dy0, int dx1, int dy1, uint mask, uint filter);
    public static uint GenFramebuffer() { uint f; glGenFramebuffers(1, &f); return f; }
    public static void DeleteFramebuffer(uint f) { if (f != 0) glDeleteFramebuffers(1, &f); }
    public static uint GenRenderbuffer() { uint r; glGenRenderbuffers(1, &r); return r; }
    public static void DeleteRenderbuffer(uint r) { if (r != 0) glDeleteRenderbuffers(1, &r); }

    public static int UniformLocation(uint p, string name) {
        var b = Encoding.ASCII.GetBytes(name + "\0");
        fixed (byte* pb = b) return glGetUniformLocation(p, pb);
    }

    public static void BindAttrib(uint p, uint i, string name) {
        var b = Encoding.ASCII.GetBytes(name + "\0");
        fixed (byte* pb = b) glBindAttribLocation(p, i, pb);
    }

    public static uint CompileShader(uint type, string src, out string? error) {
        uint s = glCreateShader(type);
        var b = Encoding.UTF8.GetBytes(src);
        fixed (byte* pb = b) {
            byte* p = pb;
            int len = b.Length;
            glShaderSource(s, 1, &p, &len);
        }
        glCompileShader(s);
        int ok;
        glGetShaderiv(s, COMPILE_STATUS, &ok);
        error = null;
        if (ok == 0) {
            var log = new byte[4096];
            int n;
            fixed (byte* pl = log) glGetShaderInfoLog(s, log.Length, &n, pl);
            error = Encoding.UTF8.GetString(log, 0, n);
            glDeleteShader(s);
            return 0;
        }
        return s;
    }

    public static bool LinkProgram(uint p, out string? error) {
        glLinkProgram(p);
        int ok;
        glGetProgramiv(p, LINK_STATUS, &ok);
        error = null;
        if (ok != 0) return true;
        var log = new byte[4096];
        int n;
        fixed (byte* pl = log) glGetProgramInfoLog(p, log.Length, &n, pl);
        error = Encoding.UTF8.GetString(log, 0, n);
        return false;
    }
}

public static unsafe class EGL {
    const string L = "libEGL.so";
    public const int SURFACE_TYPE = 0x3033, PBUFFER_BIT = 0x1, WINDOW_BIT = 0x4, RENDERABLE_TYPE = 0x3040, OPENGL_ES3_BIT = 0x40,
        RED_SIZE = 0x3024, GREEN_SIZE = 0x3023, BLUE_SIZE = 0x3022, ALPHA_SIZE = 0x3021, DEPTH_SIZE = 0x3025, STENCIL_SIZE = 0x3026,
        NONE = 0x3038, WIDTH = 0x3057, HEIGHT = 0x3056, CONTEXT_CLIENT_VERSION = 0x3098, OPENGL_ES_API = 0x30A0;

    [DllImport(L)] public static extern IntPtr eglGetDisplay(IntPtr id);
    [DllImport(L)] public static extern uint eglInitialize(IntPtr dpy, int* major, int* minor);
    [DllImport(L)] public static extern uint eglBindAPI(uint api);
    [DllImport(L)] public static extern uint eglChooseConfig(IntPtr dpy, int* attrs, IntPtr* configs, int size, int* num);
    [DllImport(L)] public static extern IntPtr eglCreateContext(IntPtr dpy, IntPtr cfg, IntPtr share, int* attrs);
    [DllImport(L)] public static extern IntPtr eglCreatePbufferSurface(IntPtr dpy, IntPtr cfg, int* attrs);
    [DllImport(L)] public static extern IntPtr eglCreateWindowSurface(IntPtr dpy, IntPtr cfg, IntPtr win, int* attrs);
    [DllImport(L)] public static extern uint eglMakeCurrent(IntPtr dpy, IntPtr draw, IntPtr read, IntPtr ctx);
    [DllImport(L)] public static extern uint eglSwapBuffers(IntPtr dpy, IntPtr surf);
    [DllImport(L)] public static extern uint eglSwapInterval(IntPtr dpy, int interval);
    [DllImport(L)] public static extern int eglGetError();
    [DllImport(L)] public static extern uint eglQuerySurface(IntPtr dpy, IntPtr surf, int attr, int* value);
    [DllImport(L)] public static extern uint eglDestroySurface(IntPtr dpy, IntPtr surf);
}
