using CodeWalker.GameFiles;

namespace Duble.Core.Formats;

/// <summary>
/// One-time setup of the CodeWalker library: always read in gen9 mode.
///
/// In gen9 mode CodeWalker recognises the format of every file from its RSC7 header (ydd 165/159, ytd 13/5) and
/// reads Legacy correctly as well; legacy mode does NOT read gen9 — a gen9 uppr_015_r.ydd throws "illegal
/// position". So the flag is set once and never changed, which leaves no race between indexing and preview.
/// The Legacy/Enhanced label of a file comes from that file's own header (Rsc7Header.IsEnhanced), not from the reading mode.
///
/// This used to run from a [ModuleInitializer], which meant that merely loading the assembly mutated a global.
/// It now runs when the container is built, or on the first call from an entry point.
/// </summary>
public sealed class CodeWalkerRuntime
{
    static readonly object gate = new();
    static bool initialized;

    public CodeWalkerRuntime() => Initialize();

    /// <summary>
    /// Idempotent and safe to call from any thread. Under the lock rather than a compare-and-swap, because a
    /// caller that loses the race must not return before the flag is actually set: the next thing it does is
    /// read a game file.
    /// </summary>
    public static void Initialize()
    {
        lock (gate)
        {
            if (initialized) return;
            RpfManager.IsGen9 = true;
            initialized = true;
        }
    }
}
