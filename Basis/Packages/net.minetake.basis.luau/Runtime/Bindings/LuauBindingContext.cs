namespace Minetake.Basis.Luau.Bindings
{
    /// <summary>
    /// Thread-scoped host pointer for LuauLibrary callbacks during protected lifecycle calls.
    /// Not an object registry — cleared after every protected invocation.
    /// </summary>
    internal static class LuauBindingContext
    {
        [System.ThreadStatic]
        static LuauHostBase _host;

        public static LuauHostBase Host
        {
            get => _host;
            set => _host = value;
        }

        public static void Clear() => _host = null;
    }
}
