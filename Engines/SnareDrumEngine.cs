// Engines/SnareDrumEngine.cs — Plaits engine 14 (slot 8 in this port).
//
// Manual:
//   HARMONICS: balance of harmonic (tonal) and noisy components
//   TIMBRE:    balance between drum modes (resonator tuning)
//   MORPH:     decay time
//   OUT: tuned components from a couple of bridged-T networks
//          + noise filtered by a BPF (909-flavoured)
//   AUX: pair of FM sine oscillators + noise filtered by an HPF
//          (early FM-drum flavour)
//
// Architecture:
// - Two impulse-excited Chamberlin SVF resonators tuned to model the
//   primary shell modes of a snare. Mode 1 sits at the note's pitch
//   (the "fundamental"). Mode 2 sits at a TIMBRE-controlled ratio,
//   roughly 1×–3× the fundamental (1.83 mid-range, the classic 909
//   ratio).
// - Single noise SVF whose BP tap drives OUT and HP tap drives AUX —
//   one filter, two taps, both tracked independently.
// - Two envelopes: _ampEnv for tonal (shorter), _noiseEnv for noise
//   (longer, the "wire buzz" of a snare lasts past the body hit).
// - AUX FM path: sine carrier at note pitch, sine modulator at 1.5×,
//   modulation index decays with _ampEnv.
//
// Per §30.4 — every SVF state variable and every decaying envelope
// gets a 1e-25f flush after each update to avoid microcode-trap
// CPU spikes when the resonator decays into denormal range. This is
// the same pattern that caught bass drum at higher notes.

using System;
using PedalPlaits.Util;

namespace PedalPlaits.Engines
{
    public class SnareDrumEngine : IEngine
    {
        public bool IsPercussive => true;
        public bool IsSilent => _ampEnv < 1e-5f && _noiseEnv < 1e-5f;

        float _sr = 44100f;
        float _baseHz = 180f;   // typical snare fundamental — overridden by NoteOn

        // Two SVF resonators (shell modes)
        float _res1Lp, _res1Bp;
        float _res2Lp, _res2Bp;

        // Single noise SVF — BP tap for OUT, HP tap for AUX
        float _noiseLp, _noiseBp;

        // Envelopes
        float _ampEnv;       // tonal — body hit
        float _noiseEnv;     // wire buzz — decays slower
        float _ampCoef;
        float _noiseCoef;

        // FM oscillators for AUX
        float _phaseCar, _phaseMod;

        // Trigger
        bool _triggerPending;
        int  _impulseCounter;

        // RNG
        uint _rngState;

        public void Init(float sr)
        {
            _sr = sr;
            _rngState = (uint)new Random().Next(1, int.MaxValue);
            Reset();
        }

        public void Reset()
        {
            _res1Lp = _res1Bp = 0f;
            _res2Lp = _res2Bp = 0f;
            _noiseLp = _noiseBp = 0f;
            _phaseCar = _phaseMod = 0f;
            _ampEnv = _noiseEnv = 0f;
            _triggerPending = false;
            _impulseCounter = 0;
        }

        public void NoteOn(int midiNote, float velocity)
        {
            _baseHz = DspUtil.MidiToHz(midiNote);
            float v = velocity > 0f ? velocity : 1f;
            _ampEnv = v;
            _noiseEnv = v;
            _phaseCar = _phaseMod = 0f;     // reset FM phases for consistent click per hit
            _triggerPending = true;
        }

        public void NoteOff() { }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            // ── HARMONICS: tonal / noise balance ──
            // 0 = noise-only (snare-rim style), 1 = tonal-only (more like a tom)
            float harmonics = DspUtil.Clamp01(p.Harmonics);
            float tonalMix  = harmonics;
            float noiseMix  = 1f - harmonics;

            // ── TIMBRE: second mode ratio, 1.0 .. 3.0 ──
            // 1.83 is the classic 909 inharmonic ratio; the wider range gives
            // dial-in flexibility from harmonic (1.0, near-unison) to spread.
            float timbre = DspUtil.Clamp01(p.Timbre);
            float mode2Ratio = 1f + timbre * 2f;

            // ── MORPH: decay ──
            float morph = DspUtil.Clamp01(p.Morph);
            float ampDecaySec   = 0.05f + morph * 0.40f;   // 50..450 ms body
            float noiseDecaySec = 0.10f + morph * 0.60f;   // 100..700 ms wire buzz
            _ampCoef   = MathF.Exp(-1f / (ampDecaySec   * _sr));
            _noiseCoef = MathF.Exp(-1f / (noiseDecaySec * _sr));

            // ── Resonator coefficients (per-block, control-rate) ──
            float fc1 = MathF.Min(_baseHz,              _sr * 0.45f);
            float fc2 = MathF.Min(_baseHz * mode2Ratio, _sr * 0.45f);
            float f1  = MathF.Min(2f * MathF.Sin(MathF.PI * fc1 / _sr), 1.8f);
            float f2  = MathF.Min(2f * MathF.Sin(MathF.PI * fc2 / _sr), 1.8f);
            const float resDamp = 1f / 30f;        // Q=30 — sharp shell resonance

            // ── Tonal output gain: 1/f1 ──
            // The Chamberlin SVF's impulse-response peak amplitude is approximately
            // f (= 2·sin(π·fc/sr)), not f·Q as I'd initially assumed. At low notes
            // f is tiny (~0.009 at C-2, ~0.018 at C-3), so without compensation the
            // tonal body is ~10× quieter than the continuously-excited noise BP
            // output (peak RMS ~0.2 at the fixed 5 kHz buzz cutoff). The user
            // perceives "note selection doesn't change the sound" because the
            // dominant component (noise) doesn't track pitch. Normalizing by 1/f1
            // brings the tonal peak to ~1.0 at any note, restoring pitch
            // audibility across the whole keyboard.
            float tonalGain = 1f / MathF.Max(f1, 1e-6f);

            // ── Noise filter: 5 kHz "snare buzz" centre ──
            float noiseFc = MathF.Min(5000f, _sr * 0.45f);
            float fNoise  = MathF.Min(2f * MathF.Sin(MathF.PI * noiseFc / _sr), 1.8f);
            const float noiseDamp = 0.5f;          // Q=2 — wide noise band

            // ── Impulse trigger on first sample of this block ──
            if (_triggerPending)
            {
                _impulseCounter = 1;
                _triggerPending = false;
            }

            const float TWO_PI = 2f * MathF.PI;

            for (int i = 0; i < n; i++)
            {
                float impulse = 0f;
                if (_impulseCounter > 0) { impulse = 1f; _impulseCounter--; }

                // ── Resonator 1 (fundamental mode) ──
                _res1Lp += f1 * _res1Bp;
                float hp1 = impulse - _res1Lp - resDamp * _res1Bp;
                _res1Bp += f1 * hp1;
                if (_res1Bp > -1e-25f && _res1Bp < 1e-25f) _res1Bp = 0f;
                if (_res1Lp > -1e-25f && _res1Lp < 1e-25f) _res1Lp = 0f;

                // ── Resonator 2 (second mode) ──
                _res2Lp += f2 * _res2Bp;
                float hp2 = impulse - _res2Lp - resDamp * _res2Bp;
                _res2Bp += f2 * hp2;
                if (_res2Bp > -1e-25f && _res2Bp < 1e-25f) _res2Bp = 0f;
                if (_res2Lp > -1e-25f && _res2Lp < 1e-25f) _res2Lp = 0f;

                // Tonal sum (mode 2 slightly quieter — adds inharmonic spice but
                // doesn't dominate). Apply normalization gain and tonal envelope.
                float tonal = (_res1Bp + 0.6f * _res2Bp) * tonalGain * _ampEnv;

                // ── Noise SVF — single filter, two simultaneous taps ──
                float noise = NextNoise();
                _noiseLp += fNoise * _noiseBp;
                float noiseHp = noise - _noiseLp - noiseDamp * _noiseBp;
                _noiseBp += fNoise * noiseHp;
                if (_noiseBp > -1e-25f && _noiseBp < 1e-25f) _noiseBp = 0f;
                if (_noiseLp > -1e-25f && _noiseLp < 1e-25f) _noiseLp = 0f;

                float noiseBpOut = _noiseBp * _noiseEnv;    // for OUT (BP-filtered)
                float noiseHpOut = noiseHp  * _noiseEnv;    // for AUX (HP-filtered)

                // ── OUT: tonal mix + BP-filtered noise ──
                // Scale 0.3 (lower than the typical 0.6) because the tonal path
                // now peaks at ~1.0 instead of ~0.02 — without this, the new
                // tonal level would clip at high notes where peak rises further.
                outBuf[i] += (tonalMix * tonal + noiseMix * noiseBpOut) * 0.3f;

                // ── AUX: FM sine pair + HP-filtered noise ──
                float dtCar = _baseHz       / _sr;
                float dtMod = _baseHz * 1.5f / _sr;
                float modSig   = MathF.Sin(TWO_PI * _phaseMod);
                float modIndex = 4f * _ampEnv;
                float fmSig    = MathF.Sin(TWO_PI * _phaseCar + modIndex * modSig);
                float fmTonal  = fmSig * _ampEnv;

                auxBuf[i] += (tonalMix * fmTonal + noiseMix * noiseHpOut) * 0.5f;

                _phaseCar += dtCar; if (_phaseCar >= 1f) _phaseCar -= 1f;
                _phaseMod += dtMod; if (_phaseMod >= 1f) _phaseMod -= 1f;

                // Advance envelopes with denormal flush
                _ampEnv   *= _ampCoef;
                _noiseEnv *= _noiseCoef;
                if (_ampEnv   < 1e-25f) _ampEnv   = 0f;
                if (_noiseEnv < 1e-25f) _noiseEnv = 0f;
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
