using Luau;
using Minetake.Basis.Luau.Registry;
using UnityEngine;

namespace Minetake.Basis.Luau.Bindings
{
    [LuauLibrary("transform")]
    public partial class TransformBindings
    {
        [LuauMember("rotate")]
        public static void Rotate(double handleRaw, double x, double y, double z)
        {
            var host = LuauBindingContext.Host;
            if (host == null)
            {
                return;
            }

            var handle = LuauObjectHandle.FromRaw((ulong)handleRaw);
            if (!host.TryResolveTransform(handle, out Transform transform))
            {
                return;
            }

            transform.Rotate((float)x, (float)y, (float)z, Space.World);
        }

        [LuauMember("rotateLocal")]
        public static void RotateLocal(double handleRaw, double x, double y, double z)
        {
            var host = LuauBindingContext.Host;
            if (host == null)
            {
                return;
            }

            var handle = LuauObjectHandle.FromRaw((ulong)handleRaw);
            if (!host.TryResolveTransform(handle, out Transform transform))
            {
                return;
            }

            transform.Rotate((float)x, (float)y, (float)z, Space.Self);
        }

        [LuauMember("setEuler")]
        public static void SetEuler(double handleRaw, double x, double y, double z)
        {
            var host = LuauBindingContext.Host;
            if (host == null)
            {
                return;
            }

            var handle = LuauObjectHandle.FromRaw((ulong)handleRaw);
            if (!host.TryResolveTransform(handle, out Transform transform))
            {
                return;
            }

            transform.rotation = Quaternion.Euler((float)x, (float)y, (float)z);
        }
    }
}
