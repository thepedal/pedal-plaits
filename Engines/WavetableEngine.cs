// Engines/WavetableEngine.cs — Plaits engine 5 (slot 4 in this port).
//
// Manual:
//   HARMONICS: bank selection (4 interpolated banks + 4 non-interpolated)
//   TIMBRE:    row index within bank (8 rows, brightness sweep)
//   MORPH:     column index within bank (8 cols, character sweep)
//   AUX:       low-fi 5-bit quantized output of the same wavetable
//
// Architecture: 8 banks × 8 rows × 8 cols × 256 samples = 131k float
// table values (~512 KB). Stored statically so multiple Pedal Plaits
// instances share the same wavetable RAM (Core §22).
//
// Banks 0-3: interpolated. TIMBRE/MORPH smoothly cross-fade between
//   the four corner cells of the (row, col) grid.
// Banks 4-7: non-interpolated. Stepped behaviour — sweeping TIMBRE
//   or MORPH jumps from cell to cell. Gives a deliberately digital,
//   non-musical character.
//
// Bank archetypes (same set repeated, interpolated vs stepped):
//   Bank 0/4: Harmonic series with varying tilt (row=harmonic count,
//             col=brightness)
//   Bank 1/5: Sine through wavefolder (row=fold amount, col=asymmetry)
//   Bank 2/6: Inharmonic partial sums (row+col seed = ratios picked)
//   Bank 3/7: Single-peak formant (row=peak position, col=bandwidth)
//
// v0.1 generates tables algorithmically rather than shipping a
// resources binary with the real Plaits wavetable data. The result
// is *Plaits-like* in shape and behaviour but not bit-identical.
// Swappable for a Resources.cs binary loader in a future revision.

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
        const int WAVE_LEN = 256;

        // Static — built once, shared across all Pedal Plaits instances.
        // ~512 KB total. Read-only after generation.
        static float[] s_wavetables;
        static readonly object s_genLock = new object();

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
            const float OUT_GAIN = 0.5f;

            for (int i = 0; i < n; i++)
            {
                float sample;

                if (interpolated)
                {
                    // Bilinear interpolation across (row, col) — smooth sweep
                    sample = SampleBilinear(bank, rowF, colF, _phase);
                }
                else
                {
                    // Snap to nearest cell — stepped sweep
                    sample = SampleCell(bank, rowSnap, colSnap, _phase);
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
        // Wavetable sampling
        // ─────────────────────────────────────────────────────────

        static int CellOffset(int bank, int row, int col)
            => ((bank * N_ROWS + row) * N_COLS + col) * WAVE_LEN;

        // Read one wavetable cell with linear interpolation between adjacent
        // samples in the table. phase ∈ [0, 1).
        float SampleCell(int bank, int row, int col, float phase)
        {
            float fIdx = phase * WAVE_LEN;
            int   i0   = (int)fIdx;
            if (i0 >= WAVE_LEN) i0 = WAVE_LEN - 1;
            int   i1   = (i0 + 1) % WAVE_LEN;
            float frac = fIdx - i0;

            int baseIdx = CellOffset(bank, row, col);
            float s0 = s_wavetables[baseIdx + i0];
            float s1 = s_wavetables[baseIdx + i1];
            return s0 + (s1 - s0) * frac;
        }

        // Bilinear interpolation across 4 neighbour cells in the (row, col)
        // grid. Used for banks 0-3 (interpolated mode).
        float SampleBilinear(int bank, float rowF, float colF, float phase)
        {
            int r0 = (int)rowF; if (r0 >= N_ROWS - 1) r0 = N_ROWS - 1;
            int r1 = r0 + 1;    if (r1 >= N_ROWS)     r1 = N_ROWS - 1;
            float rFrac = rowF - r0;

            int c0 = (int)colF; if (c0 >= N_COLS - 1) c0 = N_COLS - 1;
            int c1 = c0 + 1;    if (c1 >= N_COLS)     c1 = N_COLS - 1;
            float cFrac = colF - c0;

            float s00 = SampleCell(bank, r0, c0, phase);
            float s01 = SampleCell(bank, r0, c1, phase);
            float s10 = SampleCell(bank, r1, c0, phase);
            float s11 = SampleCell(bank, r1, c1, phase);

            float sRow0 = s00 + (s01 - s00) * cFrac;
            float sRow1 = s10 + (s11 - s10) * cFrac;
            return sRow0 + (sRow1 - sRow0) * rFrac;
        }

        // ─────────────────────────────────────────────────────────
        // Wavetable generation — run once, statically, at first Init
        // ─────────────────────────────────────────────────────────

        static void GenerateAllWavetables()
        {
            s_wavetables = new float[N_BANKS * N_ROWS * N_COLS * WAVE_LEN];

            for (int bank = 0; bank < N_BANKS; bank++)
            {
                int archetype = bank % 4;
                for (int row = 0; row < N_ROWS; row++)
                {
                    for (int col = 0; col < N_COLS; col++)
                    {
                        GenerateOneCell(bank, archetype, row, col);
                    }
                }
            }
        }

        static void GenerateOneCell(int bank, int archetype, int row, int col)
        {
            int baseIdx = CellOffset(bank, row, col);
            float peak = 0f;

            // First pass — fill with raw samples per archetype
            for (int s = 0; s < WAVE_LEN; s++)
            {
                float phase = (float)s / WAVE_LEN;
                float v;
                switch (archetype)
                {
                    case 0:  v = HarmonicSample(phase, row, col); break;
                    case 1:  v = WavefoldSample(phase, row, col); break;
                    case 2:  v = InharmonicSample(phase, row, col); break;
                    default: v = FormantSample(phase, row, col);   break;
                }
                s_wavetables[baseIdx + s] = v;
                float av = MathF.Abs(v);
                if (av > peak) peak = av;
            }

            // Second pass — normalize to ±1 peak (each cell independently)
            if (peak > 1e-6f)
            {
                float inv = 1f / peak;
                for (int s = 0; s < WAVE_LEN; s++)
                    s_wavetables[baseIdx + s] *= inv;
            }
        }

        // Archetype 0/4 — harmonic series with varying tilt.
        // row → number of partials (1..8)
        // col → spectral tilt (0 = natural 1/p, 1 = flat — all partials equal)
        static float HarmonicSample(float phase, int row, int col)
        {
            int numPartials = 1 + row;
            float tilt = (float)col / (N_COLS - 1);   // 0..1
            float sum = 0f;
            for (int p = 1; p <= numPartials; p++)
            {
                float amp = 1f / p;
                amp = amp * (1f - tilt) + (1f / numPartials) * tilt;  // lerp to flat spectrum
                sum += amp * MathF.Sin(2f * MathF.PI * p * phase);
            }
            return sum;
        }

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

        // Archetype 2/6 — inharmonic partial sum.
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

        // Archetype 3/7 — narrow-band formant peak.
        // row → centre harmonic of the formant (1..8)
        // col → bandwidth (1..8 partials wide around the centre)
        static float FormantSample(float phase, int row, int col)
        {
            int peakH = 1 + row;
            int bandwidth = 1 + col;
            int loP = Math.Max(1, peakH - bandwidth);
            int hiP = peakH + bandwidth;
            float sum = 0f;
            for (int p = loP; p <= hiP; p++)
            {
                float dist = Math.Abs(p - peakH);
                float amp = MathF.Max(0f, 1f - dist / (bandwidth + 1f));
                sum += amp * MathF.Sin(2f * MathF.PI * p * phase);
            }
            return sum;
        }
    }
}
