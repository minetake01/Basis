#pragma warning disable CS8500
#pragma warning disable CS8981

using System;
using System.Runtime.InteropServices;

namespace Luau.Native
{
    // malloc/free — stock libluau prebuilds do not export these symbols on Windows.
    // LuauCompiler bytecode buffers are allocated with the platform CRT; free must match.

    unsafe partial class NativeMethods
    {
#if (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN) && !UNITY_EDITOR_OSX
        const string MemoryDll = "ucrtbase";
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        const string MemoryDll = "libc";
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        const string MemoryDll = "libc.so.6";
#else
        const string MemoryDll = __DllName;
#endif

        [DllImport(MemoryDll, EntryPoint = "malloc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void* malloc(nuint size);

        [DllImport(MemoryDll, EntryPoint = "free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void free(void* ptr);
    }
}
