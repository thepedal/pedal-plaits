// ──────────────────────────────────────────────────────────────────────
// PlaitsWavetables.cs — port of plaits/resources/wavetables.py
//
// Provides the wave-generation functions Plaits uses to build its
// wavetable engine's bank_1 (mild additive) and bank_2 (formantish).
// The functions accept a target table size (Plaits hardware uses 128;
// this port uses 256 to match Pedal Plaits' existing engine geometry —
// audibly equivalent, just oversampled).
//
// Plaits' bank_3 (shruthi/ambika/braids-derived) is not ported here:
// it requires plaits/resources/waves.bin (a binary blob of Braids
// waveform data), which we don't redistribute. Pedal Plaits keeps its
// existing algorithmic generation for that bank slot.
//
// All functions return raw waveforms (no normalisation, no DC removal
// — the caller does both). Functions match the Python algorithms; not
// bit-identical due to float-vs-double arithmetic but spectrally
// equivalent.
//
// Original Python: Émilie Gillet (Mutable Instruments), MIT license.
// C# port: this project, MIT license (preserves original attribution).
// ──────────────────────────────────────────────────────────────────────

using System;

namespace PedalPlaits.Engines
{
    internal static class PlaitsWavetables
    {
        // ─────────────────────────────────────────────────────────
        // Building blocks (correspond to Python helpers in wavetables.py)
        // ─────────────────────────────────────────────────────────

        // sine(frequency) — a sinewave of `frequency` cycles per table.
        // Cap at Nyquist: frequencies >= N/2 return silence (anti-alias).
        static float[] Sine(int n, float frequency)
        {
            var x = new float[n];
            if (frequency >= n / 2f) return x;
            float k = 2f * MathF.PI * frequency / n;
            for (int i = 0; i < n; i++) x[i] = MathF.Sin(k * i);
            return x;
        }

        // comb(n) — sum of sines 1..n at unit amplitude.
        static float[] Comb(int n, int harmonics)
        {
            var x = new float[n];
            for (int h = 1; h <= harmonics; h++)
            {
                var s = Sine(n, h);
                for (int i = 0; i < n; i++) x[i] += s[i];
            }
            return x;
        }

        // pair(n) — harmonic stack with paired 4th-harmonic emphasis.
        static float[] Pair(int n, int harmonics)
        {
            var x = new float[n];
            float denom = harmonics - 1f;
            if (denom < 1e-6f) denom = 1f;
            for (int h = 0; h < harmonics; h++)
            {
                float amp = (h + 0.5f) / denom;
                var s1 = Sine(n, h + 1);
                var s4 = Sine(n, (h + 1) * 4);
                for (int i = 0; i < n; i++) x[i] += s1[i] * amp + s4[i] * amp * 0.5f;
            }
            return x;
        }

        // tri(n, f) — triangle approximation as sum of odd harmonics
        // with 1/(2k+1)^2 amplitude, at fundamental frequency f.
        static float[] Tri(int n, int harmonics, float f = 1f)
        {
            var x = new float[n];
            for (int k = 0; k < harmonics; k++)
            {
                float kk = 2 * k + 1;
                var s = Sine(n, kk * f);
                float amp = 1f / (kk * kk);
                for (int i = 0; i < n; i++) x[i] += s[i] * amp;
            }
            return x;
        }

        // tri_stack(n) — stack of N triangles at progressive fundamentals.
        // Note: Python uses Python-2 integer division on count/3; preserve that
        // here so the stack frequencies match the original exactly.
        static float[] TriStack(int n, int count)
        {
            var x = new float[n];
            int countDiv3 = count / 3;  // intentional integer division
            for (int i = 0; i < count; i++)
            {
                var t = Tri(n, 15 + 5 * count, i + countDiv3);
                for (int j = 0; j < n; j++) x[j] += t[j];
            }
            return x;
        }

        // saw(n, f) — sawtooth as sum of harmonics 1..n with 1/k amplitude.
        static float[] Saw(int n, int harmonics, float f = 1f)
        {
            var x = new float[n];
            for (int k = 0; k < harmonics; k++)
            {
                var s = Sine(n, (k + 1) * f);
                float amp = 1f / (k + 1);
                for (int i = 0; i < n; i++) x[i] += s[i] * amp;
            }
            return x;
        }

        // quadra(n) — 4 specifically-chosen harmonics around 2n+1.
        static float[] Quadra(int n, int param)
        {
            float[] amps = { 1f, 0.5f, 1f, 0.5f };
            var x = new float[n];
            for (int h = 0; h < 4; h++)
            {
                var s = Sine(n, 2 * param + 2 * h + 1);
                for (int i = 0; i < n; i++) x[i] += s[i] * amps[h];
            }
            return x;
        }

        // drawbars(bars) — Hammond-style additive at 9 Hammond pipe ratios.
        // bars is a 9-character string of digits 0..9 (intensity per pipe).
        static float[] Drawbars(int n, string bars)
        {
            float[] pipes = { 1f, 3f, 2f, 4f, 6f, 8f, 10f, 12f, 16f };
            var x = new float[n];
            for (int i = 0; i < 9; i++)
            {
                int intensity = bars[i] - '0';
                if (intensity == 0) continue;
                float amp = intensity / 8f;
                var s = Sine(n, pipes[i]);
                for (int j = 0; j < n; j++) x[j] += s[j] * amp;
            }
            return x;
        }

        // pulse(duty) — variable-duty pulse, ±1 amplitude.
        // Python sets t[-1] = t[0] to make the last sample wrap to phase 0;
        // the C# port mirrors that one-sample wrap exactly.
        static float[] Pulse(int n, float duty)
        {
            var x = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (i == n - 1) ? 0f : i / (float)n;
                x[i] = (t < duty) ? 1f : -1f;
            }
            return x;
        }

        // burst(duty) — pulse-gated sine, period 1/duty.
        static float[] Burst(int n, float duty)
        {
            float d = MathF.Sqrt(duty);
            var s = Sine(n, 1f / duty);
            var x = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (i == n - 1) ? 0f : i / (float)n;
                x[i] = (t < d) ? s[i] : 0f;
            }
            return x;
        }

        // trisaw(h) — triangle plus a small saw component at harmonic h.
        static float[] Trisaw(int n, float h)
        {
            var x = Tri(n, 80);
            var saw = Saw(n, 80, h);
            float factor = (MathF.Abs(h - 1f) > 1e-6f ? 1f : 0.25f) * 0.5f;
            for (int i = 0; i < n; i++) x[i] += saw[i] * factor;
            return x;
        }

        // sawtri(h) — saw plus a triangle component at harmonic h.
        static float[] Sawtri(int n, float h)
        {
            var saw = Saw(n, 80);
            for (int i = 0; i < n; i++) saw[i] *= 0.5f;
            var tri = Tri(n, 80, h);
            float factor = (MathF.Abs(h - 1f) > 1e-6f ? 1f : 0.25f);
            for (int i = 0; i < n; i++) saw[i] += tri[i] * factor;
            return saw;
        }

        // bandpass_formant(ratio) — sine with linear-decay envelope.
        static float[] BandpassFormant(int n, float ratio)
        {
            var s = Sine(n, ratio * 1.5f);
            var x = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                x[i] = s[i] * (1f - t) * 0.5f;
            }
            return x;
        }

        // formant_f(index) — 3-formant composite synth voice.
        static float[] FormantF(int n, int index)
        {
            float f1 = 3.9f * (index + 1) / 8f;
            float f2 = f1 * (1f - MathF.Cos(f1 * MathF.PI * 0.8f));
            var s1 = Sine(n, 1f + 3f * f1);
            var s2 = Sine(n, 1f + 4f * f2);
            var s3 = Sine(n, 1f + 2.8f * (f1 + f2));
            var x = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float a1 = MathF.Pow(1f - t, 0.2f) * MathF.Exp(-4f * t);
                float a2 = MathF.Pow(1f - t, 0.2f) * MathF.Exp(-2f * t);
                x[i] = s1[i] * a1 + s2[i] * a2 * 1.5f + s3[i] * a2 * 1.7f;
            }
            CenterMinMax(x);
            return x;
        }

        // digi_formant_f(index) — formant_f with arctan distortion on each
        // sine before envelope shaping.
        static float[] DigiFormantF(int n, int index)
        {
            float f1 = 3.9f * (index + 1) / 8f;
            float f2 = f1 * (1f - MathF.Cos(f1 * MathF.PI * 0.8f));
            var s1 = Sine(n, 1f + 3.2f * f1);
            var s2 = Sine(n, 1f + 4.1f * f2);
            var s3 = Sine(n, 1f + 2.9f * (f1 + f2));
            for (int i = 0; i < n; i++)
            {
                s1[i] = MathF.Atan(s1[i] * 8f) / MathF.PI;
                s2[i] = MathF.Atan(s2[i] * 8f) / MathF.PI;
                s3[i] = MathF.Atan(s3[i] * 8f) / MathF.PI;
            }
            var x = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float a1 = MathF.Pow(1f - t, 0.2f) * MathF.Exp(-4f * t);
                float a2 = MathF.Pow(1f - t, 0.2f) * MathF.Exp(-2f * t);
                x[i] = s1[i] * a1 + s2[i] * a2 * 0.7f + s3[i] * a2 * 0.7f;
            }
            CenterMinMax(x);
            return x;
        }

        // sine_power(power) — sign-preserving power curve applied to
        // (sine + multi-harmonic saw).
        static float[] SinePower(int n, int power)
        {
            var x = Sine(n, 1f);
            var saw = Saw(n, 16);
            for (int i = 0; i < n; i++) x[i] += saw[i];
            float p = MathF.Pow(2f, power);
            for (int i = 0; i < n; i++)
                x[i] = MathF.Sign(x[i]) * MathF.Pow(MathF.Abs(x[i]), p);
            return x;
        }

        // ─────────────────────────────────────────────────────────
        // Post-processing helpers
        // ─────────────────────────────────────────────────────────

        // Subtract (max+min)/2 — removes DC offset while preserving peaks
        // (different from removing mean; Plaits uses this for formants).
        static void CenterMinMax(float[] x)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] < lo) lo = x[i];
                if (x[i] > hi) hi = x[i];
            }
            float c = (hi + lo) * 0.5f;
            for (int i = 0; i < x.Length; i++) x[i] -= c;
        }

        // Subtract mean — true DC removal.
        static void RemoveMean(float[] x)
        {
            float sum = 0f;
            for (int i = 0; i < x.Length; i++) sum += x[i];
            float m = sum / x.Length;
            for (int i = 0; i < x.Length; i++) x[i] -= m;
        }

        // Normalize to ±1 peak.
        public static void NormalizePeak(float[] x)
        {
            float peak = 0f;
            for (int i = 0; i < x.Length; i++)
            {
                float av = MathF.Abs(x[i]);
                if (av > peak) peak = av;
            }
            if (peak > 1e-6f)
            {
                float inv = 1f / peak;
                for (int i = 0; i < x.Length; i++) x[i] *= inv;
            }
        }

        // ─────────────────────────────────────────────────────────
        // Bank builders — exact reproductions of bank_1 and bank_2
        // from wavetables.py, with rows = families and cols = parameter
        // values within each family. Returns 64 waves of length n each.
        // ─────────────────────────────────────────────────────────

        public static float[][] BuildBank1(int n)
        {
            var waves = new float[64][];
            int idx = 0;

            // Row 0: sine, 1..8 cycles per table
            int[] r0 = { 1, 2, 3, 4, 5, 6, 7, 8 };
            for (int c = 0; c < 8; c++) waves[idx++] = Sine(n, r0[c]);

            // Row 1: sine, fast-growing cycle counts
            int[] r1 = { 2, 3, 4, 6, 8, 12, 16, 24 };
            for (int c = 0; c < 8; c++) waves[idx++] = Sine(n, r1[c]);

            // Row 2: quadra at the same set
            for (int c = 0; c < 8; c++) waves[idx++] = Quadra(n, r1[c]);

            // Row 3: comb, Fibonacci-ish growth
            int[] r3 = { 2, 3, 5, 8, 13, 21, 34, 55 };
            for (int c = 0; c < 8; c++) waves[idx++] = Comb(n, r3[c]);

            // Row 4: pair, even harmonic counts
            int[] r4 = { 2, 4, 6, 8, 10, 12, 14, 16 };
            for (int c = 0; c < 8; c++) waves[idx++] = Pair(n, r4[c]);

            // Row 5: tri_stack at the same counts
            for (int c = 0; c < 8; c++) waves[idx++] = TriStack(n, r4[c]);

            // Rows 6 and 7: two banks of 8 Hammond drawbar registrations
            string[] r6 = { "688600000", "686040000", "666806000", "655550600",
                            "665560060", "688500888", "660000888", "060000046" };
            for (int c = 0; c < 8; c++) waves[idx++] = Drawbars(n, r6[c]);

            string[] r7 = { "867000006", "888876788", "668744354", "448644054",
                            "327645222", "204675300", "002478500", "002050321" };
            for (int c = 0; c < 8; c++) waves[idx++] = Drawbars(n, r7[c]);

            return waves;
        }

        public static float[][] BuildBank2(int n)
        {
            var waves = new float[64][];
            int idx = 0;

            // Row 0: trisaw at 8 fundamental harmonic ratios
            float[] r0 = { 1f, 1.5f, 2f, 3f, 4f, 4.5f, 5f, 8f };
            for (int c = 0; c < 8; c++) waves[idx++] = Trisaw(n, r0[c]);

            // Row 1: sawtri at the same ratios
            for (int c = 0; c < 8; c++) waves[idx++] = Sawtri(n, r0[c]);

            // Row 2: burst with varying duty cycles
            float[] r2 = { 0.5f, 0.4f, 1f / 3f, 0.25f, 0.2f, 0.125f, 1f / 16f, 1f / 32f };
            for (int c = 0; c < 8; c++) waves[idx++] = Burst(n, r2[c]);

            // Row 3: bandpass formants at 8 sine-frequency ratios
            float[] r3 = { 2f, 3f, 4f, 6f, 8f, 9f, 10f, 16f };
            for (int c = 0; c < 8; c++) waves[idx++] = BandpassFormant(n, r3[c]);

            // Row 4: formant_f
            for (int c = 0; c < 8; c++) waves[idx++] = FormantF(n, c);

            // Row 5: digi_formant_f
            for (int c = 0; c < 8; c++) waves[idx++] = DigiFormantF(n, c);

            // Row 6: pulse at the same duty cycles as burst
            for (int c = 0; c < 8; c++) waves[idx++] = Pulse(n, r2[c]);

            // Row 7: sine_power 0..7
            for (int c = 0; c < 8; c++) waves[idx++] = SinePower(n, c);

            return waves;
        }
    }
}
