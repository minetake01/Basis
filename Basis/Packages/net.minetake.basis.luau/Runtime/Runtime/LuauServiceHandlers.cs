using System;
using System.Text;
using Luau.Unity;
using Minetake.Basis.Luau.Registry;
using Minetake.Basis.Luau.Services;
using UnityEngine;

namespace Minetake.Basis.Luau.Runtime
{
    public static class LuauNativeBufferReader
    {
        public static string ReadString(BasisLuauNativeRuntime native, BasisLuauCommandNative cmd)
        {
            unsafe
            {
                var bufferRef = new BasisLuauBufferRefNative
                {
                    Slot = (uint)cmd.Data[0],
                    Generation = (uint)cmd.Data[1],
                    HostId = cmd.HostId,
                    Length = (uint)cmd.Data[2],
                };

                if (bufferRef.Length == 0)
                {
                    return string.Empty;
                }

                byte* bytes;
                uint length;
                try
                {
                    if (native.TryGetBufferBytes(ref bufferRef, out bytes, out length) == 0 || bytes == null)
                    {
                        return string.Empty;
                    }

                    return Encoding.UTF8.GetString(bytes, (int)length);
                }
                finally
                {
                    native.ReleaseBuffer(ref bufferRef);
                }
            }
        }
    }

    public static class LuauServiceHandlers
    {
        public static void SetUiText(LuauHostBase host, LuauAuthorityTable authority, BasisLuauNativeRuntime native, BasisLuauCommandNative cmd)
        {
            if (!authority.TryValidate(cmd.HandleIndex, cmd.HandleGeneration, typeof(TMPro.TMP_Text), out UnityEngine.Object obj))
            {
                return;
            }

            if (obj is not TMPro.TMP_Text tmp)
            {
                return;
            }

            tmp.text = LuauNativeBufferReader.ReadString(native, cmd) ?? string.Empty;
        }

        public static void Log(BasisLuauNativeRuntime native, BasisLuauCommandNative cmd) =>
            BasisLuauDebug.Log(LuauNativeBufferReader.ReadString(native, cmd));

        public static void Warn(BasisLuauNativeRuntime native, BasisLuauCommandNative cmd) =>
            BasisLuauDebug.LogWarning(LuauNativeBufferReader.ReadString(native, cmd));

        public static void Error(BasisLuauNativeRuntime native, BasisLuauCommandNative cmd) =>
            BasisLuauDebug.LogError(LuauNativeBufferReader.ReadString(native, cmd));

        public static void Destroy(LuauAuthorityTable authority, BasisLuauCommandNative cmd)
        {
            if (!authority.TryValidate(cmd.HandleIndex, cmd.HandleGeneration, typeof(UnityEngine.Object), out UnityEngine.Object obj) || obj == null)
            {
                return;
            }

            authority.Tombstone(cmd.HandleIndex, cmd.HandleGeneration);
            UnityEngine.Object.Destroy(obj);
        }

        public static void Clone(LuauAuthorityTable authority, LuauTicketRegistry tickets, uint ticketId, BasisLuauCommandNative cmd)
        {
            var handle = LuauObjectHandle.Create(cmd.HandleGeneration, cmd.HandleIndex);
            if (!authority.TryResolve(handle, out UnityEngine.Object original))
            {
                return;
            }

            UnityEngine.Object clone = BasisLuauInstantiateHandlers.SanitizeInstantiate(original);
            LuauObjectHandle result = clone != null ? authority.Register(clone) : LuauObjectHandle.FromRaw(LuauObjectHandle.InvalidRaw);
            tickets.Complete(ticketId, result);
        }

        public static void DownloadImage(LuauHostBase host, LuauScriptProxy proxy, BasisLuauNativeRuntime native, LuauTicketRegistry tickets, uint ticketId, BasisLuauCommandNative cmd)
        {
            string url = LuauNativeBufferReader.ReadString(native, cmd);
            var downloader = new BasisLuauImageDownloader();
            downloader.DownloadImage(url, result =>
            {
                if (proxy == null || !proxy.IsEnabled)
                {
                    downloader.Dispose();
                    return;
                }

                host.CompleteImageDownload(proxy, result);
                downloader.Dispose();
            });
        }

        public static void TakeOwnership(LuauHostBase host, LuauAuthorityTable authority, BasisLuauCommandNative cmd)
        {
            var handle = LuauObjectHandle.Create(cmd.HandleGeneration, cmd.HandleIndex);
            BasisLuauNetworkHandlers.TakeOwnership(host, authority, handle);
        }

        public static void MakeNetworkable(LuauHostBase host, LuauAuthorityTable authority, LuauTicketRegistry tickets, uint ticketId, BasisLuauCommandNative cmd)
        {
            var handle = LuauObjectHandle.Create(cmd.HandleGeneration, cmd.HandleIndex);
            LuauObjectHandle result = BasisLuauNetworkHandlers.MakeNetworkable(host, authority, handle);
            tickets.Complete(ticketId, result);
        }

        public static void MakeInteractable(LuauHostBase host, LuauAuthorityTable authority, LuauTicketRegistry tickets, uint ticketId, BasisLuauCommandNative cmd)
        {
            var handle = LuauObjectHandle.Create(cmd.HandleGeneration, cmd.HandleIndex);
            LuauObjectHandle result = BasisLuauUtilHandlers.MakeInteractable(host, authority, handle);
            tickets.Complete(ticketId, result);
        }

        public static void OscPublishFloat(LuauHostBase host, BasisLuauNativeRuntime native, BasisLuauCommandNative cmd)
        {
            unsafe
            {
                string address = LuauNativeBufferReader.ReadString(native, cmd);
                float value = cmd.Data[3];
                BasisLuauOscHandlers.PublishFloat(host, address, value);
            }
        }

        public static void OscSubscribe(LuauHostBase host, LuauScriptProxy proxy, BasisLuauNativeRuntime native, BasisLuauCommandNative cmd)
        {
            unsafe
            {
                int callbackRef = (int)cmd.Data[0];
                var bufferRef = new BasisLuauBufferRefNative
                {
                    Slot = (uint)cmd.Data[1],
                    Generation = (uint)cmd.Data[2],
                    HostId = cmd.HostId,
                    Length = (uint)cmd.Data[3],
                };

                string address = string.Empty;
                if (bufferRef.Length > 0)
                {
                    byte* bytes;
                    uint length;
                    if (native.TryGetBufferBytes(ref bufferRef, out bytes, out length) != 0 && bytes != null)
                    {
                        address = System.Text.Encoding.UTF8.GetString(bytes, (int)length);
                    }
                }

                BasisLuauOscSubscribeRegistry.Subscribe(host, proxy, address, callbackRef);
            }
        }

        public static void AvatarResolve(LuauHostBase host, LuauAuthorityTable authority, LuauTicketRegistry tickets, uint ticketId, BasisLuauCommandNative cmd)
        {
            var handle = LuauObjectHandle.Create(cmd.HandleGeneration, cmd.HandleIndex);
            LuauObjectHandle result = BasisLuauAvatarHandlers.ResolveAvatar(host, authority, handle);
            tickets.Complete(ticketId, result);
        }
    }
}
