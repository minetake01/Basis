using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.Networking;

namespace Minetake.Basis.Luau.Services
{
    public sealed class BasisLuauImageDownloadResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public int SizeInMemoryBytes { get; set; }
        public Texture Result { get; set; }

        internal BasisLuauImageDownloadResult(UnityWebRequest www, DownloadHandlerTexture dht, string majorFailure)
        {
            if (www != null && www.result == UnityWebRequest.Result.Success)
            {
                Success = true;
                Error = string.Empty;
                Result = dht.texture;
                SizeInMemoryBytes = dht.data?.Length ?? 0;
            }
            else
            {
                Success = false;
                Error = dht != null ? dht.error : majorFailure;
                Result = null;
            }
        }
    }

    public sealed class BasisLuauImageDownloader : IDisposable
    {
        const int MaxConcurrent = 2;
        const int MaxDownloadBytes = 4 * 1024 * 1024;
        const int TimeoutSeconds = 15;

        readonly HashSet<UnityWebRequest> _inFlight = new();
        readonly object _gate = new();

        public void DownloadImage(string url, Action<BasisLuauImageDownloadResult> callback)
        {
            if (callback == null)
            {
                return;
            }

            if (!TryValidateUrl(url, out string failure))
            {
                callback(new BasisLuauImageDownloadResult(null, null, failure));
                return;
            }

            lock (_gate)
            {
                if (_inFlight.Count >= MaxConcurrent)
                {
                    callback(new BasisLuauImageDownloadResult(null, null, "too many concurrent downloads"));
                    return;
                }
            }

            var www = new UnityWebRequest(url);
            www.timeout = TimeoutSeconds;
            var dht = new DownloadHandlerTexture(true);
            www.downloadHandler = dht;
            bool completed = false;
            UnityWebRequestAsyncOperation req = www.SendWebRequest();
            _inFlight.Add(www);

            void Complete(AsyncOperation _)
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                _inFlight.Remove(www);
                if (www.downloadedBytes > MaxDownloadBytes)
                {
                    callback(new BasisLuauImageDownloadResult(null, null, "download too large"));
                    www.Dispose();
                    return;
                }

                callback(new BasisLuauImageDownloadResult(www, dht, null));
            }

            req.completed += Complete;
            if (!completed && req.isDone)
            {
                Complete(null);
            }
        }

        public static bool TryValidateUrl(string url, out string failure)
        {
            failure = string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                failure = "empty url";
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                failure = "invalid url";
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            {
                failure = "scheme not allowed";
                return false;
            }

            if (IsBlockedHost(uri.Host))
            {
                failure = "host not allowed";
                return false;
            }

            return true;
        }

        static bool IsBlockedHost(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!IPAddress.TryParse(host, out IPAddress ip))
            {
                return false;
            }

            if (IPAddress.IsLoopback(ip))
            {
                return true;
            }

            byte[] bytes = ip.GetAddressBytes();
            if (ip.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
            {
                if (bytes[0] == 10)
                {
                    return true;
                }

                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                {
                    return true;
                }

                if (bytes[0] == 192 && bytes[1] == 168)
                {
                    return true;
                }

                if (bytes[0] == 127)
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            foreach (UnityWebRequest www in _inFlight)
            {
                www.Dispose();
            }

            _inFlight.Clear();
        }
    }
}
