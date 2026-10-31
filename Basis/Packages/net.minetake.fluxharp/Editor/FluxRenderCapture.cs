using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Net.Minetake.FluxHarp.Editor
{
    /// <summary>
    /// Renders the real instrument (the same component the client spawns, with its labels, light and void) to
    /// PNGs so the look can be checked without a headset. Run with
    /// <c>-executeMethod Net.Minetake.FluxHarp.Editor.FluxRenderCapture.Run</c>; writes to FLUX_CAPTURE_DIR. Only the
    /// session is local, and silent: the instrument is bound to a session of its own playing the demo piece.
    /// </summary>
    public static class FluxRenderCapture
    {
        private const int Width = 1600, Height = 1000;

        public static void Run()
        {
            string dir = Environment.GetEnvironmentVariable("FLUX_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("FLUX_CAPTURE_DIR is not set.");
            Directory.CreateDirectory(dir);
            string presetName = Environment.GetEnvironmentVariable("FLUX_CAPTURE_PRESET");
            FluxPreset preset = string.IsNullOrEmpty(presetName) ? FluxPreset.Yxeni : (FluxPreset)Enum.Parse(typeof(FluxPreset), presetName, true);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            long now = 0;
            FluxSession session = new FluxSession((packet, reliable, recipient) => { });
            session.BecomeOwner(null, 0, preset);
            session.Request(FluxOp.SetAnchor(new FluxVec(0, 0, 0), 0), 0);

            GameObject camGo = new GameObject("Capture Camera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.allowHDR = true;
            cam.fieldOfView = 80;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 200;
            camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;

            // The client's default volume has bloom on; reproduce a world's typical override so the glow reads.
            Volume volume = new GameObject("Post").AddComponent<Volume>();
            volume.isGlobal = true;
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.8f);
            bloom.threshold.Override(0.9f);
            bloom.scatter.Override(0.65f);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            volume.sharedProfile = profile;

            GameObject go = new GameObject("Flux Harp");
            FluxHarpInstrument instrument = go.AddComponent<FluxHarpInstrument>();
            instrument.Audible = false;
            instrument.EyeOverride = camGo.transform;
            instrument.Setup(session, () => now);

            // The player stands on the anchor, inside the node sphere, the sheet wrapped round at arm's length.
            Shot(dir, "player_view", cam, instrument, ref now, session, beat: 2.3,
                eye: new Vector3(0, 1.6f, 0), look: new Vector3(0, 1.25f, 1f));
            Shot(dir, "node_sphere_close", cam, instrument, ref now, session, beat: 5.6,
                eye: new Vector3(0, 1.55f, 0), look: new Vector3(0.18f, 1.45f, 0.7f), fov: 40);
            Shot(dir, "node_sphere_left", cam, instrument, ref now, session, beat: 7.1,
                eye: new Vector3(0, 1.4f, 0), look: new Vector3(-0.7f, 1.2f, 0.3f), fov: 55);
            Shot(dir, "palette_behind", cam, instrument, ref now, session, beat: 8.0,
                eye: new Vector3(0, 1.4f, 0), look: new Vector3(0, 1.1f, -1f), fov: 55);
            Shot(dir, "dj_cubes", cam, instrument, ref now, session, beat: 3.0,
                eye: new Vector3(0, 1.6f, 0), look: new Vector3(0, 0.95f, -0.6f), fov: 70);
            Shot(dir, "orbit_overview", cam, instrument, ref now, session, beat: 9.1,
                eye: new Vector3(0, 6.5f, -5.5f), look: new Vector3(0, 1.0f, 0));
            Shot(dir, "inside_ring", cam, instrument, ref now, session, beat: 13.4,
                eye: new Vector3(0, 1.6f, 0), look: new Vector3(1.5f, 1.9f, -1.2f));

            // The two homes of a number, side by side: the seed's amplitude is a number node; the voice's turn is
            // lifted out into a node of its own; its radius stays written in its port.
            session.Request(FluxOp.LoadPreset(FluxPreset.Seed), now);
            ushort voice = 0;
            foreach (FluxNode n in session.Graph.Nodes) if (n.Kind == FluxKind.Voice) voice = n.Id;
            session.Request(FluxOp.SetLiteral(voice, 1, 0.125), now);
            session.Request(FluxOp.SetLiteral(voice, 2, 1.8), now);
            session.Request(FluxOp.Extract(voice, 1, new FluxVec(0.05f, -0.2f, 0.7f)), now);
            if (session.Graph.Get(voice).Inputs[1] == FluxGraph.None) throw new InvalidOperationException("Extract was not applied.");
            Shot(dir, "numbers", cam, instrument, ref now, session, beat: 1.0,
                eye: new Vector3(0, 1.4f, 0), look: new Vector3(0, 1.3f, 1f), fov: 50);

            UnityEngine.Object.DestroyImmediate(go);
            Debug.Log("[FluxRenderCapture] done");
        }

        private static void Shot(string dir, string name, Camera cam, FluxHarpInstrument instrument, ref long now,
            FluxSession session, double beat, Vector3 eye, Vector3 look, float fov = 80)
        {
            cam.fieldOfView = fov;
            cam.transform.position = eye;
            cam.transform.LookAt(look);
            now = session.Graph.Transport.EpochTicks + (long)(beat / session.Graph.Transport.BeatsPerSecond * TimeSpan.TicksPerSecond);
            instrument.Frame();
            // Labels are TextMeshPro meshes, built when their text changes; give them their frame too.
            foreach (TMPro.TextMeshPro t in instrument.GetComponentsInChildren<TMPro.TextMeshPro>()) t.ForceMeshUpdate();

            RenderTexture rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.DefaultHDR);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture ldr = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(rt, ldr);
            RenderTexture.active = ldr;
            Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            RenderTexture.ReleaseTemporary(ldr);
            cam.targetTexture = null;
            rt.Release();
        }
    }
}
