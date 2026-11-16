// C++ allocations (SoLoud objects) come from a region below 2 GB: Memoria converts SoLoud object
// handles to int (as sound IDs), which only works on PC because Windows heap addresses there are low.
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <new>
#include <sys/mman.h>

namespace {
constexpr size_t kArena = 512u << 20;          // 512 MB reserved, committed on demand by the kernel
constexpr uintptr_t kBase = 0x40000000;        // 1 GB: below INT_MAX
constexpr size_t kClasses = 64;                 // 16-byte classes up to 1 KB, then power-of-two buckets

struct Header { size_t size; size_t pad; };    // 16 bytes, keeps 16-byte alignment

std::mutex lock;
char *arena, *top, *end;
void *freeList[kClasses + 32];

// The region exactly at base, or null: taken already, or a kernel that treats the address as a hint.
void *tryAt(uintptr_t base, size_t size) {
    void *p = mmap((void *)base, size, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS | MAP_FIXED_NOREPLACE | MAP_NORESERVE, -1, 0);
    if (p == MAP_FAILED) return nullptr;
    if ((uintptr_t)p != base) { munmap(p, size); return nullptr; }
    return p;
}

// What lives below 2 GB, for the report when no arena fits.
void dumpLowMaps() {
    FILE *f = fopen("/proc/self/maps", "r");
    if (!f) return;
    char line[512];
    while (fgets(line, sizeof line, f)) if (strtoull(line, nullptr, 16) < 0x80000000ull) fprintf(stderr, "lowmem: in use %s", line);
    fclose(f);
}

void init() {
    // FF9_TEST_LOWMEM_TAKEN: hold the usual address first, as on devices where something lives there
    // already (a Pixel 5: GitHub issue 2)
    if (getenv("FF9_TEST_LOWMEM_TAKEN"))
        mmap((void *)kBase, 64u << 20, PROT_NONE, MAP_PRIVATE | MAP_ANONYMOUS | MAP_FIXED_NOREPLACE | MAP_NORESERVE, -1, 0);
    // 1 GB as before, else any free 64 MB-aligned place between 256 MB and 2 GB, else a smaller arena;
    // every address stays below INT_MAX
    constexpr uintptr_t kLow = 0x10000000, kTop = 0x80000000, kStep = 64u << 20;
    for (size_t size = kArena; size >= (64u << 20); size >>= 1) {
        void *p = tryAt(kBase, size);
        for (uintptr_t b = kLow; !p && b + size <= kTop; b += kStep) if (b != kBase) p = tryAt(b, size);
        if (p) {
            if ((uintptr_t)p != kBase || size != kArena) fprintf(stderr, "lowmem: arena at %p, %zu MB\n", p, size >> 20);
            arena = top = (char *)p;
            end = arena + size;
            return;
        }
    }
    fprintf(stderr, "lowmem: cannot reserve the low arena (no free 64 MB below 2 GB)\n");
    dumpLowMaps();
    abort();
}

int bucket(size_t n, size_t &rounded) {
    if (n <= kClasses * 16) { rounded = (n + 15) & ~size_t(15); return (int)(rounded / 16) - 1; }
    int b = 0;
    size_t s = 2048;
    while (s < n) { s <<= 1; b++; }
    rounded = s;
    return (int)kClasses + b;
}

void *lowAlloc(size_t n) {
    if (n == 0) n = 1;
    std::lock_guard<std::mutex> g(lock);
    if (!arena) init();
    size_t rounded;
    int b = bucket(n, rounded);
    if (b >= (int)(kClasses + 32)) return nullptr;
    Header *h;
    if (freeList[b]) {
        h = (Header *)freeList[b];
        freeList[b] = *(void **)(h + 1);
    } else {
        if (top + sizeof(Header) + rounded > end) return nullptr;
        h = (Header *)top;
        top += sizeof(Header) + rounded;
        h->size = rounded;
    }
    return h + 1;
}

void lowFree(void *p) {
    if (!p) return;
    Header *h = (Header *)p - 1;
    std::lock_guard<std::mutex> g(lock);
    size_t rounded;
    int b = bucket(h->size, rounded);
    *(void **)p = freeList[b];
    freeList[b] = h;
}
}  // namespace

void *operator new(size_t n) { void *p = lowAlloc(n); if (!p) throw std::bad_alloc(); return p; }
void *operator new[](size_t n) { void *p = lowAlloc(n); if (!p) throw std::bad_alloc(); return p; }
void *operator new(size_t n, const std::nothrow_t &) noexcept { return lowAlloc(n); }
void *operator new[](size_t n, const std::nothrow_t &) noexcept { return lowAlloc(n); }
void operator delete(void *p) noexcept { lowFree(p); }
void operator delete[](void *p) noexcept { lowFree(p); }
void operator delete(void *p, size_t) noexcept { lowFree(p); }
void operator delete[](void *p, size_t) noexcept { lowFree(p); }
