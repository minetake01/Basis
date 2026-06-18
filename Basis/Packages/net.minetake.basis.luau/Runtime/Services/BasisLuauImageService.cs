using System;
using Luau;
using Minetake.Basis.Luau.Bindings;

namespace Minetake.Basis.Luau.Services
{
    [LuauLibrary("basis_image")]
    public partial class BasisLuauImageService
    {
        public static void Install(LuauState state, LuauHostBase host)
        {
            state?.OpenLibrary<BasisLuauImageService>();
        }

        [LuauMember("downloadImage")]
        public static void DownloadImage(string url, LuauFunction callback)
        {
            var host = LuauBindingContext.Host;
            var proxy = LuauBindingContext.Proxy;
            if (host == null || proxy == null || callback == null)
            {
                return;
            }

            var downloader = new BasisLuauImageDownloader();
            downloader.DownloadImage(url, result =>
            {
                LuauState thread = host.CreateSandboxedThread();
                LuauTable table = thread.CreateTable();
                table["success"] = result.Success;
                table["error"] = result.Error ?? string.Empty;
                table["sizeInMemoryBytes"] = result.SizeInMemoryBytes;
                if (result.Result != null)
                {
                    table["texture"] = host.RegisterObject(result.Result).ToRaw();
                }

                host.InvokeCallback(proxy, callback, table);
                downloader.Dispose();
            });
        }
    }
}
