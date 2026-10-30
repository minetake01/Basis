using System;
using System.IO;
using Basis.BasisUI;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace Basis.FluxHarp
{
    /// <summary>
    /// Unattended check of a built client. Inert unless <c>--flux-selfcheck=&lt;dir&gt;</c> is on the command line:
    /// then, once the instrument has been playing for a while, it writes what it sees (a screenshot) and what it
    /// is (a report: lines sounding, how loud each is, where each is) to that directory, and quits.
    /// </summary>
    [AutoStaticsCleanup]
    public static partial class FluxHarpSelfCheck
    {
        private const float PlaySeconds = 12f;

        private static string _dir;
        private static float _since = -1;
        private static bool _shot;
        private static bool _closed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith("--flux-selfcheck=", StringComparison.OrdinalIgnoreCase)) _dir = arg.Substring("--flux-selfcheck=".Length);
            }
            if (string.IsNullOrEmpty(_dir)) return;
            Directory.CreateDirectory(_dir);
            Debug.Log($"[FluxHarpSelfCheck] armed, writing to {_dir}");
            GameObject go = new GameObject("Flux Harp Self Check");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        private sealed class Runner : MonoBehaviour
        {
            private void Update()
            {
                FluxHarpInstrument instrument = FluxHarpInstrument.Instance;
                if (instrument == null) return;
                if (_since < 0) _since = Time.unscaledTime;
                float playing = Time.unscaledTime - _since;
                if (Time.frameCount % 90 == 0)
                {
                    Basis.Scripts.BasisSdk.Players.BasisLocalPlayer player = Basis.Scripts.BasisSdk.Players.BasisLocalPlayer.Instance;
                    Debug.Log($"[FluxHarpSelfCheck] t={playing:F1} player={(player != null ? player.transform.position.ToString() : "none")} eye={(Camera.main != null ? Camera.main.transform.position.ToString() : "none")} cam={(Basis.Scripts.Drivers.BasisLocalCameraDriver.CameraInstance != null ? Basis.Scripts.Drivers.BasisLocalCameraDriver.CameraInstance.transform.position.ToString() : "none")} anchor={instrument.transform.position}");
                }
                // The menu opens over everything on first launch; the check looks at the instrument, not at it.
                if (!_closed && playing > PlaySeconds - 3)
                {
                    _closed = true;
                    BasisMainMenu.Close();
                }
                if (!_shot && playing > PlaySeconds)
                {
                    _shot = true;
                    ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "client.png"));
                    File.WriteAllText(Path.Combine(_dir, "report.txt"), instrument.Report());
                    Debug.Log("[FluxHarpSelfCheck] written");
                }
                else if (_shot && playing > PlaySeconds + 2)
                {
                    Application.Quit();
                }
            }
        }
    }
}
