using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Luau.Unity;
using Minetake.Basis.Luau.Registry;

namespace Minetake.Basis.Luau.Runtime
{
    /// <summary>
    /// Main-thread MPSC staging for physics/network/OSC events before worker delivery.
    /// </summary>
    public sealed class LuauEventIngress
    {
        readonly ConcurrentQueue<BasisLuauCommandNative> _queue = new();
        readonly uint _hostId;

        public LuauEventIngress(uint hostId) => _hostId = hostId;

        public bool TryEnqueue(LuauCommandType type, uint handleIndex, uint handleGeneration, ReadOnlySpan<float> data)
        {
            if (!LuauCommandCatalog.IsRegistered(type))
            {
                return false;
            }

            var cmd = new BasisLuauCommandNative
            {
                Type = (ushort)type,
                HostId = (ushort)_hostId,
                HandleIndex = handleIndex,
                HandleGeneration = handleGeneration,
            };

            unsafe
            {
                cmd.Data[0] = data.Length > 0 ? data[0] : 0;
                cmd.Data[1] = data.Length > 1 ? data[1] : 0;
                cmd.Data[2] = data.Length > 2 ? data[2] : 0;
            }

            _queue.Enqueue(cmd);
            return true;
        }

        public int DrainToNative(BasisLuauNativeRuntime native)
        {
            if (native == null || !native.IsCreated)
            {
                return 0;
            }

            int count = 0;
            while (_queue.TryDequeue(out BasisLuauCommandNative cmd))
            {
                if (native.PushCommand(_hostId, ref cmd) == BasisLuauRingResult.Ok)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public sealed class LuauTicketRegistry
    {
        uint _nextId = 1;
        readonly Dictionary<uint, Action<LuauObjectHandle>> _pending = new();

        public uint Issue(Action<LuauObjectHandle> onComplete)
        {
            uint id = _nextId++;
            _pending[id] = onComplete;
            return id;
        }

        public void Complete(uint ticketId, LuauObjectHandle handle)
        {
            if (_pending.TryGetValue(ticketId, out Action<LuauObjectHandle> cb))
            {
                _pending.Remove(ticketId);
                cb?.Invoke(handle);
            }
        }

        public void CancelAll() => _pending.Clear();
    }
}
