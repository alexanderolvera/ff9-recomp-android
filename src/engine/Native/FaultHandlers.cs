// Mono's SIGSEGV/SIGBUS handlers make a null dereference in managed code a NullReferenceException;
// something a mod loads (The Lost Chapters, GitHub issue 14) resets them, and the game's next null
// reference killed the process. The host keeps Mono's and puts them back (ff9_keep_fault_handlers).
using System.Runtime.InteropServices;

namespace FF9Engine.Native;

public static class FaultHandlers {
    [DllImport("__Internal")] static extern int ff9_keep_fault_handlers();
    static bool told;

    public static void Tick() {
        int fixedMask = ff9_keep_fault_handlers();
        if (fixedMask != 0 && !told) {
            told = true;
            Log.Warn($"the runtime's fault handlers had been reset ({((fixedMask & 1) != 0 ? "SIGSEGV " : "")}{((fixedMask & 2) != 0 ? "SIGBUS" : "")}): put back");
        }
    }
}
