using System;
using System.Collections.Generic;
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
        readonly HashSet<UnityWebRequest> _inFlight = new();

        public void DownloadImage(string url, Action<BasisLuauImageDownloadResult> callback)
        {
            if (callback == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(url) ||
                (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                 !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                callback(new BasisLuauImageDownloadResult(null, null, "Security Failure"));
                return;
            }

            var www = new UnityWebRequest(url);
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
                callback(new BasisLuauImageDownloadResult(www, dht, null));
            }

            req.completed += Complete;
            if (!completed && req.isDone)
            {
                Complete(null);
            }
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
