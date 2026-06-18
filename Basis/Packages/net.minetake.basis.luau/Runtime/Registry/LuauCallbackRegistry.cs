using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Luau;

namespace Minetake.Basis.Luau
{
    public sealed class LuauCallbackRegistry
    {
        readonly ConcurrentDictionary<int, Entry> _entries = new();
        int _nextId = 1;

        public int Register(LuauScriptProxy proxy, LuauFunction function)
        {
            if (proxy == null || function == null)
            {
                return 0;
            }

            int id = _nextId++;
            _entries[id] = new Entry(proxy, function);
            return id;
        }

        public void Unregister(int id)
        {
            if (id > 0)
            {
                _entries.TryRemove(id, out _);
            }
        }

        public bool TryGet(int id, out LuauScriptProxy proxy, out LuauFunction function)
        {
            proxy = null;
            function = null;
            if (!_entries.TryGetValue(id, out Entry entry))
            {
                return false;
            }

            proxy = entry.Proxy;
            function = entry.Function;
            return proxy != null && function != null;
        }

        public void ClearProxy(LuauScriptProxy proxy)
        {
            if (proxy == null)
            {
                return;
            }

            foreach (KeyValuePair<int, Entry> pair in _entries)
            {
                if (pair.Value.Proxy == proxy)
                {
                    _entries.TryRemove(pair.Key, out _);
                }
            }
        }

        sealed class Entry
        {
            public Entry(LuauScriptProxy proxy, LuauFunction function)
            {
                Proxy = proxy;
                Function = function;
            }

            public LuauScriptProxy Proxy { get; }
            public LuauFunction Function { get; }
        }
    }
}
