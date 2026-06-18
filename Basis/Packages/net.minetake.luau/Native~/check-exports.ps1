param([string]$Dll)
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class ExportCheck {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h, string n);
    public static string Check(string path) {
        var sb = new StringBuilder();
        var h = LoadLibrary(path);
        if (h == IntPtr.Zero) return "LoadLibrary failed";
        foreach (var name in new[]{"ffi_luaL_error_msg","basis_luau_ffi_extras_anchor","ffi_lua_newstate","ffi_lua_callbacks"}) {
            sb.Append(name).Append('=').Append(GetProcAddress(h,name)!=IntPtr.Zero).Append(' ');
        }
        return sb.ToString();
    }
}
"@
[ExportCheck]::Check($Dll)
