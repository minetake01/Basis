using Luau;
using Minetake.Basis.Luau.Bindings;
using Minetake.Basis.Luau.Registry;
using UnityEngine;

namespace Minetake.Basis.Luau.Bindings
{
    [LuauLibrary("transform")]
    public partial class TransformBindings
    {
        [LuauMember("rotate")]
        public static void Rotate(double handleRaw, double x, double y, double z) =>
            WithTransform(handleRaw, t => t.Rotate((float)x, (float)y, (float)z, Space.World));

        [LuauMember("rotateLocal")]
        public static void RotateLocal(double handleRaw, double x, double y, double z) =>
            WithTransform(handleRaw, t => t.Rotate((float)x, (float)y, (float)z, Space.Self));

        [LuauMember("setEuler")]
        public static void SetEuler(double handleRaw, double x, double y, double z) =>
            WithTransform(handleRaw, t => t.rotation = Quaternion.Euler((float)x, (float)y, (float)z));

        [LuauMember("getPosition")]
        public static LuauValue GetPosition(double handleRaw)
        {
            if (!TryGetTransform(handleRaw, out Transform transform))
            {
                return default;
            }

            Vector3 p = transform.position;
            return LuauValueMarshaller.ToLuau(LuauBindingContext.Host, p);
        }

        [LuauMember("setPosition")]
        public static void SetPosition(double handleRaw, double x, double y, double z) =>
            WithTransform(handleRaw, t => t.position = new Vector3((float)x, (float)y, (float)z));

        [LuauMember("getLocalPosition")]
        public static LuauValue GetLocalPosition(double handleRaw)
        {
            if (!TryGetTransform(handleRaw, out Transform transform))
            {
                return default;
            }

            return LuauValueMarshaller.ToLuau(LuauBindingContext.Host, transform.localPosition);
        }

        [LuauMember("setLocalPosition")]
        public static void SetLocalPosition(double handleRaw, double x, double y, double z) =>
            WithTransform(handleRaw, t => t.localPosition = new Vector3((float)x, (float)y, (float)z));

        static void WithTransform(double handleRaw, System.Action<Transform> action)
        {
            if (TryGetTransform(handleRaw, out Transform transform))
            {
                action(transform);
            }
        }

        static bool TryGetTransform(double handleRaw, out Transform transform)
        {
            transform = null;
            var host = LuauBindingContext.Host;
            if (host == null)
            {
                return false;
            }

            var handle = LuauObjectHandle.FromRaw((ulong)handleRaw);
            return host.TryResolveTransform(handle, out transform);
        }
    }
}
