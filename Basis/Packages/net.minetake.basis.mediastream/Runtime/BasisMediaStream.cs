/// <summary>
/// Entry points for the net.minetake.basis.mediastream package.
/// </summary>
public static class BasisMediaStream
{
    public static bool IsSupported => BasisMediaStreamNative.IsSupported;

    public static uint AvailableBackends => BasisMediaStreamNative.IsSupported
        ? BasisMediaStreamNative.basis_mediastream_get_available_backends()
        : 0;
}
