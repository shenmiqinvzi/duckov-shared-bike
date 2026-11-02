using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuckovSharedBike
{
    /// <summary>
    /// 程序合成音效（无外部资源）：按采样生成短音并缓存，AudioSource 播放。
    /// 铃声种类：PaidChime 扣款成功叮、ShortChime 低鸣提示、RentTone 租车双音、
    /// SummonTone 召车三连叮、RefundTone 退款。
    ///
    /// 1.0.10：部分环境里 AudioClip.Create 得到的静态 clip 没有可写缓冲，
    /// AudioClip.SetData 返回 false 且只在 Player.log 打出
    /// "AudioClip.SetData failed; AudioClip  contains no data"，
    /// 于是所有提示音静默而模组日志毫无异常。首次失败后改为流式 PCM 回调 clip
    /// （不依赖可写缓冲），并把切换写进模组日志。
    /// </summary>
    internal sealed class BikeSounds : IDisposable
    {
        private const int Rate = 44100;
        private readonly Action<string> _log;
        private readonly Dictionary<string, AudioClip> _clips = new(StringComparer.Ordinal);
        private readonly List<AudioClip> _transient = new();
        private AudioSource? _source;
        private bool _disposed;
        private bool _streaming;
        private bool _reportedStreaming;
        private bool _reportedEmpty;
        private AudioClip? _borrowed;
        private bool _borrowedSearched;
        private float _nextTransientCleanup;

        public BikeSounds(Action<string> log) => _log = log;

        /// <summary>Ui.Sounds=false 时全部静音，调用点不必各自判断。</summary>
        public bool Enabled { get; set; } = true;

        internal bool StreamingFallback => _streaming;

        private AudioSource Source
        {
            get
            {
                if (_source != null) return _source;
                var go = new GameObject("SharedBikeSounds");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _source = go.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.loop = false;
                _source.spatialBlend = 0f; // 2D UI 音
                _source.volume = 0.9f;
                // 打开背包/暂停时游戏可能整体暂停 AudioListener，提示音不该一起被吞掉。
                _source.ignoreListenerPause = true;
                _source.bypassEffects = true;
                _source.bypassListenerEffects = true;
                _source.bypassReverbZones = true;
                return _source;
            }
        }

        public void PaidChime() => Play("paid", 0.35f, t => Bell(t, 880f, 0.5f) + Bell(t, 1320f, 0.35f));
        public void ShortChime() => Play("short", 0.3f, t => Bell(t, 220f, 0.6f));
        public void RentTone() => Play("rent", 0.4f, t => t < 0.18f ? Bell(t, 660f, 0.5f) : Bell(t - 0.18f, 990f, 0.5f));
        public void RefundTone() => Play("refund", 0.4f, t => t < 0.18f ? Bell(t, 990f, 0.5f) : Bell(t - 0.18f, 660f, 0.5f));
        public void SummonTone() => Play("summon", 0.6f, t =>
        {
            float v = 0f;
            foreach (var off in SummonOffsets) v += Bell(t - off, 1046f, 0.55f);
            return v * 0.8f;
        });

        private static readonly float[] SummonOffsets = { 0f, 0.12f, 0.24f };

        private static float Bell(float t, float hz, float decay)
        {
            if (t < 0) return 0f;
            var env = Mathf.Exp(-t * decay * 9f);
            return env * (Mathf.Sin(2f * Mathf.PI * hz * t) * 0.7f
                        + Mathf.Sin(2f * Mathf.PI * hz * 2f * t) * 0.2f
                        + Mathf.Sin(2f * Mathf.PI * hz * 3f * t) * 0.1f);
        }

        private void Play(string key, float seconds, Func<float, float> gen)
        {
            if (_disposed || !Enabled) return;
            try
            {
                CleanupTransient();
                var clip = Clip(key, seconds, gen);
                if (clip == null) { ReportUnavailable(key, "clip creation returned null"); return; }
                // 无数据的 clip 在部分环境里 length 为 0，播放只会静默退出；先记日志再放弃。
                if (clip.length <= 0.0001f)
                {
                    var borrowed = BorrowedClip();
                    if (borrowed == null) { ReportUnavailable(key, "clip length is zero"); return; }
                    Source.pitch = 1.4f;
                    Source.PlayOneShot(borrowed);
                    return;
                }
                Source.pitch = 1f;
                Source.PlayOneShot(clip);
            }
            catch (Exception ex) { _log("sound play failed: " + ex); }
        }

        // 最后一级兜底：运行时合成完全不可用时，借用游戏里已加载的短提示音。
        private AudioClip? BorrowedClip()
        {
            if (_borrowedSearched) return _borrowed;
            _borrowedSearched = true;
            try
            {
                foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>())
                {
                    if (clip == null) continue;
                    if (clip.length < 0.08f || clip.length > 0.5f) continue;
                    var name = clip.name ?? string.Empty;
                    if (name.Length == 0) continue;
                    if (_borrowed == null) _borrowed = clip;
                    if (name.IndexOf("ui", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("click", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("notif", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("beep", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("chime", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("confirm", StringComparison.OrdinalIgnoreCase) >= 0)
                    { _borrowed = clip; break; }
                }
            }
            catch (Exception ex) { _log("sound: game clip search failed: " + ex.Message); }
            if (_borrowed != null) _log("sound: borrowed game clip '" + _borrowed.name + "' for cues.");
            return _borrowed;
        }

        private void ReportUnavailable(string key, string reason)
        {
            if (_reportedEmpty) return;
            _reportedEmpty = true;
            _log("sound: " + key + " unavailable (" + reason + "); audio output may be disabled.");
        }

        private AudioClip? Clip(string key, float seconds, Func<float, float> gen)
        {
            if (!_streaming && _clips.TryGetValue(key, out var cached)) return cached;
            var samples = Render(seconds, gen);
            var clip = Build(key, samples);
            if (clip == null) return null;
            if (_streaming) _transient.Add(clip); else _clips[key] = clip;
            return clip;
        }

        private static float[] Render(float seconds, Func<float, float> gen)
        {
            var count = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
            var data = new float[count];
            for (var i = 0; i < count; i++) data[i] = Mathf.Clamp(gen(i / (float)Rate), -1f, 1f);
            return data;
        }

        private AudioClip? Build(string key, float[] samples)
        {
            var name = "sbk-" + key;
            if (!_streaming)
            {
                var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
                if (clip != null && clip.SetData(samples, 0)) return clip;
                if (clip != null) UnityEngine.Object.Destroy(clip);
                _streaming = true;
                if (!_reportedStreaming)
                {
                    _reportedStreaming = true;
                    _log("sound: AudioClip.SetData unavailable; using streaming PCM clips.");
                }
            }
            // 回退：由 PCM 回调直接供数，绕开"静态缓冲为空"这条失败路径。
            var cursor = 0;
            return AudioClip.Create(name, samples.Length, 1, Rate, true, buffer =>
            {
                for (var i = 0; i < buffer.Length; i++) buffer[i] = cursor < samples.Length ? samples[cursor++] : 0f;
            });
        }

        // 流式 clip 每次播放都要新建（回调位置不可重置），播完一秒后销毁。
        private void CleanupTransient()
        {
            if (_transient.Count == 0 || Time.unscaledTime < _nextTransientCleanup) return;
            _nextTransientCleanup = Time.unscaledTime + 1f;
            foreach (var clip in _transient) if (clip != null) UnityEngine.Object.Destroy(clip);
            _transient.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var clip in _clips.Values)
                if (clip != null) UnityEngine.Object.Destroy(clip);
            _clips.Clear();
            foreach (var clip in _transient) if (clip != null) UnityEngine.Object.Destroy(clip);
            _transient.Clear();
            if (_source != null) UnityEngine.Object.Destroy(_source.gameObject);
            _source = null;
        }
    }
}
