using System.Reflection;
using Luau;

namespace Minetake.Basis.Luau.Runtime
{
    public static class LuauFunctionRefHelper
    {
        public static int GetReference(LuauFunction function)
        {
            if (function == null || function.IsDisposed)
            {
                return -1;
            }

            PropertyInfo referenceProperty = function.GetType().GetProperty(
                "Reference",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (referenceProperty?.GetValue(function) is int reference && reference > 0)
            {
                return reference;
            }

            return -1;
        }
    }
}
