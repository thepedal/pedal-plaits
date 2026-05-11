// Engines/BassDrumEngine.cs — Plaits engine 13 (slot 7 in this port).
//
// Manual:
//   HARMONICS: attack sharpness + amount of overdrive
//   TIMBRE:    brightness (initial pitch sweep top, filter Q)
//   MORPH:     decay time
//   OUT: bridged-T network excited by a shaped pulse (808-flavoured)
//   AUX: FM triangle VCO turned to sine via diodes, dirty VCA
//        (909-flavoured FM kick)
//
// Architecture:
// - Pitch envelope: short exponential decay, sweeps current freq from
//   pitchTop down to baseHz (the "thump" character).
// - Amp envelope: longer exponential decay, controls overall loudness.
// - OUT path: a high-Q Chamberlin SVF (the "bridged-T" stand-in) is
//   excited by a short impulse at NoteOn. The filter rings at the
//   current pitch. Output is the bandpass tap, soft-clipped through
//   tanh with HARMONICS-driven overdrive.
// - AUX path: classic FM kick — sine carrier at pitch, sine modulator
//   at 2× pitch, modulation index tracks amplitude env so the click
//   character is loudest at attack. tanh "diode" stage softens the
//   FM-rich attack into a more sine-like sustain.
//
// IsPercussive = true → Voice bypasses the LPG entirely. The engine
// owns its envelope and reports IsSilent when _ampEnv has decayed
// below threshold so Voice can clear IsActive.
//
// On NoteOn:
// - Trigger pending → impulse fires on the first sample of next Render
// - Phases reset to 0 for consistent FM character per kick
// - Both envelopes reset to full

using System;
using PedalPlaits.Util;

namespace PedalPlaits.Engines
{
    public class BassDrumEngine : IEngine
    {
        public bool IsPercussive => true;
        public bool IsSilent => _ampEnv < 1e-5f;

        float _sr = 44100f;
        float _baseHz = 60f;

        // Envelopes (exponential decay coefficients set per-block from MORPH)
        float _ampEnv;
        float _ampCoef;
        float _pitchEnv;
        float _pitchCoef;

        // Trigger / impulse
        bool _triggerPending;
        int  _impulseCounter;

        // Bridged-T resonator state (Chamberlin SVF integrators)
        float _resLp, _resBp;

        // FM oscillator state
        float _phaseCar, _phaseMod;

        public void Init(float sr) { _sr = sr; Reset(); }

        public void Reset()
        {
            _ampEnv   = 0f;
            _pitchEnv = 0f;
            _ampCoef  = 0.999f;
            _pitchCoef = 0.99f;
            _resLp = _resBp = 0f;
            _phaseCar = _phaseMod = 0f;
            _triggerPending = false;
            _impulseCounter = 0;
        }

        public void NoteOn(int midiNote, float velocity)
        {
            // Use the note's pitch as the kick fundamental. No clamping —
            // Plaits hardware does the same, letting high notes produce
            // "tom" or "synth-drum" character.
            _baseHz = DspUtil.MidiToHz(midiNote);

            _ampEnv   = velocity > 0f ? velocity : 1f;
            _pitchEnv = 1f;             // start at top of pitch sweep
            _phaseCar = 0f;
            _phaseMod = 0f;
            _triggerPending = true;
        }

        public void NoteOff() { /* fire-and-forget; env handles fade */ }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            // ── HARMONICS: attack sharpness (impulse length) + overdrive ──
            // Sharp = 1-sample impulse. Soft = ~40-sample ramp.
            float attackSharp = DspUtil.Clamp01(p.Harmonics);
            int   impulseLen  = 1 + (int)((1f - attackSharp) * 40f);   // 1..41
            float overdrive   = 1f + attackSharp * 4f;                 // 1..5

            // ── TIMBRE: brightness — pitch-sweep top + filter Q ──
            float brightness = DspUtil.Clamp01(p.Timbre);
            float pitchTop   = _baseHz * (2f + brightness * 6f);   // 2..8× base
            float qFactor    = 20f + brightness * 30f;             // 20..50
            float damp       = 1f / qFactor;

            // ── MORPH: decay time ──
            // Amp env decay 50 ms .. ~1 s, pitch env decay 20 ms .. 100 ms.
            float morph = DspUtil.Clamp01(p.Morph);
            float ampDecaySec   = 0.05f + morph * 0.95f;
            float pitchDecaySec = 0.02f + morph * 0.08f;
            _ampCoef   = MathF.Exp(-1f / (ampDecaySec   * _sr));
            _pitchCoef = MathF.Exp(-1f / (pitchDecaySec * _sr));

            // ── Trigger handling — start impulse on first sample of this block ──
            if (_triggerPending)
            {
                _impulseCounter = impulseLen;
                _triggerPending = false;
            }

            const float TWO_PI = 2f * MathF.PI;

            for (int i = 0; i < n; i++)
            {
                // Current resonator/oscillator pitch (sweeps top → base)
                float currentHz = _baseHz + (pitchTop - _baseHz) * _pitchEnv;
                if (currentHz > _sr * 0.45f) currentHz = _sr * 0.45f;

                // SVF coefficient (clamped for Chamberlin stability)
                float f = MathF.Min(2f * MathF.Sin(MathF.PI * currentHz / _sr), 1.8f);

                // Single-sample (or short) impulse excites the resonator
                float impulse = 0f;
                if (_impulseCounter > 0)
                {
                    impulse = 1f;
                    _impulseCounter--;
                }

                // ── OUT path: bridged-T resonator (Chamberlin SVF) ──
                _resLp += f * _resBp;
                float hp = impulse - _resLp - damp * _resBp;
                _resBp += f * hp;

                // Denormal protection — when the SVF state decays toward zero
                // (after the impulse, no more excitation) the values eventually
                // cross into denormal range (~1e-38) where each FP op traps to
                // microcode and runs 50-100× slower. Worst at high resonance
                // frequencies where the SVF decays fast enough to hit denormals
                // before _ampEnv reaches the IsSilent threshold. Flushing at
                // 1e-25 is well above denormal range and well below audibility.
                if (_resBp > -1e-25f && _resBp < 1e-25f) _resBp = 0f;
                if (_resLp > -1e-25f && _resLp < 1e-25f) _resLp = 0f;

                // Bandpass output is the natural "kick" tap; overdrive +
                // soft-clip add 808-style grit.
                float overdriven = MathF.Tanh(_resBp * overdrive);
                outBuf[i] += overdriven * _ampEnv * 0.8f;

                // ── AUX path: FM-sine kick ──
                // Modulator at 2× carrier, modulation index decays with amp env
                // so the FM-rich attack softens into a sine-like tail.
                float dtCar = currentHz / _sr;
                float dtMod = currentHz * 2f / _sr;

                float modSig = MathF.Sin(TWO_PI * _phaseMod);
                float modIndex = 3f * _ampEnv;
                float fmSig  = MathF.Sin(TWO_PI * _phaseCar + modIndex * modSig);

                // "Diode" soft-clip — slight rounding of FM peaks
                float shaped = MathF.Tanh(fmSig * 1.5f);
                auxBuf[i] += shaped * _ampEnv * 0.6f;

                _phaseCar += dtCar; if (_phaseCar >= 1f) _phaseCar -= 1f;
                _phaseMod += dtMod; if (_phaseMod >= 1f) _phaseMod -= 1f;

                // Advance envelopes
                _ampEnv   *= _ampCoef;
                _pitchEnv *= _pitchCoef;
                // Pitch env denormal flush (amp env can't reach denormal range
                // before IsSilent's 1e-5 threshold fires, but pitch env has no
                // such gate and decays unboundedly).
                if (_pitchEnv < 1e-25f) _pitchEnv = 0f;
            }
        }
    }
}
