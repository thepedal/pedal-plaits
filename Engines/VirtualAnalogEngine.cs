// Engines/VirtualAnalogEngine.cs — Plaits engine 0.
//
// Manual:
//   HARMONICS: detuning between two waves
//   TIMBRE:    variable square — narrow pulse → full square → hardsync formants
//   MORPH:     variable saw — triangle → saw with widening notch (Braids' CSAW)
//   AUX:       sum of two hardsync'd waveforms, shape from MORPH, detune from HARMONICS
//
// v1.5 changes:
//   1. AUX is no longer ring-modulation. It now renders Plaits' documented
//      "sum of two hardsync'd waveforms": two (master, slave) pairs where
//      master B runs detuned from master A by the same HARMONICS-driven
//      ratio that drives osc 2 on the main output. Each slave is MORPH-
//      shaped through the same triangle/saw/notched-saw blend as osc 1.
//      AUX slaves use a fixed 2× ratio (integer, no AA concerns).
//   2. Hardsync anti-aliasing: replaces v1.4's "suppress PolyBLEP for one
//      sample after master wrap" with a scaled-correction approach. At
//      post-master-wrap samples, PolyBLEP is scaled by frac(syncRatio) to
//      match the actual discontinuity magnitude rather than assuming the
//      standard ±2 saw jump. Integer ratios (where the natural slave wrap
//      coincides with master wrap) use unscaled PolyBLEP. Non-integer
//      ratios get the proportional correction.
//
// State note: _master1WrappedLastStep and _master2WrappedLastStep both
// drive the same AA logic — _phase1 for the main-output slave and AUX
// pair A, _phase2 for AUX pair B. AUX pair A's 2× ratio means its master-
// wrap discontinuity is zero (integer), so the AA scaling has no effect
// there but the code path is the same.

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

        // v1.5 hardsync AA: track master wrap for scaled-PolyBLEP correction
        // at post-master-wrap samples. _phase1 drives the main-output slave
        // (TIMBRE > 0.5) and AUX pair A; _phase2 drives AUX pair B.
        bool _master1WrappedLastStep;
        bool _master2WrappedLastStep;

        public void Init(float sr) { _sr = sr; Reset(); }

        public void Reset()
        {
            _phase1 = 0f;
            _phase2 = 0f;
            _master1WrappedLastStep = false;
            _master2WrappedLastStep = false;
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

            // Main-output hardsync slave (active when TIMBRE > 0.5).
            // syncRatio_main is exponential 1..8, giving sweepable formant
            // from master frequency to 3 octaves above.
            float syncMix = MathF.Max(0f, (p.Timbre - 0.5f) * 2f);
            float syncRatio_main = MathF.Pow(2f, syncMix * 3f);
            float dtSync_main = dt1 * syncRatio_main;
            float fracR_main = syncRatio_main - MathF.Floor(syncRatio_main);
            float pulseMix = 1f - syncMix;

            // AUX hardsync slaves — fixed 2× ratio (integer, no AA scaling
            // needed since master-wrap discontinuity is zero at integer ratios).
            const float syncRatio_aux = 2f;
            float dtSync_aux_a = dt1 * syncRatio_aux;
            float dtSync_aux_b = dt2 * syncRatio_aux;

            // MORPH params (shared between osc 1 and AUX slave shaping)
            float morphTri   = 1f - MathF.Min(1f, p.Morph * 2f);          // 1@0, 0@0.5
            float morphNotch = MathF.Max(0f, (p.Morph - 0.5f) * 2f);     // 0@0.5, 1@1
            float notchWidth = morphNotch * 0.4f;                        // up to 40% of period
            float morphSaw   = 1f - morphTri - morphNotch;               // saw weight 0..1@0.5..0

            for (int i = 0; i < n; i++)
            {
                // ── OSC 1 — variable saw (MORPH-shaped) ──
                float saw1Naive = 2f * _phase1 - 1f;
                float saw1 = saw1Naive - PolyBlep.Compute(_phase1, dt1);
                float morph1 = MorphShape(saw1, _phase1, dt1, morphTri, morphSaw, morphNotch, notchWidth);

                // ── OSC 2 — variable square (TIMBRE-shaped) ──
                float pulse2 = (_phase2 < pwm) ? 1f : -1f;
                pulse2 += PolyBlep.Compute(_phase2, dt2);
                float fall2 = _phase2 + 1f - pwm;
                if (fall2 >= 1f) fall2 -= 1f;
                pulse2 -= PolyBlep.Compute(fall2, dt2);

                // ── MAIN HARDSYNC SLAVE — variable ratio, scaled PolyBLEP AA ──
                float syncSaw_main = HardsyncSaw(_phase1, syncRatio_main, dtSync_main,
                                                  fracR_main, _master1WrappedLastStep);

                // OSC 2 output: cross-fade variable pulse and hardsync slave
                float osc2Out = pulse2 * pulseMix + syncSaw_main * syncMix;

                // OUT = morph saw 1 + osc2 output, balanced
                outBuf[i] += 0.5f * (morph1 + osc2Out);

                // ── AUX — sum of two hardsync'd MORPH-shaped slaves ──
                // Pair A: master = _phase1, slave at 2× master
                float syncSaw_aux_a = HardsyncSaw(_phase1, syncRatio_aux, dtSync_aux_a,
                                                   0f, _master1WrappedLastStep);
                float morphAuxA = MorphShape(syncSaw_aux_a, FracMul(_phase1, syncRatio_aux), dtSync_aux_a,
                                              morphTri, morphSaw, morphNotch, notchWidth);

                // Pair B: master = _phase2 (detuned), slave at 2× master
                float syncSaw_aux_b = HardsyncSaw(_phase2, syncRatio_aux, dtSync_aux_b,
                                                   0f, _master2WrappedLastStep);
                float morphAuxB = MorphShape(syncSaw_aux_b, FracMul(_phase2, syncRatio_aux), dtSync_aux_b,
                                              morphTri, morphSaw, morphNotch, notchWidth);

                auxBuf[i] += 0.25f * (morphAuxA + morphAuxB);

                // Advance _phase1 with wrap detection
                _phase1 += dt1;
                _master1WrappedLastStep = _phase1 >= 1f;
                if (_master1WrappedLastStep) _phase1 -= 1f;

                // Advance _phase2 with wrap detection
                _phase2 += dt2;
                _master2WrappedLastStep = _phase2 >= 1f;
                if (_master2WrappedLastStep) _phase2 -= 1f;
            }
        }

        // ─────────────────────────────────────────────────────────
        // Hardsync slave with scaled-PolyBLEP AA. Phase-derived from
        // a master phase; correction is standard PolyBLEP for slave's
        // own natural wraps, scaled by frac(ratio) at post-master-wrap
        // samples (where the discontinuity magnitude is -2 × frac(ratio)
        // rather than the standard -2 of a saw wrap).
        //
        // For integer ratios (fracR ≈ 0), no master-wrap discontinuity
        // exists — natural slave wrap coincides with master wrap — and
        // standard PolyBLEP applies. The branch below short-circuits to
        // unscaled correction in that case.
        // ─────────────────────────────────────────────────────────
        static float HardsyncSaw(float masterPhase, float syncRatio,
                                  float dtSync, float fracR,
                                  bool postMasterWrap)
        {
            float syncPhase = FracMul(masterPhase, syncRatio);
            float correction = PolyBlep.Compute(syncPhase, dtSync);

            // If master just wrapped AND the ratio is non-integer, the
            // PolyBLEP correction at low syncPhase fired assuming the
            // standard -2 saw jump. The actual jump at master wrap is
            // -2 × fracR, so scale correction by fracR.
            //
            // Note: PolyBLEP's "t > 1-dt" (pre-wrap) branch doesn't apply
            // to master-induced wraps because syncPhase just before master
            // wrap = fracR < 1-dt for non-integer ratios. So only the
            // post-wrap correction needs scaling.
            if (postMasterWrap && fracR > 1e-6f)
            {
                correction *= fracR;
            }

            return 2f * syncPhase - 1f - correction;
        }

        // (masterPhase × syncRatio) mod 1 — slave's effective phase
        // under phase-derived hardsync.
        static float FracMul(float masterPhase, float syncRatio)
        {
            float p = masterPhase * syncRatio;
            return p - MathF.Floor(p);
        }

        // Apply MORPH blend (triangle → saw → notched-saw) to a saw
        // value at a given phase position. Shared by osc 1 and the
        // AUX hardsync slaves so both follow the same MORPH vocabulary.
        //
        // Notch anti-aliasing:
        //   v1.6 — wrap-side AA. The notched component holds a flat +1 in
        //   the region phase > 1-notchWidth, then drops to the post-wrap
        //   saw value (~-1) when phase wraps. The non-notch region already
        //   inherits PolyBLEP correctly through sawValue's weight. The
        //   notch region's +1→saw transition gets an extra PolyBLEP term
        //   scaled by wNotch.
        //   v1.7 — entry-side AA. The notched component jumps from saw
        //   value (= 1 − 2·notchWidth) up to +1 at phase = 1 − notchWidth,
        //   magnitude +2·notchWidth weighted by wNotch. Apply a phase-
        //   shifted PolyBLEP centred at that disc position — equivalent
        //   to rotating the standard wrap-edge polynomial so its "wrap"
        //   coincides with the notch entry rather than phase=1.
        static float MorphShape(float sawValue, float phase, float dt,
                                 float wTri, float wSaw, float wNotch,
                                 float notchWidth)
        {
            float tri  = 4f * MathF.Abs(phase - 0.5f) - 1f;
            bool inNotch = notchWidth > 0f && phase > 1f - notchWidth;
            float notched = inNotch ? 1f : sawValue;
            float morph = wTri * tri + wSaw * sawValue + wNotch * notched;

            // Wrap-side notch AA (v1.6) — fires only inside the notch region
            if (inNotch)
            {
                morph -= wNotch * PolyBlep.Compute(phase, dt);
            }

            // Entry-side notch AA (v1.7) — fires near phase = 1 − notchWidth
            // where the notched component jumps from saw to +1 by an amount
            // 2·notchWidth. The PolyBLEP polynomial smooths a -2 saw jump
            // when subtracted, so for a +J jump we instead ADD (J/2 × polyBlep);
            // here J = 2·notchWidth so the scale is notchWidth.
            if (notchWidth > 0f)
            {
                float effectivePhase = phase - (1f - notchWidth);
                if (effectivePhase < 0f) effectivePhase += 1f;
                morph += wNotch * notchWidth * PolyBlep.Compute(effectivePhase, dt);
            }

            return morph;
        }
    }
}
