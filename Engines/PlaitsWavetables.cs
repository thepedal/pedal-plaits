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
        // Braids-derived bank_3 support (v1.6).
        //
        // wavetables.py's make_braids_family() reads waves.bin (256 waves
        // × 129-byte stride, uint8) and for each requested index either:
        //   - fix=True (default): FFT → keep magnitudes, zero phases to
        //     -π/2 → IFFT. Produces a sine-coherent waveform where every
        //     frequency component appears as a pure sine, regardless of
        //     where the original was sampled in its cycle.
        //   - fix=False: pass through unchanged (modulo signed conversion).
        //
        // Port strategy: rather than implement a full FFT/IFFT pair, we
        // compute spectral magnitudes via DFT (only need 63 bins for a
        // 128-point input) then synthesize the output as a sum of sines.
        // This is mathematically equivalent to the FFT-magnitude-IFFT
        // pipeline when phases are all -π/2 (which gives pure sines), and
        // skips the inverse-transform entirely.
        //
        // Cost analysis: 48 fix=True waves × ~128² DFT + 128² sine
        // synthesis ≈ 1.5 M MathF.Cos/Sin per startup. ~30 ms on a modern
        // CPU; runs once when engine 4 first initialises.
        // ─────────────────────────────────────────────────────────

        static byte[] s_braidsWaves;     // lazy-loaded from embedded resource
        static bool s_braidsLoadAttempted;

        // Load waves.bin embedded as a resource in the assembly. Returns
        // null if the resource is missing — caller should fall back to
        // algorithmic generation in that case.
        static byte[] LoadBraidsWaves()
        {
            if (s_braidsLoadAttempted) return s_braidsWaves;
            s_braidsLoadAttempted = true;

            var asm = typeof(PlaitsWavetables).Assembly;
            // Try canonical name first, then any resource ending in waves.bin
            // (insulates against RootNamespace changes).
            System.IO.Stream stream = asm.GetManifestResourceStream("PedalPlaits.Resources.waves.bin");
            if (stream == null)
            {
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("waves.bin", StringComparison.Ordinal))
                    {
                        stream = asm.GetManifestResourceStream(name);
                        break;
                    }
                }
            }
            if (stream == null) return null;

            try
            {
                var data = new byte[stream.Length];
                int offset = 0;
                while (offset < data.Length)
                {
                    int read = stream.Read(data, offset, data.Length - offset);
                    if (read <= 0) break;
                    offset += read;
                }
                s_braidsWaves = data;
                return data;
            }
            finally
            {
                stream.Dispose();
            }
        }

        // make_braids_family with fix=True. Extract 128 bytes for wave
        // `index`, compute spectral magnitudes via DFT, synthesize a sum
        // of sines of those magnitudes. Resamples to the engine's wave
        // length (typically 256) via linear interpolation.
        static float[] BraidsFamilyFix(byte[] waves, int index, int outLen)
        {
            const int N = 128;
            int start = index * 129;

            // Signed input (subtract 128 from uint8 byte values)
            var x = new float[N];
            for (int i = 0; i < N; i++) x[i] = waves[start + i] - 128f;

            // Spectral magnitudes via DFT — bins 1..N/2-1 only
            // (DC and Nyquist are dropped because phase-zeroing them
            // produces zero contribution to the real output).
            const int K = N / 2;     // 64 — Nyquist bin index
            var mags = new float[K]; // index k - 1 holds |X[k]| for k=1..K-1
            for (int k = 1; k < K; k++)
            {
                float re = 0f, im = 0f;
                float kOmega = 2f * MathF.PI * k / N;
                for (int nn = 0; nn < N; nn++)
                {
                    re += x[nn] * MathF.Cos(kOmega * nn);
                    im -= x[nn] * MathF.Sin(kOmega * nn);
                }
                mags[k - 1] = MathF.Sqrt(re * re + im * im);
            }

            // Synthesize: y[n] = (2/N) × Σ_k |X[k]| × sin(2π k n / N)
            // (factor of 2 comes from collapsing positive+negative frequency
            // contributions for real signals.)
            var yShort = new float[N];
            float twoPiOverN = 2f * MathF.PI / N;
            for (int nn = 0; nn < N; nn++)
            {
                float sum = 0f;
                for (int k = 1; k < K; k++)
                {
                    sum += mags[k - 1] * MathF.Sin(twoPiOverN * k * nn);
                }
                yShort[nn] = 2f * sum / N;
            }

            return ResampleLinear(yShort, N, outLen);
        }

        // make_braids_family with fix=False. No spectral processing —
        // just signed byte conversion and resample.
        static float[] BraidsFamilyNoFix(byte[] waves, int index, int outLen)
        {
            const int N = 128;
            int start = index * 129;
            var yShort = new float[N];
            for (int i = 0; i < N; i++) yShort[i] = waves[start + i] - 128f;
            return ResampleLinear(yShort, N, outLen);
        }

        // Linear-interp resample from srcLen samples to dstLen samples.
        // Wraps cyclically — appropriate for periodic wavetables.
        static float[] ResampleLinear(float[] src, int srcLen, int dstLen)
        {
            var dst = new float[dstLen];
            for (int i = 0; i < dstLen; i++)
            {
                float srcIdx = (float)i * srcLen / dstLen;
                int idx0 = (int)srcIdx;
                int idx1 = (idx0 + 1) % srcLen;
                float frac = srcIdx - idx0;
                dst[i] = src[idx0] + (src[idx1] - src[idx0]) * frac;
            }
            return dst;
        }

        // ─────────────────────────────────────────────────────────
        // BuildBank3 — Plaits' bank_3 (shruthi/ambika/braids-derived).
        // Returns 64 waves at outLen samples each, mirroring the index
        // tables from wavetables.py. Returns null if waves.bin couldn't
        // be loaded — caller falls back to algorithmic generation.
        // ─────────────────────────────────────────────────────────
        public static float[][] BuildBank3(int outLen)
        {
            var waves = LoadBraidsWaves();
            if (waves == null || waves.Length < 256 * 129) return null;

            // Row indices into waves.bin per wavetables.py bank_3 definition.
            int[][] fixedRows = {
                new[] { 0, 2, 4, 6, 8, 10, 12, 14 },                       // Male
                new[] { 32, 34, 36, 38, 40, 42, 44, 46 },                  // Choir
                new[] { 176, 189, 191, 193, 195, 197, 199, 201 },          // Digi
                new[] { 203, 204, 205, 206, 207, 208, 209, 211 },          // Drone
                new[] { 220, 222, 224, 226, 228, 230, 232, 234 },          // Metal
                new[] { 236, 238, 240, 242, 244, 246, 248, 250 },          // Fant
            };
            int[][] passthroughRows = {
                new[] { 172, 173, 174, 175, 176, 177, 178, 179 },          // pass A
                new[] { 180, 181, 182, 183, 184, 185, 186, 187 },          // pass B
            };

            var result = new float[64][];
            int idx = 0;
            foreach (var row in fixedRows)
                foreach (var i in row)
                    result[idx++] = BraidsFamilyFix(waves, i, outLen);
            foreach (var row in passthroughRows)
                foreach (var i in row)
                    result[idx++] = BraidsFamilyNoFix(waves, i, outLen);
            return result;
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
