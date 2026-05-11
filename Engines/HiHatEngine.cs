// Engines/HiHatEngine.cs — Plaits engine 15 (slot 9 in this port).
//
// Manual:
//   HARMONICS: balance of metallic vs filtered-noise components
//   TIMBRE:    HPF cutoff frequency
//   MORPH:     decay time
//   OUT: 6 square oscillators + clocked noise → HPF → dirty VCA (808-style)
//   AUX: 3 ring-modulated square pairs       → HPF → clean VCA (708-style)
//
// Architecture:
// - OUT path: 6 squares at inharmonic ratios summed, mixed with white
//   noise, fed through HPF (TIMBRE), soft-clipped by tanh ("dirty VCA").
// - AUX path: 3 ring-mod pairs (each pair = two of the same 6 squares
//   multiplied together — same oscillators, different combination),
//   fed through HPF, no soft-clip ("clean VCA").
// - Single amplitude envelope drives both paths.
//
// Inharmonic ratios chosen to produce the dense, metallic spectrum
// that's characteristic of analog hi-hats. None are simple integer
// ratios — none of the squares' harmonics align — which is what
// keeps the sound "noisy" rather than "pitched".
//
// Per §30.4 — HP filter state and amplitude envelope get denormal
// flushes. Square phase accumulators are bounded [0, 1] and don't
// need protection. Squares are intentionally naive (no PolyBLEP):
// the HPF removes everything below the cutoff, and the high-frequency
// aliasing artefacts above the cutoff actually contribute to the
// metallic character.

using System;
using PedalPlaits.Util;

namespace PedalPlaits.Engines
{
    public class HiHatEngine : IEngine
    {
        public bool IsPercussive => true;
        public bool IsSilent => _ampEnv < 1e-5f;

        const int N_OSC = 6;

        // Inharmonic ratios for the 6 squares — 808-derived, tuned for
        // dense spectrum with no harmonic alignment between oscillators.
        static readonly float[] RATIOS = { 1.000f, 1.342f, 1.682f, 2.058f, 2.473f, 2.928f };

        float _sr = 44100f;
        float _baseHz = 540f;       // typical 808 hi-hat base — overridden by NoteOn

        // Per-oscillator phase accumulators
        readonly float[] _phasesOut = new float[N_OSC];
        readonly float[] _phasesAux = new float[N_OSC];

        // Two HP filter states (separate for OUT and AUX paths)
        float _hp1Lp, _hp1Bp;
        float _hp2Lp, _hp2Bp;

        // Envelope
        float _ampEnv;
        float _ampCoef;

        // RNG (for noise generation in OUT path)
        uint _rngState;

        public void Init(float sr)
        {
            _sr = sr;
            _rngState = (uint)new Random().Next(1, int.MaxValue);
            Reset();
        }

        public void Reset()
        {
            for (int o = 0; o < N_OSC; o++) { _phasesOut[o] = 0f; _phasesAux[o] = 0f; }
            _hp1Lp = _hp1Bp = 0f;
            _hp2Lp = _hp2Bp = 0f;
            _ampEnv = 0f;
        }

        public void NoteOn(int midiNote, float velocity)
        {
            _baseHz = DspUtil.MidiToHz(midiNote);
            _ampEnv = velocity > 0f ? velocity : 1f;
            // Free-running oscillator phases — like a real 808's continuously
            // running square oscs. Gives subtle hit-to-hit variation.
        }

        public void NoteOff() { }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            // ── HARMONICS: metallic vs noise balance ──
            // 1 = pure metallic (all 6 squares), 0 = pure noise
            float h = DspUtil.Clamp01(p.Harmonics);
            float metalMix = h;
            float noiseMix = 1f - h;

            // ── TIMBRE: HPF cutoff, exp 1 kHz .. 12 kHz ──
            float t = DspUtil.Clamp01(p.Timbre);
            float hpCutoff = 1000f * MathF.Pow(12f, t);
            if (hpCutoff > _sr * 0.45f) hpCutoff = _sr * 0.45f;
            float fHp = MathF.Min(2f * MathF.Sin(MathF.PI * hpCutoff / _sr), 1.8f);
            const float hpDamp = 0.5f;     // Q=2

            // ── MORPH: decay 30 ms .. 500 ms ──
            float m = DspUtil.Clamp01(p.Morph);
            float decaySec = 0.03f + m * 0.47f;
            _ampCoef = MathF.Exp(-1f / (decaySec * _sr));

            // ── Per-oscillator dt ──
            // Computed once per block (control rate). Static for the block.
            float invSr = 1f / _sr;

            for (int i = 0; i < n; i++)
            {
                // ── 6 squares for OUT (summed) ──
                float metalSum = 0f;
                for (int o = 0; o < N_OSC; o++)
                {
                    float dt = _baseHz * RATIOS[o] * invSr;
                    metalSum += _phasesOut[o] < 0.5f ? 1f : -1f;
                    _phasesOut[o] += dt;
                    if (_phasesOut[o] >= 1f) _phasesOut[o] -= 1f;
                }
                metalSum *= (1f / N_OSC);

                // ── Noise (filtered through OUT HP only — adds the "fizz") ──
                float noise = NextNoise();

                // ── OUT: HP-filter the sum, then dirty VCA (tanh) ──
                float outPre = metalMix * metalSum + noiseMix * noise;
                _hp1Lp += fHp * _hp1Bp;
                float hpOut = outPre - _hp1Lp - hpDamp * _hp1Bp;
                _hp1Bp += fHp * hpOut;
                if (_hp1Bp > -1e-25f && _hp1Bp < 1e-25f) _hp1Bp = 0f;
                if (_hp1Lp > -1e-25f && _hp1Lp < 1e-25f) _hp1Lp = 0f;

                float dirty = MathF.Tanh(hpOut * 2f);
                outBuf[i] += dirty * _ampEnv * 0.5f;

                // ── 3 ring-mod pairs for AUX ──
                // Pairs: (0,1), (2,3), (4,5). Each pair's product is ring-mod.
                float ringSum = 0f;
                for (int o = 0; o < N_OSC; o += 2)
                {
                    float dt1 = _baseHz * RATIOS[o]     * invSr;
                    float dt2 = _baseHz * RATIOS[o + 1] * invSr;
                    float sq1 = _phasesAux[o]     < 0.5f ? 1f : -1f;
                    float sq2 = _phasesAux[o + 1] < 0.5f ? 1f : -1f;
                    ringSum += sq1 * sq2;
                    _phasesAux[o]     += dt1; if (_phasesAux[o]     >= 1f) _phasesAux[o]     -= 1f;
                    _phasesAux[o + 1] += dt2; if (_phasesAux[o + 1] >= 1f) _phasesAux[o + 1] -= 1f;
                }
                ringSum *= (1f / 3f);

                // ── AUX: HP-filter the ring-mod sum, then clean VCA (no clip) ──
                _hp2Lp += fHp * _hp2Bp;
                float hpAux = ringSum - _hp2Lp - hpDamp * _hp2Bp;
                _hp2Bp += fHp * hpAux;
                if (_hp2Bp > -1e-25f && _hp2Bp < 1e-25f) _hp2Bp = 0f;
                if (_hp2Lp > -1e-25f && _hp2Lp < 1e-25f) _hp2Lp = 0f;

                auxBuf[i] += hpAux * _ampEnv * 0.5f;

                // Advance envelope (denormal flush)
                _ampEnv *= _ampCoef;
                if (_ampEnv < 1e-25f) _ampEnv = 0f;
            }
        }

        float NextNoise()
        {
            uint s = _rngState;
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            _rngState = s;
            return (s / (float)uint.MaxValue) * 2f - 1f;
        }
    }
}
