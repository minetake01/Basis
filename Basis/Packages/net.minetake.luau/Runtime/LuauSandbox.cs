using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau.Unity
{
    public static unsafe class LuauSandbox
    {
        public static void ApplyRoot(Luau.LuauState state)
        {
            if (state == null)
            {
                throw new System.ArgumentNullException(nameof(state));
            }

            luaL_sandbox(state.AsPointer());
        }

        public static void ApplyThread(Luau.LuauState thread)
        {
            if (thread == null)
            {
                throw new System.ArgumentNullException(nameof(thread));
            }

            luaL_sandboxthread(thread.AsPointer());
        }
    }
}
