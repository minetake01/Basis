using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.Receivers;
using UnityEngine;

namespace Net.Minetake.AutoTranslator.BasisIntegration
{
    public sealed class TranslatorHost : MonoBehaviour
    {
        private sealed class Capture
        {
            public readonly Guid Id = Guid.NewGuid();
            public readonly AudioCaptureBuffer Buffer = new AudioCaptureBuffer();
            public readonly BasisRemotePlayer Player;
            public readonly BasisRemoteAudioDriver Driver;
            public readonly BasisAudioReceiver Receiver;
            public readonly Action<float[], int> Callback;
            public readonly CaptionState Caption = new CaptionState();
            public TranslatorSubtitle Subtitle;
            public TranslatedVoicePlayer Voice;
            public volatile bool Active = true;
            public int OverflowReported;
            public double LastVoice = TranslationClock.Now;
            public Capture(BasisRemotePlayer player, BasisRemoteAudioDriver driver, int sampleRate)
            {
                Player = player; Driver = driver; Receiver = driver.BasisAudioReceiver;
                Callback = (data, channels) =>
                {
                    if (Active && ReferenceEquals(driver.BasisAudioReceiver, Receiver))
                        Buffer.Write(data, channels, sampleRate, TranslationClock.Now);
                };
            }
        }
        private readonly struct Delivery
        {
            public readonly int Epoch;
            public readonly CaptionUpdate? Caption;
            public readonly TranslationFault? Fault;
            public readonly TranslatedAudioFrame? Audio;
            public Delivery(int epoch, CaptionUpdate? caption, TranslationFault? fault, TranslatedAudioFrame? audio)
            { Epoch = epoch; Caption = caption; Fault = fault; Audio = audio; }
        }
        public static TranslatorHost Instance { get; private set; }
        public TranslatorConfiguration Config { get; private set; } = new TranslatorConfiguration();
        public string Status { get; private set; } = "未設定";
        private readonly Dictionary<Guid, Capture> captures = new Dictionary<Guid, Capture>();
        private readonly HashSet<BasisRemotePlayer> failed = new HashSet<BasisRemotePlayer>();
        private readonly ConcurrentQueue<Delivery> deliveries = new ConcurrentQueue<Delivery>();
        private Capture[] snapshot = Array.Empty<Capture>();
        private ISpeechTranslationEngine engine;
        private CancellationTokenSource pumpCancel;
        private int epoch, sampleRate, excluded;
        private double nextScan;
        private string folder, lastError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.WindowsPlayer) return;
            if (Instance != null) return;
            var go = new GameObject("Auto Translator"); DontDestroyOnLoad(go);
            Instance = go.AddComponent<TranslatorHost>();
            SettingsProvider.ExternalTabs.RemoveAll(tab => tab.TabName == "自動翻訳");
            SettingsProvider.ExternalTabs.Add(("自動翻訳", TranslatorSettingsPanel.Build));
        }
        private void Awake()
        {
            folder = Path.Combine(Application.persistentDataPath, "net.minetake.auto-translator");
            sampleRate = AudioSettings.outputSampleRate;
            AudioSettings.OnAudioConfigurationChanged += AudioChanged;
            try { Config = TranslatorConfiguration.Load(folder); TryStartEngine(LoadKeys()); }
            catch (Exception) { ShowError("自動翻訳の設定または保存済みキーを読み込めません。設定を確認し、必要ならキーを再入力してください。"); }
        }
        private void AudioChanged(bool deviceChanged)
        {
            sampleRate = AudioSettings.outputSampleRate;
            StopEngine(); ShowError("音声デバイスの構成が変更されました。「保存して適用 / 再接続」で再接続してください。");
        }
        public void Apply(TranslatorConfiguration config, string speechKey, string translationKey, string dashscopeKey)
        {
            Commit(config, speechKey, translationKey, dashscopeKey);
        }
        private void Commit(TranslatorConfiguration config, string speechKey, string translationKey, string dashscopeKey)
        {
            config.Validate();
            string speechPath = Path.Combine(folder, "speech.key");
            string translationPath = Path.Combine(folder, "translation.key");
            string dashscopePath = Path.Combine(folder, "dashscope.key");
            string speech = string.IsNullOrWhiteSpace(speechKey) ? SecretStore.Load(speechPath) : speechKey.Trim();
            string translation = string.IsNullOrWhiteSpace(translationKey) ? SecretStore.Load(translationPath) : translationKey.Trim();
            string dashscope = string.IsNullOrWhiteSpace(dashscopeKey) ? SecretStore.Load(dashscopePath) : dashscopeKey.Trim();
            StopEngine();
            if (!string.IsNullOrWhiteSpace(speechKey)) SecretStore.Save(speechPath, speech);
            if (!string.IsNullOrWhiteSpace(translationKey)) SecretStore.Save(translationPath, translation);
            if (!string.IsNullOrWhiteSpace(dashscopeKey)) SecretStore.Save(dashscopePath, dashscope);
            config.Save(folder); Config = config; lastError = null;
            TryStartEngine((speech, translation, dashscope));
        }
        private (string Speech, string Translation, string Dashscope) LoadKeys() =>
            (SecretStore.Load(Path.Combine(folder, "speech.key")),
             SecretStore.Load(Path.Combine(folder, "translation.key")),
             SecretStore.Load(Path.Combine(folder, "dashscope.key")));
        private void TryStartEngine((string Speech, string Translation, string Dashscope) keys)
        {
            string missing = Config.Mode == TranslationMode.Voice
                ? (keys.Dashscope.Length == 0 ? "DashScope" : null)
                : (keys.Speech.Length == 0 || keys.Translation.Length == 0 ? "xAI・翻訳API" : null);
            if (missing != null) { Status = $"未設定（{missing} APIキーを保存してください）"; return; }
            try { StartEngine(keys); }
            catch (Exception) { ShowError("自動翻訳を開始できません。接続設定とAPIキーを確認してください。"); }
        }
        public void ClearKeys()
        {
            StopEngine();
            try
            {
                File.Delete(Path.Combine(folder, "speech.key"));
                File.Delete(Path.Combine(folder, "translation.key"));
                File.Delete(Path.Combine(folder, "dashscope.key"));
                lastError = null; Status = "未設定（APIキーを削除しました）";
            }
            catch (Exception) { ShowError("APIキーの削除に失敗しました。"); }
        }
        public void ShowError(string message)
        {
            lastError = message; Status = message;
            Debug.LogError(message);
        }
        private void StartEngine((string Speech, string Translation, string Dashscope) keys)
        {
            var current = TranslatorComposition.Create(Config,
                Config.Mode == TranslationMode.Voice ? "" : keys.Speech,
                Config.Mode == TranslationMode.Voice ? "" : keys.Translation,
                Config.Mode == TranslationMode.Voice ? keys.Dashscope : "");
            engine = current; int generation = ++epoch;
            current.Caption += caption => deliveries.Enqueue(new Delivery(generation, caption, null, null));
            current.Audio += audio => deliveries.Enqueue(new Delivery(generation, null, null, audio));
            current.Fault += fault => deliveries.Enqueue(new Delivery(generation, null, fault, null));
            pumpCancel = new CancellationTokenSource();
            var token = pumpCancel.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        foreach (var capture in Volatile.Read(ref snapshot))
                        {
                            if (!capture.Active) continue;
                            if (capture.Buffer.Overflowed && Interlocked.Exchange(ref capture.OverflowReported, 1) == 0)
                            {
                                deliveries.Enqueue(new Delivery(generation, null, new TranslationFault(capture.Id, "音声取得バッファが上限に達しました。再接続してください。"), null));
                                capture.Active = false; continue;
                            }
                            while (capture.Active && capture.Buffer.TryPeek(out var frame))
                            {
                                current.PushAudio(capture.Id, frame); capture.Buffer.Release();
                            }
                        }
                        await Task.Delay(10, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception)
                { deliveries.Enqueue(new Delivery(generation, null, new TranslationFault(Guid.Empty, "音声処理が停止しました。再接続してください。"), null)); }
            });
            Status = "待機中（対象プレイヤーが発話すると接続します）";
        }
        private void StopEngine()
        {
            epoch++; pumpCancel?.Cancel(); pumpCancel?.Dispose(); pumpCancel = null;
            foreach (var c in captures.Values) Detach(c);
            captures.Clear(); Volatile.Write(ref snapshot, Array.Empty<Capture>());
            engine?.Dispose(); engine = null; failed.Clear(); excluded = 0;
            while (deliveries.TryDequeue(out _)) { }
        }
        private void Update()
        {
            if (engine == null) return;
            foreach (var c in Volatile.Read(ref snapshot))
            {
                var driver = EligibleDriver(c.Player);
                if (driver != c.Driver || !ReferenceEquals(driver?.BasisAudioReceiver, c.Receiver) || !IsTranslationTarget(c.Player))
                    Remove(c);
                else if (c.Receiver.IsAudioActive && c.Receiver.SourcePeak > 0.0005f) c.LastVoice = TranslationClock.Now;
                else if (TranslationClock.Now - c.LastVoice >= 30) Remove(c);
            }
            while (deliveries.TryDequeue(out var delivery))
            {
                if (delivery.Epoch != epoch) continue;
                if (delivery.Fault.HasValue)
                {
                    var fault = delivery.Fault.Value;
                    if (fault.Speaker == Guid.Empty) { StopEngine(); ShowError(fault.Message); return; }
                    if (captures.TryGetValue(fault.Speaker, out var c)) { failed.Add(c.Player); Remove(c); }
                    ShowError(fault.Message);
                }
                else if (delivery.Caption.HasValue && captures.TryGetValue(delivery.Caption.Value.Transcript.Speaker, out var captioned))
                    captioned.Caption.Apply(delivery.Caption.Value, TranslationClock.Now);
                else if (delivery.Audio.HasValue && captures.TryGetValue(delivery.Audio.Value.Speaker, out var voiced) && voiced.Voice != null)
                    voiced.Voice.Push(delivery.Audio.Value);
            }
            if (TranslationClock.Now >= nextScan)
            {
                nextScan = TranslationClock.Now + 0.1;
                foreach (var capture in captures.Values.ToArray())
                    if (capture.Driver.AudioData == null || !capture.Driver.AudioData.GetInvocationList().Contains(capture.Callback)) Remove(capture);
                var admitted = new HashSet<BasisRemotePlayer>(captures.Values.Select(c => c.Player));
                var local = BasisLocalPlayer.Instance;
                var candidates = BasisNetworkPlayers.RemotePlayers.Values.Where(p => !p.IsDestroyed && !admitted.Contains(p) && IsTranslationTarget(p))
                    .Select(p => (Player: p, Driver: EligibleDriver(p)))
                    .Where(x => x.Driver != null && x.Driver.BasisAudioReceiver.IsAudioActive && x.Driver.BasisAudioReceiver.SourcePeak > 0.0005f)
                    .OrderBy(x => local != null && x.Player.MouthTransform != null ? (x.Player.MouthTransform.position - local.transform.position).sqrMagnitude : 0).ToArray();
                excluded = 0;
                foreach (var candidate in candidates)
                {
                    if (captures.Count >= Config.MaxSpeakers || failed.Contains(candidate.Player)) { excluded++; continue; }
                    var capture = new Capture(candidate.Player, candidate.Driver, sampleRate);
                    try
                    {
                        engine.AddSpeaker(capture.Id);
                        if (Config.Mode == TranslationMode.Voice)
                        {
                            var mouth = candidate.Player.MouthTransform;
                            if (mouth == null) throw new InvalidOperationException("Mouth transform is missing.");
                            capture.Voice = TranslatedVoicePlayer.Create(mouth, candidate.Driver.BasisAudioReceiver.audioSource, candidate.Driver.IsAnnounceSource);
                        }
                        captures.Add(capture.Id, capture);
                        capture.Driver.AudioData += capture.Callback;
                        Volatile.Write(ref snapshot, captures.Values.ToArray());
                    }
                    catch (Exception)
                    {
                        if (captures.ContainsKey(capture.Id)) Remove(capture);
                        else
                        {
                            engine.RemoveSpeaker(capture.Id);
                            if (capture.Voice != null) capture.Voice.RestoreAndDestroy(candidate.Driver.BasisAudioReceiver.audioSource);
                        }
                        failed.Add(candidate.Player); ShowError("話者の音声処理を開始できません。再接続してください。");
                    }
                }
                failed.RemoveWhere(p => p.IsDestroyed || !IsTranslationTarget(p));
                if (lastError == null) Status = captures.Count == 0 ? "待機中（対象プレイヤーが発話すると接続します）" : $"処理対象: {captures.Count}人 / 対象外: {excluded}人";
            }
            foreach (var c in captures.Values)
            {
                if (Config.Mode == TranslationMode.Voice && c.Receiver.audioSource != null)
                    c.Receiver.audioSource.volume = Config.OriginalVoiceGain;
                if (c.Voice != null)
                    c.Voice.Gain = c.Receiver.PerPlayerVolume * SMModuleAudio.ActiveMainVolume;
                if (c.Subtitle == null && c.Caption.Original.Length > 0)
                {
                    var plate = c.Player.NamePlateTransformProvider?.Invoke();
                    if (plate != null) c.Subtitle = TranslatorSubtitle.Create(plate);
                }
                if (c.Subtitle != null) c.Subtitle.Render(c.Caption);
            }
        }
        private static bool IsTranslationTarget(BasisRemotePlayer player)
        {
            if (string.IsNullOrEmpty(player.UUID)) return false;
            if (BasisPlayerSettingsManager.TryGetCached(player.UUID, out var settings)) return settings.TranslationEnabled;
            BasisPlayerSettingsManager.Warm(player.UUID);
            return false;
        }
        private static BasisRemoteAudioDriver EligibleDriver(BasisRemotePlayer player)
        {
            if (player.IsDestroyed || player.IsLocal || player.IsEffectivelyBlocked || player.IsSelfMuted || SMModuleAudio.ActiveMainVolume <= 0)
                return null;
            var receiver = player.NetworkReceiver?.AudioReceiverModule;
            if (receiver == null || receiver.PerPlayerVolume <= 0) return null;
            if (BasisAnnounceAudioDriver.TryGetAudioDriver(player.NetworkReceiver.playerId, out var announce)) return announce;
            var driver = receiver.BasisRemoteVisemeAudioDriver;
            return receiver.HasAudioSource && driver != null && driver.Initialized ? driver : null;
        }
        private void Remove(Capture c)
        {
            Detach(c); captures.Remove(c.Id); engine?.RemoveSpeaker(c.Id);
            Volatile.Write(ref snapshot, captures.Values.ToArray());
        }
        private static void Detach(Capture c)
        {
            c.Active = false;
            if (c.Driver != null) c.Driver.AudioData -= c.Callback;
            if (c.Voice != null) { c.Voice.RestoreAndDestroy(c.Receiver.audioSource); c.Voice = null; }
            else if (c.Receiver.audioSource != null) c.Receiver.audioSource.volume = 1;
            if (c.Subtitle != null) Destroy(c.Subtitle.gameObject);
        }
        private void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= AudioChanged;
            StopEngine();
            SettingsProvider.ExternalTabs.RemoveAll(tab => tab.TabName == "自動翻訳");
            if (Instance == this) Instance = null;
        }
    }
}
