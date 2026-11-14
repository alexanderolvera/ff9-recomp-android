/* Detours for .NET Framework -> .NET 8 compatibility shims (FF9Engine Compat.cs): overwrite the
 * start of a JIT-compiled method with "ldr x16, #8; br x16; .quad target". */
#include <stdint.h>
#include <string.h>
#include <sys/mman.h>
#include <unistd.h>

__attribute__((visibility("default"))) int ff9_jump_patch(void *from, void *to) {
    uintptr_t page = (uintptr_t)from & ~(uintptr_t)(getpagesize() - 1);
    size_t len = ((uintptr_t)from + 16 - page + getpagesize() - 1) & ~(size_t)(getpagesize() - 1);
    if (mprotect((void *)page, len, PROT_READ | PROT_WRITE | PROT_EXEC) != 0) return -1;
    uint32_t code[4] = {0x58000050u, 0xD61F0200u, (uint32_t)(uintptr_t)to, (uint32_t)((uintptr_t)to >> 32)};
    memcpy(from, code, sizeof code);
    __builtin___clear_cache((char *)from, (char *)from + sizeof code);
    return 0;
}
