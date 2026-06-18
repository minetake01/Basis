using System;
using System.Collections.Generic;
using Luau;

namespace Minetake.Basis.Luau.Bindings
{
    /// <summary>
    /// Thread-scoped host/proxy pointers for LuauLibrary callbacks during protected lifecycle calls.
    /// </summary>
    public static class LuauBindingContext
    {
        [ThreadStatic] static LuauHostBase _host;
        [ThreadStatic] static LuauScriptProxy _proxy;

        public static LuauHostBase Host
        {
            get => _host;
            internal set => _host = value;
        }

        public static LuauScriptProxy Proxy
        {
            get => _proxy;
            internal set => _proxy = value;
        }

        public static void Clear()
        {
            _host = null;
            _proxy = null;
        }
    }
}
