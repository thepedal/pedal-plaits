// Engines/WavetableEngine.cs — Plaits engine 4 (wavetable).
//
// Manual:
//   HARMONICS: bank selection (4 interpolated banks + 4 non-interpolated)
//   TIMBRE:    row index within bank (8 rows, brightness sweep)
//   MORPH:     column index within bank (8 cols, character sweep)
//   AUX:       low-fi 5-bit quantized output of the same wavetable
//
// Architecture (v1.7): 8 banks × 8 rows × 8 cols × 132 samples =
//   67,584 floats (~264 KB) of static storage. Stored as integrated
//   wavetables — each cell holds the cumulative sum of a 128-sample
//   normalised waveform, plus 4 padding samples (continuation of the
//   cumsum into the next period) for clean cross-wrap interpolation.
//   Single static buffer shared across all Pedal Plaits instances
//   (Core §22).
//
// Bank archetypes:
//   Bank 0/4: Plaits bank_1 (mild additive)
//   Bank 1/5: algorithmic sine wavefolder with asymmetry
//   Bank 2/6: Plaits bank_3 (Braids-derived, from embedded waves.bin)
//             or algorithmic InharmonicSample fallback if waves.bin
//             missing.
//   Bank 3/7: Plaits bank_2 (formantish)
// Banks 0-3 are interpolated across (row, col) for smooth sweeps;
// banks 4-7 snap to nearest cell for stepped digital character.
//
// v1.7 — integrated wavetable playback (Franck-Valimaki K=1, linear
// interp). Storage is cumulative sums; playback computes
//   y = (I(phase + dt) − I(phase)) / (dt × N)
// which equals the average of the original waveform over the playback
// step. The averaging length grows with dt (= freq/sr), naturally
// rolling off high frequencies that would otherwise alias. At low
// pitches the response is essentially identical to direct sample
// playback; at high pitches a 6 dB/oct sinc-style anti-aliasing kicks
// in. Drops Plaits' native 128-sample geometry into place — same
// resolution as the original module rather than the v0.1-v1.6
// 256-sample oversampling, but anti-aliasing more than compensates.

using System;
using PedalPlaits.Util;

namespace PedalPlaits.Engines
{
    public class WavetableEngine : IEngine
    {
        public bool IsPercussive => false;

        const int N_BANKS = 8;
        const int N_ROWS  = 8;
        const int N_COLS  = 8;

        // Wave geometry — matches Plaits' native 128-sample tables, plus 4
        // padding samples per cell so fractional reads at positions
        // p ∈ [N, N+3) are inside the stored range. Padding holds the
        // continuation of the cumsum into the next period.
        const int WAVE_LEN   = 128;
        const int PAD_LEN    = 4;
        const int STORAGE_LEN = WAVE_LEN + PAD_LEN;

        // Static — built once, shared across all Pedal Plaits instances.
        // 8×8×8×132 × 4 bytes ≈ 264 KB. Read-only after generation.
        static float[] s_wavetables;
        static readonly object s_genLock = new object();

        // Set by GenerateAllWavetables: true when Plaits' Braids-derived
        // bank_3 was successfully loaded from the embedded waves.bin
        // resource, false when the engine fell back to algorithmic
        // InharmonicSample for archetype 2. Read by Pedalplaits.cs to
        // print a one-time status line to ReBuzz's debug console.
        public static bool Bank3FromBraids;

        float _sr = 44100f;
        float _baseHz = 220f;
        float _phase;                           // free-running across notes

        public void Init(float sr)
        {
            _sr = sr;
            // Generate wavetable bank once (cross-instance shared).
            if (s_wavetables == null)
            {
                lock (s_genLock)
                {
                    if (s_wavetables == null) GenerateAllWavetables();
                }
            }
            Reset();
        }

        public void Reset() { _phase = 0f; }

        public void NoteOn(int midiNote, float velocity)
        {
            _baseHz = DspUtil.MidiToHz(midiNote);
        }

        public void NoteOff() { }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            // ── HARMONICS: discrete bank selection (no inter-bank crossfade) ──
            int bank = (int)(DspUtil.Clamp01(p.Harmonics) * (N_BANKS - 1));
            bool interpolated = bank < 4;          // banks 0-3 interp, 4-7 stepped

            // ── TIMBRE: row (fractional in interpolated mode) ──
            // ── MORPH:  col (fractional in interpolated mode) ──
            float rowF = DspUtil.Clamp01(p.Timbre) * (N_ROWS - 1);
            float colF = DspUtil.Clamp01(p.Morph)  * (N_COLS - 1);

            int rowSnap = (int)(rowF + 0.5f); if (rowSnap >= N_ROWS) rowSnap = N_ROWS - 1;
            int colSnap = (int)(colF + 0.5f); if (colSnap >= N_COLS) colSnap = N_COLS - 1;

            float dt = _baseHz / _sr;
            // Step size in table-sample units — used by integrated lookup as
            // the differencing window. Floor enforces precision for very low
            // notes (sub-1-table-sample steps would otherwise hit float noise).
            float stepSamples = MathF.Max(1e-4f, dt * WAVE_LEN);
            float invStep = 1f / stepSamples;
            const float OUT_GAIN = 0.5f;

            for (int i = 0; i < n; i++)
            {
                float sample;

                if (interpolated)
                {
                    sample = SampleBilinear(bank, rowF, colF, _phase, stepSamples, invStep);
                }
                else
                {
                    sample = SampleCell(bank, rowSnap, colSnap, _phase, stepSamples, invStep);
                }

                outBuf[i] += sample * OUT_GAIN;

                // AUX: 5-bit quantized (31 levels in [-1, +1])
                float aux = MathF.Round(sample * 15f) / 15f;
                auxBuf[i] += aux * OUT_GAIN;

                _phase += dt;
                if (_phase >= 1f) _phase -= 1f;
            }
        }

        // ─────────────────────────────────────────────────────────
        // Integrated wavetable lookup (Franck-Valimaki K=1, linear interp)
        //
        // Each cell's storage holds the cumulative sum of a normalised
        // 128-sample waveform plus 4 padding samples (continuation of the
        // cumsum). The playback formula
        //   y = (I(p2) − I(p1)) / stepSamples
        // computes the mean of the original waveform over the table-sample
        // interval [p1, p2], which equals direct sample playback at low
        // pitches and naturally low-passes at high pitches.
        //
        // For mean-zero raw waveforms (enforced during generation), the
        // integrated form is periodic over WAVE_LEN, so positions can be
        // wrapped modulo WAVE_LEN without disrupting the differencing.
        // ─────────────────────────────────────────────────────────

        static int CellOffset(int bank, int row, int col)
            => ((bank * N_ROWS + row) * N_COLS + col) * STORAGE_LEN;

        // Linearly interpolate I at fractional table-sample position p.
        // Caller ensures p ∈ [0, WAVE_LEN) — the +1 read uses the
        // padding samples to stay in-bounds without explicit modulo.
        static float LerpI(int baseIdx, float p)
        {
            int p_int = (int)p;
            float frac = p - p_int;
            float a = s_wavetables[baseIdx + p_int];
            float b = s_wavetables[baseIdx + p_int + 1];
            return a + (b - a) * frac;
        }

        // Read one wavetable cell with integrated differencing.
        // phase ∈ [0, 1), stepSamples = dt × WAVE_LEN (table-sample units),
        // invStep = 1 / stepSamples (precomputed for performance).
        static float SampleCell(int bank, int row, int col,
                                 float phase, float stepSamples, float invStep)
        {
            int baseIdx = CellOffset(bank, row, col);

            float p1 = phase * WAVE_LEN;
            float p2 = p1 + stepSamples;
            if (p2 >= WAVE_LEN) p2 -= WAVE_LEN;

            float I1 = LerpI(baseIdx, p1);
            float I2 = LerpI(baseIdx, p2);
            return (I2 - I1) * invStep;
        }

        // Bilinear interpolation across 4 neighbour cells in the (row, col)
        // grid. Used for banks 0-3 (interpolated mode).
        static float SampleBilinear(int bank, float rowF, float colF,
                                     float phase, float stepSamples, float invStep)
        {
            int r0 = (int)rowF; if (r0 >= N_ROWS - 1) r0 = N_ROWS - 1;
            int r1 = r0 + 1;    if (r1 >= N_ROWS)     r1 = N_ROWS - 1;
            float rFrac = rowF - r0;

            int c0 = (int)colF; if (c0 >= N_COLS - 1) c0 = N_COLS - 1;
            int c1 = c0 + 1;    if (c1 >= N_COLS)     c1 = N_COLS - 1;
            float cFrac = colF - c0;

            float s00 = SampleCell(bank, r0, c0, phase, stepSamples, invStep);
            float s01 = SampleCell(bank, r0, c1, phase, stepSamples, invStep);
            float s10 = SampleCell(bank, r1, c0, phase, stepSamples, invStep);
            float s11 = SampleCell(bank, r1, c1, phase, stepSamples, invStep);

            float sRow0 = s00 + (s01 - s00) * cFrac;
            float sRow1 = s10 + (s11 - s10) * cFrac;
            return sRow0 + (sRow1 - sRow0) * rFrac;
        }

        // ─────────────────────────────────────────────────────────
        // Wavetable generation — run once, statically, at first Init
        // ─────────────────────────────────────────────────────────

        static void GenerateAllWavetables()
        {
            s_wavetables = new float[N_BANKS * N_ROWS * N_COLS * STORAGE_LEN];

            //   Archetype 0 (banks 0/4) — Plaits bank_1 (mild additive)
            //   Archetype 1 (banks 1/5) — algorithmic wavefold
            //   Archetype 2 (banks 2/6) — Plaits bank_3 (Braids-derived)
            //                              if waves.bin loadable, else
            //                              algorithmic inharmonic fallback
            //   Archetype 3 (banks 3/7) — Plaits bank_2 (formantish)
            float[][] plaitsBank1 = PlaitsWavetables.BuildBank1(WAVE_LEN);
            float[][] plaitsBank2 = PlaitsWavetables.BuildBank2(WAVE_LEN);
            float[][] plaitsBank3 = PlaitsWavetables.BuildBank3(WAVE_LEN);
            Bank3FromBraids = plaitsBank3 != null;

            // Scratch buffer for the raw waveform of each cell, reused
            // across cells. Heap-allocated once.
            float[] rawBuf = new float[WAVE_LEN];

            for (int bank = 0; bank < N_BANKS; bank++)
            {
                int archetype = bank % 4;
                for (int row = 0; row < N_ROWS; row++)
                {
                    for (int col = 0; col < N_COLS; col++)
                    {
                        // Step 1 — fill rawBuf with this cell's raw waveform
                        if (archetype == 0)
                        {
                            Array.Copy(plaitsBank1[row * N_COLS + col], rawBuf, WAVE_LEN);
                        }
                        else if (archetype == 3)
                        {
                            Array.Copy(plaitsBank2[row * N_COLS + col], rawBuf, WAVE_LEN);
                        }
                        else if (archetype == 2 && plaitsBank3 != null)
                        {
                            Array.Copy(plaitsBank3[row * N_COLS + col], rawBuf, WAVE_LEN);
                        }
                        else
                        {
                            FillAlgorithmicRaw(rawBuf, archetype, row, col);
                        }

                        // Step 2 — normalise (DC remove + peak ±1) so the
                        // integrated form is periodic with bounded magnitude.
                        NormaliseRaw(rawBuf);

                        // Step 3 — integrate into the storage slot, with
                        // 4 padding samples extending past the period.
                        IntegrateIntoStorage(rawBuf, CellOffset(bank, row, col));
                    }
                }
            }
        }

        // Fill `dst` with a raw algorithmic waveform for the given
        // (archetype, row, col). Used for archetype 1 (wavefold) always,
        // and archetype 2 (inharmonic) when bank_3 isn't loaded.
        static void FillAlgorithmicRaw(float[] dst, int archetype, int row, int col)
        {
            for (int s = 0; s < WAVE_LEN; s++)
            {
                float phase = (float)s / WAVE_LEN;
                dst[s] = archetype == 1
                    ? WavefoldSample(phase, row, col)
                    : InharmonicSample(phase, row, col);
            }
        }

        // Remove DC (subtract mean) and normalise peak to ±1. Both steps
        // matter for integrated-wavetable playback: zero-mean keeps the
        // cumsum periodic; unit peak keeps cells balanced in level.
        static void NormaliseRaw(float[] buf)
        {
            float sum = 0f;
            for (int s = 0; s < WAVE_LEN; s++) sum += buf[s];
            float mean = sum / WAVE_LEN;
            float peak = 0f;
            for (int s = 0; s < WAVE_LEN; s++)
            {
                buf[s] -= mean;
                float av = MathF.Abs(buf[s]);
                if (av > peak) peak = av;
            }
            if (peak > 1e-6f)
            {
                float inv = 1f / peak;
                for (int s = 0; s < WAVE_LEN; s++) buf[s] *= inv;
            }
        }

        // Compute cumulative sum of `raw` (length WAVE_LEN) into s_wavetables
        // at baseIdx, extending 4 samples past the period (continuation of
        // the cumsum) for clean cross-wrap interpolation. Final pass DC-
        // centres the integrated values for float-precision hygiene — the
        // differencing in playback is invariant to DC shifts of the integral.
        static void IntegrateIntoStorage(float[] raw, int baseIdx)
        {
            float accum = 0f;
            for (int i = 0; i < WAVE_LEN; i++)
            {
                s_wavetables[baseIdx + i] = accum;
                accum += raw[i];
            }
            // Padding samples — continue the cumsum past the period.
            // For zero-mean raw, accum after the loop is ~0 (within rounding),
            // so padding picks up at I[0]'s value as expected.
            for (int i = 0; i < PAD_LEN; i++)
            {
                s_wavetables[baseIdx + WAVE_LEN + i] = accum;
                accum += raw[i];
            }
            // DC-centre the stored integral
            float sum = 0f;
            for (int i = 0; i < STORAGE_LEN; i++) sum += s_wavetables[baseIdx + i];
            float mean = sum / STORAGE_LEN;
            for (int i = 0; i < STORAGE_LEN; i++) s_wavetables[baseIdx + i] -= mean;
        }

        // ─────────────────────────────────────────────────────────
        // Algorithmic wave samplers — used for archetype 1 (always)
        // and archetype 2 (when bank_3 isn't loaded). Both produce a
        // sample value in roughly [-1, +1] at a given phase ∈ [0, 1).
        // ─────────────────────────────────────────────────────────

        // Archetype 1/5 — sine through wavefolder with asymmetry.
        // row → fold amount (mild..wild)
        // col → DC offset shifting the sine before folding (asymmetry)
        static float WavefoldSample(float phase, int row, int col)
        {
            float foldAmount = 1f + row * 0.7f;
            float asymmetry  = ((col - (N_COLS - 1) * 0.5f) / (N_COLS * 0.5f)) * 0.3f;
            float sine = MathF.Sin(2f * MathF.PI * phase) + asymmetry;
            return MathF.Sin(MathF.PI * sine * foldAmount);
        }

        // Archetype 2/6 — inharmonic partial sum (algorithmic fallback
        // when Plaits bank_3 isn't available).
        // (row, col) deterministically picks ratios from a fixed set so adjacent
        // cells have audibly related but different timbres.
        static float InharmonicSample(float phase, int row, int col)
        {
            // Inharmonic ratios — chosen to avoid integer alignments
            ReadOnlySpan<float> ratios = stackalloc float[]
                { 1.000f, 1.414f, 1.732f, 2.166f, 2.828f, 3.464f, 4.190f, 5.000f };

            int seed = row * N_COLS + col;
            int numPartials = 2 + (seed % 5);   // 2..6 partials
            float sum = 0f;
            for (int p = 0; p < numPartials; p++)
            {
                int idx = (seed + p * 3) % ratios.Length;
                float r = ratios[idx];
                sum += (1f / (p + 1)) * MathF.Sin(2f * MathF.PI * r * phase);
            }
            return sum;
        }
    }
}
