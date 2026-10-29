using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuckovSharedBike
{
    /// <summary>
    /// 程序合成音效（无外部资源）：按采样生成短音并缓存，AudioSource 播放。
    /// 铃声种类：PaidChime 扣款成功叮、ShortChime 低鸣提示、RentTone 租车双音、
    /// SummonTone 召车三连叮、RefundTone 退款。
    /// </summary>
    internal sealed class BikeSounds : IDisposable
    {
        private const int Rate = 44100;
        private readonly Action<string> _log;
        private readonly Dictionary<string, AudioClip> _clips = new(StringComparer.Ordinal);
        private AudioSource? _source;
        private bool _disposed;

        public BikeSounds(Action<string> log) => _log = log;

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
                _source.spatialBlend = 0f; // 2D UI 音
                _source.volume = 0.8f;
                return _source;
            }
        }

        public void PaidChime() => Play("paid", Build("paid", 0.35f, t => Bell(t, 880f, 0.5f) + Bell(t, 1320f, 0.35f)));
        public void ShortChime() => Play("short", Build("short", 0.3f, t => Bell(t, 220f, 0.6f)));
        public void RentTone() => Play("rent", Build("rent", 0.4f, t => t < 0.18f ? Bell(t, 660f, 0.5f) : Bell(t - 0.18f, 990f, 0.5f)));
        public void RefundTone() => Play("refund", Build("refund", 0.4f, t => t < 0.18f ? Bell(t, 990f, 0.5f) : Bell(t - 0.18f, 660f, 0.5f)));
        public void SummonTone() => Play("summon", BuildSummon());

        private static float Bell(float t, float hz, float decay)
        {
            if (t < 0) return 0f;
            var env = Mathf.Exp(-t * decay * 9f);
            return env * (Mathf.Sin(2f * Mathf.PI * hz * t) * 0.7f
                        + Mathf.Sin(2f * Mathf.PI * hz * 2f * t) * 0.2f
                        + Mathf.Sin(2f * Mathf.PI * hz * 3f * t) * 0.1f);
        }

        private static AudioClip Build(string name, float seconds, Func<float, float> gen)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(gen(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create("sbk-" + name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildSummon()
        {
            // 三连叮：0/0.12/0.24s 各一声 1046Hz（C6），总长 0.6s
            double[] offsets = { 0.0, 0.12, 0.24 };
            return Build("summon", 0.6f, t =>
            {
                float v = 0f;
                foreach (var off in offsets)
                    v += Bell(t - (float)off, 1046f, 0.55f);
                return v * 0.8f;
            });
        }

        private void Play(string key, AudioClip clip)
        {
            if (_disposed || clip == null) return;
            _clips[key] = clip;
            try { Source.PlayOneShot(clip); }
            catch (Exception ex) { _log("sound play failed: " + ex.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var clip in _clips.Values)
                if (clip != null) UnityEngine.Object.Destroy(clip);
            _clips.Clear();
            if (_source != null) UnityEngine.Object.Destroy(_source.gameObject);
            _source = null;
        }
    }
}
