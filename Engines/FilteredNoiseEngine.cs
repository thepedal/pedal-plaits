// Engines/FilteredNoiseEngine.cs — Plaits engine 9 (slot 6 in this port).
//
// Manual:
//   HARMONICS: filter response (LP → BP → HP smooth blend on OUT;
//              band separation on AUX)
//   TIMBRE:    clock frequency (variable-clock noise rate)
//   MORPH:     filter resonance
//   FREQUENCY: filter cutoff (V/Oct tracks the FILTER, not the noise!)
//   OUT:  single multimode SVF, response controlled by HARMONICS
//   AUX:  two band-pass filters in parallel, separation = HARMONICS
//
// What makes this engine musical: at high resonance the filter's
// self-oscillation peak dominates the audible pitch, so you can play
// melodies on it by playing the Note column (which sets cutoff).
//
// Implementation notes:
// - Chamberlin SVF used for low cost and easy LP/BP/HP simultaneous out.
//   Stability bound: f-coefficient capped at 1.8 to avoid blowup at
//   high cutoff. Adequate for the cutoff range we use (note pitches
//   typically 30 Hz .. ~4 kHz).
// - Random number generator is a per-engine xorshift32 — no allocation,
//   reproducible, fine for noise. Seeded from a fresh System.Random
//   at construction so multiple Pedal Plaits instances don't all
//   produce identical noise.

using System;
using PedalPlaits.Util;

namespace PedalPlaits.Engines
{
    public class FilteredNoiseEngine : IEngine
    {
        public bool IsPercussive => false;

        float _sr = 44100f;
        float _baseHz = 440f;       // filter cutoff (note pitch)

        // Variable-clock noise state
        uint _rngState;
        float _heldSample;
        float _clockPhase;

        // Chamberlin SVF state — OUT path
        float _lp, _bp;

        // Two SVFs for AUX (band-pass only — we only read _bp from each)
        float _aux1Lp, _aux1Bp;
        float _aux2Lp, _aux2Bp;

        public void Init(float sr)
        {
            _sr = sr;
            // Seed RNG with a fresh System.Random — different per instance/run
            _rngState = (uint)new Random().Next(1, int.MaxValue);
            Reset();
        }

        public void Reset()
        {
            _lp = _bp = 0f;
            _aux1Lp = _aux1Bp = 0f;
            _aux2Lp = _aux2Bp = 0f;
            _heldSample = 0f;
            _clockPhase = 0f;
        }

        public void NoteOn(int midiNote, float velocity)
        {
            _baseHz = DspUtil.MidiToHz(midiNote);
        }

        public void NoteOff() { /* env handles fade */ }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            // ── TIMBRE: variable clock rate, 20 Hz .. ~40 kHz (exponential) ──
            // Low values: audibly stepped/digital. High values: white noise.
            float clockHz = 20f * MathF.Pow(2000f, p.Timbre);
            float clockDt = clockHz / _sr;

            // ── MORPH: resonance, Q = 0.5 .. 12 ──
            // 12 is short of self-oscillation but produces a strong audible
            // resonant peak; the qComp factor below tames peak gain.
            float q    = 0.5f + p.Morph * 11.5f;
            float damp = 1f / q;
            float qComp = 1f / (1f + p.Morph * 4f);   // attenuates as resonance climbs

            // ── Filter cutoff = note pitch (Plaits' V/Oct → cutoff routing) ──
            // Cap at Nyquist*0.45 for Chamberlin SVF stability.
            float fc = MathF.Min(_baseHz, _sr * 0.45f);
            float f  = MathF.Min(2f * MathF.Sin(MathF.PI * fc / _sr), 1.8f);

            // ── HARMONICS for OUT: LP → BP → HP blend ──
            float h = DspUtil.Clamp01(p.Harmonics);
            float lpW, bpW, hpW;
            if (h < 0.5f) { float t = h * 2f;        lpW = 1f - t; bpW = t;        hpW = 0f; }
            else          { float t = (h - 0.5f) * 2f; lpW = 0f;     bpW = 1f - t; hpW = t;  }

            // ── HARMONICS for AUX: two BPs separated around the cutoff ──
            // Separation ratio 1..3 (1 = both at same freq, 3 = octave-and-a-half spread)
            float sepRatio = 1f + h * 2f;
            float fc1 = MathF.Min(fc * sepRatio, _sr * 0.45f);
            float fc2 = MathF.Max(fc / sepRatio, 10f);
            float f1 = MathF.Min(2f * MathF.Sin(MathF.PI * fc1 / _sr), 1.8f);
            float f2 = MathF.Min(2f * MathF.Sin(MathF.PI * fc2 / _sr), 1.8f);

            const float OUT_GAIN = 0.4f;

            for (int i = 0; i < n; i++)
            {
                // ── Variable-clock noise: S&H of uniform white at clock rate ──
                _clockPhase += clockDt;
                if (_clockPhase >= 1f)
                {
                    _clockPhase -= 1f;
                    if (_clockPhase >= 1f)         // very high clock rates
                        _clockPhase -= MathF.Floor(_clockPhase);
                    _heldSample = NextNoise();
                }
                float x = _heldSample;

                // ── OUT: Chamberlin SVF — LP/BP/HP simultaneous ──
                _lp += f * _bp;
                float hp = x - _lp - damp * _bp;
                _bp += f * hp;
                outBuf[i] += qComp * OUT_GAIN * (lpW * _lp + bpW * _bp + hpW * hp);

                // ── AUX: two BPs in parallel ──
                _aux1Lp += f1 * _aux1Bp;
                float aux1Hp = x - _aux1Lp - damp * _aux1Bp;
                _aux1Bp += f1 * aux1Hp;

                _aux2Lp += f2 * _aux2Bp;
                float aux2Hp = x - _aux2Lp - damp * _aux2Bp;
                _aux2Bp += f2 * aux2Hp;

                auxBuf[i] += qComp * OUT_GAIN * 0.5f * (_aux1Bp + _aux2Bp);
            }
        }

        // xorshift32 — no allocations, ~5 ns per call. Returns ±1 uniform.
        float NextNoise()
        {
            uint s = _rngState;
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            _rngState = s;
            // Map uint to float in [-1, 1)
            return (s / (float)uint.MaxValue) * 2f - 1f;
        }
    }
}
