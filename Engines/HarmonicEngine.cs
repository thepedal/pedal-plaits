// Engines/HarmonicEngine.cs — Plaits engine 4 (slot 3 in this port).
//
// Manual:
//   HARMONICS: number of bumps in the spectrum (one big bump → multiple)
//   TIMBRE:    index of the most prominent harmonic (BPF cutoff feel)
//   MORPH:     bump shape — flat/wide → peaked/narrow (BPF resonance feel)
//   AUX:       drawbar subset — only partials 1, 2, 3, 4, 6, 8, 10, 12
//                (Hammond organ drawbar ratios)
//
// Architecture: bank of N_PARTIALS pure sines at integer multiples of
// the fundamental, each scaled by a spectral envelope that's recomputed
// at control rate (once per BLOCK).
//
// Spectral envelope: sum of N_BUMPS Gaussians at multiples of a center
// position. TIMBRE sets the center, MORPH sets each bump's width,
// HARMONICS sets the number of bumps. Bumps at higher multiples are
// attenuated by BUMP_DECAY so the dominant character stays anchored to
// TIMBRE while extra bumps add timbral "ripple" without overwhelming.
//
// CPU note: N_PARTIALS × n sins per block dominate the per-sample cost.
// At 24 partials × 12 samples = 288 sins per block, ~1 µs at modern
// FPU speed. Could swap MathF.Sin for a Taylor or LUT approximation if
// CPU ever became a concern — not needed at v0.1 scale.

using System;
using PedalPlaits.Util;

namespace PedalPlaits.Engines
{
    public class HarmonicEngine : IEngine
    {
        public bool IsPercussive => false;

        const int N_PARTIALS = 24;     // partials 1..N_PARTIALS
        const float BUMP_DECAY = 0.7f; // each successive bump's gain factor

        // Drawbar partial indices (0-based) — harmonics 1, 2, 3, 4, 6, 8, 10, 12.
        static readonly int[] DRAWBAR_INDICES = { 0, 1, 2, 3, 5, 7, 9, 11 };

        float _sr = 44100f;
        float _baseHz = 220f;

        // Free-running per-partial phase (SH101 §7 convention)
        readonly float[] _phases = new float[N_PARTIALS];

        // Per-partial increment, cached at control rate
        readonly float[] _dt = new float[N_PARTIALS];

        // Per-partial amplitude, recomputed at control rate
        readonly float[] _amps    = new float[N_PARTIALS];
        readonly float[] _ampsAux = new float[N_PARTIALS];

        // Active partial count (set at control rate — skip partials above Nyquist)
        int _activePartials;

        public void Init(float sr) { _sr = sr; Reset(); }

        public void Reset()
        {
            for (int i = 0; i < N_PARTIALS; i++) _phases[i] = 0f;
        }

        public void NoteOn(int midiNote, float velocity)
        {
            _baseHz = DspUtil.MidiToHz(midiNote);
        }

        public void NoteOff() { /* env handles fade */ }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            // ── Control-rate setup (once per Render call) ──
            float nyquist = _sr * 0.45f;
            _activePartials = 0;
            for (int k = 0; k < N_PARTIALS; k++)
            {
                float partialHz = _baseHz * (k + 1);
                if (partialHz > nyquist) break;
                _dt[k] = partialHz / _sr;
                _activePartials = k + 1;
            }

            ComputeAmplitudes(p);

            const float OUT_GAIN = 0.5f;
            const float TWO_PI = 2f * MathF.PI;

            // ── Per-sample additive synthesis ──
            for (int i = 0; i < n; i++)
            {
                float sumOut = 0f;
                float sumAux = 0f;

                for (int k = 0; k < _activePartials; k++)
                {
                    float s = MathF.Sin(TWO_PI * _phases[k]);
                    sumOut += s * _amps[k];
                    sumAux += s * _ampsAux[k];

                    _phases[k] += _dt[k];
                    if (_phases[k] >= 1f) _phases[k] -= 1f;
                }

                outBuf[i] += sumOut * OUT_GAIN;
                auxBuf[i] += sumAux * OUT_GAIN;
            }
        }

        // Compute per-partial amplitudes from HARMONICS / TIMBRE / MORPH.
        // Smooth across all three knobs — no integer steps in the bump count
        // (fractional last bump faded in via a weight) so sweeps are zipper-free.
        void ComputeAmplitudes(in EngineParams p)
        {
            float timbre    = DspUtil.Clamp01(p.Timbre);
            float morph     = DspUtil.Clamp01(p.Morph);
            float harmonics = DspUtil.Clamp01(p.Harmonics);

            // TIMBRE → center partial index, 0..N_PARTIALS-1
            float center = timbre * (N_PARTIALS - 1);

            // MORPH → bandwidth (standard deviation) of each Gaussian bump.
            // 1 partial wide (peaked/narrow at MORPH=1) → 16 (flat/wide at 0).
            float bandwidth = 1f + MathF.Pow(2f, (1f - morph) * 4f);

            // HARMONICS → number of bumps, smooth from 1 to 5.
            float numBumpsF = 1f + harmonics * 4f;
            int   numBumpsI = (int)numBumpsF;
            float lastWeight = numBumpsF - numBumpsI;   // fractional fade-in

            for (int k = 0; k < N_PARTIALS; k++) _amps[k] = 0f;

            float bumpGain = 1f;
            for (int b = 0; b <= numBumpsI; b++)
            {
                float weight = (b == numBumpsI) ? lastWeight : 1f;
                if (weight < 1e-6f) break;

                // Bump centers at multiples of (center+1) so HARMONICS adds
                // bumps further up the spectrum, not stacked on the same partial.
                float bumpCenter = (b + 1) * (center + 1f) - 1f;

                for (int k = 0; k < N_PARTIALS; k++)
                {
                    float dist = (k - bumpCenter) / bandwidth;
                    _amps[k] += bumpGain * weight * MathF.Exp(-0.5f * dist * dist);
                }
                bumpGain *= BUMP_DECAY;
            }

            // Peak-normalize OUT amplitudes so loudness stays roughly constant
            // across the knob sweeps.
            float maxA = 0f;
            for (int k = 0; k < N_PARTIALS; k++)
                if (_amps[k] > maxA) maxA = _amps[k];
            if (maxA > 1e-6f)
            {
                float inv = 1f / maxA;
                for (int k = 0; k < N_PARTIALS; k++) _amps[k] *= inv;
            }

            // AUX: same envelope but zero out non-drawbar partials.
            // Deliberately NOT re-normalizing — if the bell lands between
            // drawbar slots the AUX gets quieter, if it lands on one it gets
            // louder. That's the musical character of drawbar synthesis.
            for (int k = 0; k < N_PARTIALS; k++) _ampsAux[k] = 0f;
            foreach (int idx in DRAWBAR_INDICES)
                if (idx < N_PARTIALS)
                    _ampsAux[idx] = _amps[idx];
        }
    }
}
