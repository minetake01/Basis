using System;
using System.Runtime.InteropServices;
using System.Text;

internal static class BasisMediaStreamNative
{
    private const string Lib = "basis_mediastream_native";

    public const int RenderUpdate = 1;
    public const int RenderRelease = 2;

    public const int BackendAuto = 0;
    public const int BackendSpout2 = 1;

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern uint basis_mediastream_get_available_backends();

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern int basis_mediastream_resolve_backend(int backend);

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern IntPtr basis_mediastream_publisher_create(int backend, string streamName, int width, int height);

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern void basis_mediastream_publisher_destroy(IntPtr publisher);

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern void basis_mediastream_publisher_set_texture(IntPtr publisher, IntPtr nativeTexture, int width, int height);

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern void basis_mediastream_publisher_update(IntPtr publisher);

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern int basis_mediastream_publisher_get_active_backend(IntPtr publisher);

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern int basis_mediastream_publisher_get_last_error(IntPtr publisher, byte[] buf, int bufSize);

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr basis_mediastream_get_render_event_func();

    private static bool? supported;

    public static bool IsSupported
    {
        get
        {
            if (supported.HasValue) return supported.Value;
            try
            {
                supported = basis_mediastream_get_available_backends() != 0;
            }
            catch (DllNotFoundException)
            {
                supported = false;
            }
            return supported.Value;
        }
    }

    public static string TryGetLastError(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        var buf = new byte[512];
        int len = basis_mediastream_publisher_get_last_error(handle, buf, buf.Length);
        return len > 0 ? Encoding.UTF8.GetString(buf, 0, len) : null;
    }
}
