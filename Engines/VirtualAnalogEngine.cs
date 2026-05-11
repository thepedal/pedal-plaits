// Engines/VirtualAnalogEngine.cs — Plaits engine 0.
//
// Manual:
//   HARMONICS: detuning between two waves
//   TIMBRE:    variable square — narrow pulse → full square → hardsync formants
//   MORPH:     variable saw — triangle → saw with widening notch (Braids' CSAW)
//   AUX:       sum of two hardsync'd waveforms (see notes — current port uses
//              ring-mod as a v1.5+ TODO)
//
// v1.4 adds the hardsync-formants region at TIMBRE > 0.5. Below 0.5 the
// engine behaves as before (variable pulse → square). At TIMBRE = 0.5 the
// pulse holds at square and a hardsync sawtooth slave fades in, its
// frequency rising exponentially from 1× to 8× the master. The slave uses
// phase-derived sync (syncPhase = master_phase × ratio, mod 1) which is
// mathematically equivalent to a free-running slave with hard-reset on
// master wrap. PolyBLEP corrects the slave's own wraps; correction is
// suppressed for one sample after master wrap (where the discontinuity has
// variable magnitude that standard PolyBLEP would mis-handle, producing
// some aliasing at high pitches with non-integer sync ratios — v1.5+
// improvement).
//
// AUX still uses ring-modulation (faster, audibly distinct). Plaits' actual
// AUX is "sum of two hardsync'd waveforms" with separate MORPH/HARMONICS
// roles; deferred to a future revision.

using System;
using PedalPlaits.Util;

namespace PedalPlaits.Engines
{
    public class VirtualAnalogEngine : IEngine
    {
        public bool IsPercussive => false;

        float _sr = 44100f;

        // Two phase accumulators (osc 1 main, osc 2 detuned)
        float _phase1, _phase2;
        float _baseHz = 220f;

        // v1.4 hardsync: suppress PolyBLEP on syncSaw for one sample
        // following a master wrap (where the variable-magnitude
        // discontinuity would be over-corrected by standard PolyBLEP).
        bool _suppressSlaveBlep;

        public void Init(float sr) { _sr = sr; Reset(); }

        public void Reset()
        {
            _phase1 = 0f;
            _phase2 = 0f;
            _suppressSlaveBlep = false;
        }

        public void NoteOn(int midiNote, float velocity)
        {
            _baseHz = DspUtil.MidiToHz(midiNote);
            // Free-running phases (SH101 §7) — don't reset on NoteOn for analog feel.
        }

        public void NoteOff() { /* no-op — LPG/env handle release */ }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            // HARMONICS: detune in cents, 0..1 -> 0..50 cents
            float detuneCents = p.Harmonics * 50f;
            float detuneRatio = MathF.Pow(2f, detuneCents / 1200f);

            float f1  = _baseHz;
            float f2  = _baseHz * detuneRatio;
            float dt1 = f1 / _sr;
            float dt2 = f2 / _sr;

            // TIMBRE region 0..0.5: pulse width 0.05 (narrow) → 0.5 (square).
            // TIMBRE region 0.5..1: pwm holds at 0.5; hardsync formants grow.
            float pwm = (p.Timbre < 0.5f)
                ? DspUtil.Lerp(0.05f, 0.5f, p.Timbre * 2f)
                : 0.5f;

            // v1.4 hardsync formant slave (active when TIMBRE > 0.5).
            // syncMix = 0 at/below TIMBRE=0.5, ramps 0..1 over TIMBRE 0.5..1.
            // syncRatio = exponential 1..8, giving a sweepable formant from
            // master frequency to 3 octaves above.
            float syncMix = MathF.Max(0f, (p.Timbre - 0.5f) * 2f);
            float syncRatio = MathF.Pow(2f, syncMix * 3f);
            float dtSync = dt1 * syncRatio;
            float pulseMix = 1f - syncMix;

            // MORPH: triangle (0) → saw (0.5) → notched-saw (1).
            // Triangle shaping = absolute value of saw, scaled.
            // Notched saw = saw with a widening DC-shifted region near the wrap.
            float morphTri  = 1f - MathF.Min(1f, p.Morph * 2f);          // 1@0, 0@0.5
            float morphNotch = MathF.Max(0f, (p.Morph - 0.5f) * 2f);     // 0@0.5, 1@1
            float notchWidth = morphNotch * 0.4f;                        // up to 40% of period

            for (int i = 0; i < n; i++)
            {
                // ── OSC 1 — variable saw (MORPH-shaped) ──
                float saw1Naive = 2f * _phase1 - 1f;
                float saw1 = saw1Naive - PolyBlep.Compute(_phase1, dt1);
                // Triangle blend (rectified saw, scaled to ±1, smoother)
                float tri1 = 4f * MathF.Abs(_phase1 - 0.5f) - 1f;
                // Notch: zero out a region of the saw near phase=1 (wrap point)
                float notched1 = saw1;
                if (notchWidth > 0f && _phase1 > 1f - notchWidth) notched1 = 1f;
                float morph1 = morphTri * tri1
                             + (1f - morphTri - morphNotch) * saw1
                             + morphNotch * notched1;

                // ── OSC 2 — variable square (TIMBRE-shaped) ──
                float pulse2 = (_phase2 < pwm) ? 1f : -1f;
                pulse2 += PolyBlep.Compute(_phase2, dt2);
                float fall2 = _phase2 + 1f - pwm;
                if (fall2 >= 1f) fall2 -= 1f;
                pulse2 -= PolyBlep.Compute(fall2, dt2);

                // ── HARDSYNC SLAVE (v1.4) — phase-derived sawtooth ──
                // syncPhase tracks (master_phase * ratio) mod 1, which is
                // mathematically equivalent to a free-running saw at frequency
                // f_master * syncRatio with hard reset on master wrap. Phase
                // is taken from _phase1 BEFORE its advance, so the slave's
                // sample value corresponds to the same instant as osc1's.
                float syncPhaseRaw = _phase1 * syncRatio;
                float syncPhase = syncPhaseRaw - MathF.Floor(syncPhaseRaw);
                float syncBlepCorr = _suppressSlaveBlep ? 0f
                                                       : PolyBlep.Compute(syncPhase, dtSync);
                float syncSaw = 2f * syncPhase - 1f - syncBlepCorr;

                // OSC 2 output: cross-fade variable pulse and hardsync slave
                float osc2Out = pulse2 * pulseMix + syncSaw * syncMix;

                // OUT = morph saw 1 + osc2 output, balanced
                outBuf[i] += 0.5f * (morph1 + osc2Out);

                // AUX = ring-mod of the two sources (v1.5+ TODO: faithful
                // "sum of two hardsync'd waveforms" rendering).
                auxBuf[i] += 0.5f * morph1 * pulse2;

                // Advance phases with master-wrap detection (for hardsync)
                _phase1 += dt1;
                bool masterWraps = _phase1 >= 1f;
                if (masterWraps) _phase1 -= 1f;
                _suppressSlaveBlep = masterWraps;
                _phase2 += dt2; if (_phase2 >= 1f) _phase2 -= 1f;
            }
        }
    }
}
