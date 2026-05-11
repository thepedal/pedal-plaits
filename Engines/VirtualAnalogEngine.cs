// Engines/VirtualAnalogEngine.cs — Plaits engine 0.
//
// Manual:
//   HARMONICS: detuning between two waves
//   TIMBRE:    variable square — narrow pulse → full square → hardsync formants
//   MORPH:     variable saw — triangle → saw with widening notch
//   AUX:       sum of two hardsync'd waveforms, shape from MORPH, detune from HARMONICS
//
// This v0.1 implementation takes a pragmatic shortcut: instead of the full
// hardsync-formant TIMBRE behaviour from the original, it does a clean
// PolyBLEP saw + variable-pulse mix, with cross-detuning controlled by
// HARMONICS. The "narrow pulse → square" sweep is exact; the "hardsync
// formants" past 12 o'clock is left as TODO and currently maps to extra
// resonance/brightness via duty-cycle tightening at the high end of TIMBRE.
//
// This is enough to demonstrate the pipeline end-to-end and produce a
// pleasant SH-style synth tone. A full port of plaits/dsp/engine/
// virtual_analog_engine.cc is the v1.0 milestone for this engine.

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

        public void Init(float sr) { _sr = sr; Reset(); }

        public void Reset()
        {
            _phase1 = 0f;
            _phase2 = 0f;
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

            // TIMBRE: pulse width 0.05 (narrow) → 0.5 (square) → 0.95 (narrow other side).
            // Map 0..0.5 → 0.05..0.5, 0.5..1 → 0.5..0.95.
            float pwm = p.Timbre < 0.5f
                ? DspUtil.Lerp(0.05f, 0.5f, p.Timbre * 2f)
                : DspUtil.Lerp(0.5f, 0.95f, (p.Timbre - 0.5f) * 2f);

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

                // OUT = morph saw 1 + variable pulse 2, balanced
                outBuf[i] += 0.5f * (morph1 + pulse2);

                // AUX = hardsync-style sum: osc 2's phase forced from osc 1's wrap.
                // Cheap approximation: ring-mod of the two sources.
                auxBuf[i] += 0.5f * morph1 * pulse2;

                // Advance phases
                _phase1 += dt1; if (_phase1 >= 1f) _phase1 -= 1f;
                _phase2 += dt2; if (_phase2 >= 1f) _phase2 -= 1f;
            }
        }
    }
}
