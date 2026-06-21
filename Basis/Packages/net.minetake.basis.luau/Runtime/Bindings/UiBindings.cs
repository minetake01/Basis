using Luau;
using Minetake.Basis.Luau;
using Minetake.Basis.Luau.Registry;
using TMPro;
using UnityEngine;

namespace Minetake.Basis.Luau.Bindings
{
    [LuauLibrary("basis_ui")]
    public partial class UiBindings
    {
        [LuauMember("setText")]
        public static void SetText(double handleRaw, string text)
        {
            if (!TryGetTmpText(handleRaw, out TMP_Text tmp))
            {
                return;
            }

            tmp.text = text ?? string.Empty;
        }

        static bool TryGetTmpText(double handleRaw, out TMP_Text tmp)
        {
            tmp = null;
            LuauHostBase host = LuauBindingContext.Host;
            if (host == null)
            {
                return false;
            }

            var handle = LuauObjectHandle.FromRaw((ulong)handleRaw);
            if (!host.Capability.ValidateHandle(host.Registry, handle, typeof(TMP_Text), out UnityEngine.Object obj))
            {
                return false;
            }

            tmp = obj as TMP_Text;
            return tmp != null;
        }
    }
}
