// Engines/ModalResonatorEngine.cs — Plaits engine 10 (Rings-style modal resonator).
//
// Manual:
//   HARMONICS: structure (inharmonicity / stiffness) — 0 = perfectly
//              harmonic spectrum (string-like), → bell/marimba/glass at
//              higher values. Stiffness coefficient B in
//              f_n = n · f_0 · √(1 + B · n²) sweeps quadratically with the
//              knob so the lower half stays in string-stretch territory
//              and the upper half opens up to clangorous inharmonic
//              spectra.
//   TIMBRE:    brightness — controls per-partial decay rolloff. At 0,
//              high partials decay sharply faster than the fundamental
//              (mellow / damped); at 1, all partials share the same
//              decay (bright, even ring).
//   MORPH:     position / damping — overall T60 at the fundamental,
//              swept exponentially from ~50 ms (percussive pluck) to ~3 s
//              (long sustained ring). Maps morph01 → 60^morph01 × 50 ms.
//   AUX:       sum of even-indexed partials only (k=0, 2, 4, …) =
//              fundamental plus odd-numbered harmonics. Hollow,
//              clarinet-like character vs the OUT bank's full sum.
//
// IsPercussive=true → bypasses the LPG entirely. The modal partial bank
// has its own natural decay envelope per-partial; layering the LPG on
// top would double-envelope and flatten the modal character. The
// global Decay parameter is therefore ignored by this engine; MORPH is
// the decay control.
//
// Architecture: 24 biquad resonators in parallel. Each partial is the
// two-pole bandpass-resonator form
//   y[n] = sin(ω) · x[n] + 2r·cos(ω) · y[n-1] − r² · y[n-2]
// where ω = 2π · f_n / sr and r = exp(−ln(1000) / (T60_n · sr)). The
// sin(ω) input scaling normalises peak amplitude across pitches, and r
// is clamped to 0.9999 so partials with T60 → ∞ don't latch up. Partials
// whose frequency exceeds 0.95 · Nyquist are deactivated to prevent
// aliasing at extreme pitches.
//
// Exciter: 96-sample (~2 ms at 48 kHz) Hann-windowed white noise burst
// fired on NoteOn. Energy is broadband so all partials excite roughly
// equally; the window smooths the burst edges so there's no DC click
// at NoteOn. Subsequent NoteOns rearm the exciter without clearing
// partial state, so a retrigger overlaps natural decay with fresh
// excitation (idiomatic Rings behaviour).
//
// Coefficient update: per-buffer, not per-sample. Each Render computes
// fresh per-partial coefficients from current (HARMONICS, TIMBRE, MORPH)
// — 24 partials × ~4 transcendentals per partial is acceptable at
// buffer rate (~4 kHz with BLOCK_SIZE=12), and accommodates parameter
// smoothing without zipper.

using System;
using PedalPlaits.Util;

namespace PedalPlaits.Engines
{
    public class ModalResonatorEngine : IEngine
    {
        public bool IsPercussive => true;
        public bool IsSilent => _exciterPhase >= EXCITER_LEN && _peakState < SILENCE_THRESHOLD;

        const int   N_PARTIALS       = 24;
        const int   EXCITER_LEN      = 96;       // ~2 ms at 48 kHz
        const float SILENCE_THRESHOLD = 1e-5f;
        const float R_MAX            = 0.9999f;  // cap r so partials don't self-oscillate
        const float NYQUIST_GUARD    = 0.95f;    // skip partials above 0.95·Nyquist

        float _sr     = 44100f;
        float _baseHz = 220f;

        // Per-partial state and coefficients
        readonly float[] _y1      = new float[N_PARTIALS];
        readonly float[] _y2      = new float[N_PARTIALS];
        readonly float[] _twoRcos = new float[N_PARTIALS];
        readonly float[] _rSq     = new float[N_PARTIALS];
        readonly float[] _scale   = new float[N_PARTIALS];
        readonly bool[]  _active  = new bool[N_PARTIALS];

        // Exciter state — Hann-windowed noise burst on NoteOn
        int  _exciterPhase = EXCITER_LEN;  // start "done"
        uint _rngState;
        float _strikeAmp = 1f;

        // Tracks the peak partial state from the last Render so IsSilent
        // doesn't have to scan all 24 partials on every Voice check.
        float _peakState;

        public void Init(float sr)
        {
            _sr = sr;
            // Seed RNG with a fresh System.Random — different per instance/run
            _rngState = (uint)new Random().Next(1, int.MaxValue);
            Reset();
        }

        public void Reset()
        {
            for (int k = 0; k < N_PARTIALS; k++)
            {
                _y1[k] = 0f;
                _y2[k] = 0f;
                _active[k] = false;
            }
            _exciterPhase = EXCITER_LEN;
            _peakState = 0f;
        }

        public void NoteOn(int midiNote, float velocity)
        {
            _baseHz = DspUtil.MidiToHz(midiNote);
            // Rearm the exciter without clearing partial state — overlap
            // any in-progress decay with fresh excitation. Velocity is
            // applied via _strikeAmp during the exciter render.
            _exciterPhase = 0;
            _strikeAmp = velocity <= 0f ? 1f : velocity;
        }

        public void NoteOff() { /* modal decay handles itself */ }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            UpdateCoefficients(p);

            const float OUT_GAIN = 0.25f;   // headroom — summed 24 partials get loud
            float peak = 0f;

            for (int i = 0; i < n; i++)
            {
                // Exciter sample — Hann-windowed white noise, broadband
                // pulse to excite all partials roughly equally.
                float excite = 0f;
                if (_exciterPhase < EXCITER_LEN)
                {
                    float wPhase = (float)_exciterPhase / (EXCITER_LEN - 1);
                    float window = 0.5f * (1f - MathF.Cos(2f * MathF.PI * wPhase));
                    excite = NoiseSample() * window * _strikeAmp;
                    _exciterPhase++;
                }

                // Run all active modal partials in parallel
                float outSum = 0f;
                float auxSum = 0f;
                for (int k = 0; k < N_PARTIALS; k++)
                {
                    if (!_active[k]) continue;
                    float y = _scale[k] * excite
                              + _twoRcos[k] * _y1[k]
                              - _rSq[k]    * _y2[k];
                    _y2[k] = _y1[k];
                    _y1[k] = y;

                    outSum += y;
                    if ((k & 1) == 0) auxSum += y;   // even-indexed partials → AUX

                    // Denormal flush (Core §30) — high partials with long decay
                    // can drift sub-denormal once they're below audibility.
                    if (_y1[k] > -1e-25f && _y1[k] < 1e-25f) _y1[k] = 0f;

                    float absY = MathF.Abs(y);
                    if (absY > peak) peak = absY;
                }

                outBuf[i] += outSum * OUT_GAIN;
                auxBuf[i] += auxSum * OUT_GAIN;
            }

            _peakState = peak;
        }

        // ─────────────────────────────────────────────────────────
        // Coefficient computation
        // ─────────────────────────────────────────────────────────

        void UpdateCoefficients(in EngineParams p)
        {
            float harm  = DspUtil.Clamp01(p.Harmonics);
            float timb  = DspUtil.Clamp01(p.Timbre);
            float morph = DspUtil.Clamp01(p.Morph);

            // HARMONICS → stiffness B. Quadratic so the lower half of the
            // knob stays in string territory (B < ~0.04) and the upper half
            // opens up to clangorous spectra (B → 0.16).
            float B = harm * harm * 0.16f;

            // MORPH → fundamental T60. Exponential 50 ms → 3 s.
            float T60_base = 0.05f * MathF.Pow(60f, morph);

            // TIMBRE → per-partial decay rolloff. roll ∈ [0.3, 1.0]:
            //   roll = 1.0 → every partial decays at T60_base (bright, even)
            //   roll = 0.3 → each partial decays at 30% of the previous (mellow)
            float roll = 0.3f + timb * 0.7f;

            float nyquist = _sr * 0.5f;
            float invSr   = 1f / _sr;
            float twoPi   = 2f * MathF.PI;
            float ln1000  = MathF.Log(1000f);

            for (int k = 0; k < N_PARTIALS; k++)
            {
                int   harmonic_n = k + 1;
                float nf         = harmonic_n;
                // Stiffness-stretched modal frequency
                float fn = nf * _baseHz * MathF.Sqrt(1f + B * nf * nf);

                if (fn >= nyquist * NYQUIST_GUARD)
                {
                    _active[k] = false;
                    _y1[k] = 0f;
                    _y2[k] = 0f;
                    continue;
                }
                _active[k] = true;

                // Per-partial T60 — geometric decay by roll^k
                float T60_k = T60_base * MathF.Pow(roll, k);
                if (T60_k < 0.005f) T60_k = 0.005f;   // 5 ms floor

                float omega = twoPi * fn * invSr;
                float r     = MathF.Exp(-ln1000 * invSr / T60_k);
                if (r > R_MAX) r = R_MAX;

                _twoRcos[k] = 2f * r * MathF.Cos(omega);
                _rSq[k]     = r * r;
                // sin(ω) input scaling normalises peak amplitude per partial;
                // 1/harmonic_n weights upper partials more softly so the
                // fundamental dominates the perceived pitch.
                _scale[k]   = MathF.Sin(omega) / nf;
            }
        }

        // xorshift32 — same idiom as FilteredNoiseEngine. Returns ±1 uniform.
        float NoiseSample()
        {
            uint x = _rngState;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _rngState = x;
            return (x * (2f / uint.MaxValue)) - 1f;
        }
    }
}
