using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Net.Minetake.AutoTranslator.BasisIntegration
{
    public static class SecretStore
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Blob { public int Length; public IntPtr Data; }
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
        public static byte[] Transform(byte[] value, bool encrypt)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) throw new PlatformNotSupportedException("APIキーの保存はWindows専用です。");
            var input = new Blob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
            Blob output = default;
            try
            {
                Marshal.Copy(value, 0, input.Data, value.Length);
                // UI_FORBIDDEN; deliberately no LOCAL_MACHINE flag (current user only).
                bool success = encrypt
                    ? CryptProtectData(ref input, "net.minetake.auto-translator", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "APIキーを暗号化／復号できません。再入力してください。");
                var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
            }
            finally
            {
                for (int i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
                Marshal.FreeHGlobal(input.Data);
                if (output.Data != IntPtr.Zero)
                {
                    for (int i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                    LocalFree(output.Data);
                }
            }
        }
        public static void Save(string path, string value)
        {
            byte[] plain = Encoding.UTF8.GetBytes(value);
            try { AtomicWrite(path, Transform(plain, true)); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }
        public static string Load(string path)
        {
            if (!File.Exists(path)) return "";
            byte[] plain = Transform(File.ReadAllBytes(path), false);
            try { return Encoding.UTF8.GetString(plain); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }
        public static void AtomicWrite(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, data);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
