// Engines/PhaseDistortionEngine.cs — Plaits engine 12 (phase distortion and modulation).
//
// Manual:
//   HARMONICS: distortion frequency — modulator/carrier ratio swept
//              smoothly 0.5× to 8× (quadratic mapping for finer control
//              in the musical 1–3× range, opens up to clangorous
//              inharmonic ratios at the top).
//   TIMBRE:    distortion amount — modulator depth in radians, 0 to 2.
//              Soft-clipped per-buffer (analytical sideband bound, same
//              technique as the FM engine in v1.8) so peak sidebands
//              stay below 90 % Nyquist; behaviour unchanged at low
//              pitches, smoothly limited at high pitches.
//   MORPH:     distortion asymmetry — break point of a piecewise-linear
//              phase mapping applied to the carrier before the sine
//              lookup. 0.5 = symmetric (no distortion, smooth sine),
//              extremes = strongly skewed phase advance (CZ-style saw
//              / reverse-saw character). Mapped to [0.1, 0.9] so the
//              extremes still produce a meaningful waveform rather
//              than collapsing to silence.
//   OUT:       carrier is hard-sync'd to the modulator — modulator
//              phase = ratio · carrier_phase mod 1, so the sidebands
//              land on exact integer multiples of the carrier. This is
//              the "phase distortion" half: strict harmonic skirts,
//              classic Casio CZ flavour.
//   AUX:       carrier and modulator both free-running — sin(2π·φ_c +
//              depth · sin(2π·φ_m)). This is the "modulation" half:
//              smooth FM-like sidebands that can be inharmonic when
//              the ratio is non-integer.
//
// Architecture (v1.12): two phase accumulators, one for the carrier
// and one for the free-running modulator. The synced modulator
// reuses the carrier phase × ratio (mod 1) so no separate accumulator
// is needed — the hard-sync is implicit in the modulo. The carrier
// phase passes through an asymmetric piecewise-linear distortion
// function (the CZ "break point" mapping) before the sine lookup;
// the modulator then phase-modulates the distorted carrier.
//
// Cost: four MathF.Sin calls per sample (two carrier outputs, two
// modulator samples). At BLOCK_SIZE=12 and sr=48 kHz that's roughly
// 190 k transcendentals per second per instance — comparable to FM,
// well under 1 % of one core.

using System;
using PedalPlaits.Util;

namespace PedalPlaits.Engines
{
    public class PhaseDistortionEngine : IEngine
    {
        public bool IsPercussive => false;

        const float TWO_PI = 2f * MathF.PI;

        float _sr     = 44100f;
        float _baseHz = 220f;

        // Phase accumulators, normalised 0..1.
        // Free-running across notes (no NoteOn reset) for analog-style continuity.
        float _phaseC;        // carrier
        float _phaseM;        // modulator (free-running, used for AUX)

        public void Init(float sr) { _sr = sr; Reset(); }

        public void Reset()
        {
            _phaseC = 0f;
            _phaseM = 0f;
        }

        public void NoteOn(int midiNote, float velocity)
        {
            _baseHz = DspUtil.MidiToHz(midiNote);
            // Don't reset phases — analog-style continuity across notes.
        }

        public void NoteOff() { /* envelope handles fade */ }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            float harm  = DspUtil.Clamp01(p.Harmonics);
            float timb  = DspUtil.Clamp01(p.Timbre);
            float morph = DspUtil.Clamp01(p.Morph);

            // ── HARMONICS → modulator ratio (smooth, quadratic) ──
            // 0.5× sub-octave at the bottom, 8× clangorous bell at the top.
            // Quadratic mapping concentrates resolution in the musical
            // 1–3× range where most phase-distortion patches live.
            float ratio = 0.5f + harm * harm * 7.5f;

            // ── TIMBRE → modulator depth (radians) ──
            // 0..2 radians is enough range for from "barely audible PD
            // character" to "full CZ-saw bite". Anything higher just
            // produces unmusical alias-mess.
            float depth = timb * 2f;

            // Sideband soft-clip (same analytical bound as the FM engine):
            // peak sideband ≈ f · (1 + I·r), so I_max = (0.45·sr/f - 1) / r.
            // tanh-soft-clip toward the bound keeps low-pitch behaviour
            // unchanged and bounds high-pitch aliasing smoothly. Floor of
            // 0.3 keeps a bit of PD character alive at extreme high notes.
            float safeNyquist = _sr * 0.45f;
            float maxSafeDepth = MathF.Max(0.3f, (safeNyquist / _baseHz - 1f) / ratio);
            depth = maxSafeDepth * MathF.Tanh(depth / maxSafeDepth);

            // ── MORPH → break point of the piecewise phase mapping ──
            // 0.5 = identity (no distortion). Mapping into [0.1, 0.9] so
            // even at the knob extremes the second segment still has
            // enough range to advance the phase meaningfully.
            float skew = 0.1f + morph * 0.8f;
            float inv_skew    = 0.5f / skew;          // precomputed first-segment slope
            float inv_1mskew  = 0.5f / (1f - skew);   // precomputed second-segment slope

            float dt_c = _baseHz         / _sr;
            float dt_m = _baseHz * ratio / _sr;

            const float OUT_GAIN = 0.5f;

            for (int i = 0; i < n; i++)
            {
                float phaseC = _phaseC;

                // ── Asymmetric phase distortion ── piecewise-linear remap
                // of the carrier phase before the sine lookup. CZ-style.
                float distorted = (phaseC < skew)
                    ?              phaseC          * inv_skew
                    : 0.5f + (phaseC - skew)       * inv_1mskew;

                // ── Synced modulator ── phase = ratio · carrier_phase mod 1
                // (the hard-sync is implicit; no separate accumulator).
                // Sidebands land on exact integer multiples of the carrier.
                float syncPhase = ratio * phaseC;
                syncPhase -= MathF.Floor(syncPhase);
                float syncMod = MathF.Sin(TWO_PI * syncPhase);

                // ── Free-running modulator ── runs at carrier × ratio
                // independently of the carrier wrap, so sidebands can be
                // inharmonic when the ratio isn't an integer.
                float freeMod = MathF.Sin(TWO_PI * _phaseM);

                // ── Outputs ── distorted carrier phase, phase-modulated
                // by the corresponding modulator.
                float outSample = MathF.Sin(TWO_PI * distorted + depth * syncMod);
                float auxSample = MathF.Sin(TWO_PI * distorted + depth * freeMod);

                outBuf[i] += outSample * OUT_GAIN;
                auxBuf[i] += auxSample * OUT_GAIN;

                _phaseC += dt_c; if (_phaseC >= 1f) _phaseC -= 1f;
                _phaseM += dt_m; if (_phaseM >= 1f) _phaseM -= 1f;
            }
        }
    }
}
