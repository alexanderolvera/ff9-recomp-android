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

void init() {
    void *p = mmap((void *)kBase, kArena, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS | MAP_FIXED_NOREPLACE | MAP_NORESERVE, -1, 0);
    if (p == MAP_FAILED || (uintptr_t)p + kArena > 0x7FFFFFFF) { fprintf(stderr, "lowmem: cannot reserve the low arena\n"); abort(); }
    arena = top = (char *)p;
    end = arena + kArena;
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
