using System;
using UnityEngine;

namespace Handshake.SplitKeys
{
    // Every sound is synthesized at startup, so the build ships no audio files.
    public sealed class SkAudio : MonoBehaviour
    {
        const int Rate = 44100;
        const float Master = 0.5f;

        AudioSource[] pool;
        int next;
        bool muted;
        System.Random random = new System.Random(7);

        AudioClip clack, buzz, beep, go, tick, whoosh, jingle, timeUp, fanfare, sad, join;

        public bool Muted => muted;

        public void Init(bool startMuted)
        {
            pool = new AudioSource[6];
            for (var i = 0; i < pool.Length; i++)
            {
                pool[i] = gameObject.AddComponent<AudioSource>();
                pool[i].playOnAwake = false;
            }
            clack = Make("clack", 0.045f, Clack);
            join = Make("join", 0.06f, (t, i) => Clack(t, i) * 0.6f + Sine(t, 520f + t * 3000f) * Env(t, 0.004f, 0.06f) * 0.15f);
            buzz = Make("buzz", 0.2f, Buzz);
            beep = Make("beep", 0.1f, (t, i) => Sine(t, 660f) * Env(t, 0.004f, 0.09f) * 0.5f);
            go = Make("go", 0.17f, (t, i) => Sine(t, 990f) * Env(t, 0.004f, 0.16f) * 0.5f);
            tick = Make("tick", 0.03f, (t, i) => Sine(t, 1400f) * Env(t, 0.002f, 0.025f) * 0.3f);
            whoosh = Make("whoosh", 0.32f, Whoosh);
            jingle = Make("jingle", 0.42f, (t, i) => Notes(t, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.07f, 0.08f, Triangle) * 0.45f);
            timeUp = Make("timeup", 0.48f, TimeUp);
            fanfare = Make("fanfare", 0.75f, (t, i) => Notes(t, new[] { 523.25f, 783.99f, 659.25f, 1046.5f }, 0.12f, 0.12f, Triangle, 0.4f) * 0.45f);
            sad = Make("sad", 0.9f, (t, i) => Notes(t, new[] { 329.63f, 293.66f, 261.63f }, 0.26f, 0.25f, Triangle, 0.4f) * 0.45f);
            SetMuted(startMuted);
        }

        public void SetMuted(bool value)
        {
            muted = value;
            AudioListener.volume = muted ? 0f : 1f;
        }

        public void Clack(int playerIndex) => Play(clack, 0.9f, 1f + 0.1f * Mathf.Clamp(playerIndex, 0, 7));
        public void Join() => Play(join, 0.6f, 1f);
        public void Buzz() => Play(buzz, 0.8f, 1f);
        public void Beep() => Play(beep, 0.8f, 1f);
        public void Go() => Play(go, 0.8f, 1f);
        public void Tick() => Play(tick, 0.7f, 1f);
        public void Whoosh() => Play(whoosh, 0.8f, 1f);
        public void Jingle() => Play(jingle, 0.9f, 1f);
        public void TimeUp() => Play(timeUp, 0.9f, 1f);
        public void Fanfare(bool happy) => Play(happy ? fanfare : sad, 0.9f, 1f);

        void Play(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || pool == null || muted) return;
            var source = pool[next];
            next = (next + 1) % pool.Length;
            source.pitch = pitch;
            source.volume = volume * Master;
            source.clip = clip;
            source.Play();
        }

        AudioClip Make(string name, float seconds, Func<float, int, float> sample)
        {
            var count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            for (var i = 0; i < count; i++) data[i] = Mathf.Clamp(sample(i / (float)Rate, i), -1f, 1f);
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Sine(float t, float f) => Mathf.Sin(2f * Mathf.PI * f * t);

        static float Triangle(float t, float f)
        {
            var phase = t * f - Mathf.Floor(t * f);
            return 4f * Mathf.Abs(phase - 0.5f) - 1f;
        }

        static float Square(float t, float f) => (t * f - Mathf.Floor(t * f)) < 0.5f ? 1f : -1f;

        static float Saw(float t, float f) => 2f * (t * f - Mathf.Floor(t * f)) - 1f;

        // Quick attack, linear-ish release to silence at `length`.
        static float Env(float t, float attack, float length)
        {
            if (t < attack) return t / attack;
            if (t >= length) return 0f;
            var k = 1f - (t - attack) / Mathf.Max(0.0001f, length - attack);
            return k * k;
        }

        static float Notes(float t, float[] freqs, float spacing, float length, Func<float, float, float> wave, float lastLength = -1f)
        {
            var sum = 0f;
            for (var n = 0; n < freqs.Length; n++)
            {
                var start = n * spacing;
                var len = n == freqs.Length - 1 && lastLength > 0f ? lastLength : length;
                if (t < start || t > start + len) continue;
                sum += wave(t - start, freqs[n]) * Env(t - start, 0.006f, len);
            }
            return sum;
        }

        // Stateful filters for the noise-based sounds; reset per clip by checking the sample index.
        float bx1, bx2, by1, by2, lp;

        float Bandpass(float x, float freq, float q)
        {
            var w0 = 2f * Mathf.PI * freq / Rate;
            var alpha = Mathf.Sin(w0) / (2f * q);
            var a0 = 1f + alpha;
            var y = (alpha * x - alpha * bx2 + 2f * Mathf.Cos(w0) * by1 - (1f - alpha) * by2) / a0;
            bx2 = bx1; bx1 = x; by2 = by1; by1 = y;
            return y;
        }

        float Noise() => (float)(random.NextDouble() * 2.0 - 1.0);

        float Clack(float t, int i)
        {
            if (i == 0) { bx1 = bx2 = by1 = by2 = 0f; }
            var click = Bandpass(Noise() * Env(t, 0.0008f, 0.012f), 1800f, 3f) * 2.4f;
            var thump = Sine(t, 140f) * Env(t, 0.001f, 0.02f) * 0.5f;
            return click + thump;
        }

        float Buzz(float t, int i)
        {
            if (i == 0) lp = 0f;
            var raw = Square(t, 110f) * Env(t, 0.004f, 0.18f) * 0.45f;
            var k = 1f - Mathf.Exp(-2f * Mathf.PI * 1200f / Rate);
            lp += (raw - lp) * k;
            return lp;
        }

        float Whoosh(float t, int i)
        {
            if (i == 0) { bx1 = bx2 = by1 = by2 = 0f; }
            var freq = Mathf.Lerp(400f, 3000f, t / 0.3f);
            var shape = Mathf.Sin(Mathf.Clamp01(t / 0.3f) * Mathf.PI);
            return Bandpass(Noise(), freq, 1.2f) * shape * 0.9f;
        }

        float TimeUp(float t, int i)
        {
            if (i == 0) lp = 0f;
            var raw = t < 0.22f ? Saw(t, 196f) * Env(t, 0.005f, 0.22f) : Saw(t - 0.24f, 146.83f) * Env(t - 0.24f, 0.005f, 0.22f);
            if (t >= 0.22f && t < 0.24f) raw = 0f;
            var k = 1f - Mathf.Exp(-2f * Mathf.PI * 800f / Rate);
            lp += (raw * 0.6f - lp) * k;
            return lp;
        }
    }
}
