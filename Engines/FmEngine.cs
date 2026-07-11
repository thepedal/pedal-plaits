// Engines/FmEngine.cs — Plaits engine 2: Two-op FM.
//
// Manual:
//   HARMONICS: frequency ratio (carrier:modulator)
//   TIMBRE:    modulation index
//   MORPH:     feedback — op2-self past 12, op1-self before 12
//                (12 o'clock = no feedback, "clean" 2-op FM)
//   AUX:       sub-oscillator (carrier at half frequency)
//
// v0.1 design choices documented inline. Faithful in spirit to
// plaits/dsp/engine/fm_engine.cc but written fresh rather than
// line-by-line ported.

using System;
using PedalPlaits.Util;

namespace PedalPlaits.Engines
{
    public class FmEngine : IEngine
    {
        public bool IsPercussive => false;

        // v1.1 — quantised FM ratios (snap-to-table on HARMONICS).
        // Eleven entries gives ~12-unit-wide HARMONICS bands. Spread
        // matches Plaits' convention: more entries below 2.0 (where most
        // musical FM happens) than above. 1.0 sits at index 3 — slightly
        // below the centre of the knob, which is fine because clean
        // 1:1 FM is a useful but not universal destination.
        static readonly float[] FM_RATIOS =
            { 0.25f, 0.5f, 0.75f, 1.0f, 1.5f, 2.0f, 3.0f, 4.0f, 5.0f, 7.0f, 11.0f };

        float _sr = 44100f;
        float _baseHz = 220f;

        // Two phase accumulators, normalized 0..1.
        // Free-running across notes (SH101 §7 convention) — no reset on NoteOn.
        float _phase1, _phase2;
        float _phaseSub;   // independent accumulator for the AUX sub-oscillator

        // One-sample-delayed outputs for self-feedback paths.
        float _op1Prev, _op2Prev;

        public void Init(float sr) { _sr = sr; Reset(); }

        public void Reset()
        {
            _phase1 = _phase2 = 0f;
            _phaseSub = 0f;
            _op1Prev = _op2Prev = 0f;
        }

        public void NoteOn(int midiNote, float velocity)
        {
            _baseHz = DspUtil.MidiToHz(midiNote);
            // Don't reset phases — analog-style continuity across notes.
        }

        public void NoteOff() { /* env handles fade */ }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            // ── HARMONICS: modulator/carrier frequency ratio (v1.1 — snap) ──
            // Quantises to a fixed table of musically-useful ratios rather
            // than sweeping smoothly. Each HARMONICS value maps to one of
            // 11 ratios — landing on "clean" intervals by ear is much
            // easier than threading a continuous knob to find 1:1 or 2:1.
            // The table is biased slightly toward the lower end (more
            // entries below 2.0 than above) because that's where most
            // musically-useful FM lives. Index calculation:
            //   index = (Harmonics01 * 11)  clamped to [0, 10]
            // So HARMONICS values are grouped into ~12-unit-wide bands.
            int rIdx = (int)(p.Harmonics * FM_RATIOS.Length);
            if (rIdx >= FM_RATIOS.Length) rIdx = FM_RATIOS.Length - 1;
            if (rIdx < 0) rIdx = 0;
            float ratio = FM_RATIOS[rIdx];

            // ── TIMBRE: modulation index in RADIANS ──
            // 0 = no FM (pure carrier sine), 6 = aggressive bell-like
            // sidebands. Standard FM literature uses radian indices.
            float modIndex = p.Timbre * 6f;

            // ── v1.8 sideband AA — soft-clip mod index so peak sideband
            // stays below ~90% of Nyquist. For sine-on-sine FM, the
            // bandwidth scales as f · (1 + I · r). Solving for I gives
            // the upper safe limit; tanh-soft-clip the requested index
            // toward that limit so behaviour is unchanged at low pitches
            // and smoothly bounded at high pitches without a hard knee.
            // Floor of 0.5 keeps some FM character alive at very high
            // notes where the analytical safe limit goes near zero.
            float safeNyquist = _sr * 0.45f;
            float maxSafeIdx  = MathF.Max(0.5f,
                                          (safeNyquist / _baseHz - 1f) / ratio);
            modIndex = maxSafeIdx * MathF.Tanh(modIndex / maxSafeIdx);

            // ── MORPH: feedback split around 12 o'clock ──
            // <0.5 → op1 (carrier) self-feedback, "chaotic" character
            // =0.5 → no feedback, clean 2-op FM
            // >0.5 → op2 (modulator) self-feedback, "rougher" modulator
            // Feedback amounts in radians, bounded to π/2 at extremes
            // for v0.1 to avoid runaway self-modulation.
            float fb1 = p.Morph < 0.5f ? (0.5f - p.Morph) * MathF.PI : 0f;
            float fb2 = p.Morph > 0.5f ? (p.Morph - 0.5f) * MathF.PI : 0f;

            float f1  = _baseHz;
            float f2  = _baseHz * ratio;
            float dt1 = f1 / _sr;
            float dt2 = f2 / _sr;

            const float TWO_PI = 2f * MathF.PI;
            const float OUT_GAIN = 0.5f;   // headroom — strong FM peaks near ±1

            for (int i = 0; i < n; i++)
            {
                // Modulator (op2) with optional self-feedback
                float op2 = MathF.Sin(TWO_PI * _phase2 + fb2 * _op2Prev);
                _op2Prev = op2;

                // Carrier (op1):  phase + modIndex * modulator + self-fb
                float op1 = MathF.Sin(
                    TWO_PI * _phase1
                    + modIndex * op2
                    + fb1 * _op1Prev);
                _op1Prev = op1;

                outBuf[i] += op1 * OUT_GAIN;

                // AUX: sub-oscillator (carrier at half frequency).
                //
                // NOTE (v1.13): this was previously derived as
                //     sin(TWO_PI * (_phase1 * 0.5f))
                // which is NOT a half-frequency sine. _phase1 wraps 0→1, so the
                // argument only ever sweeps 0→π: the result is an always-positive
                // half-sine hump at the SAME frequency as the carrier, with a mean
                // of 2/π ≈ 0.637. That produced both a wrong timbre (no sub octave)
                // and the largest DC offset in the machine — enveloped by the LPG,
                // so it thumped on every note-on rather than sitting still.
                //
                // A real sub needs its own accumulator advancing at half rate and
                // wrapping over two carrier periods. Zero-mean by construction.
                float sub = MathF.Sin(TWO_PI * _phaseSub);
                auxBuf[i] += sub * OUT_GAIN;

                _phase1 += dt1; if (_phase1 >= 1f) _phase1 -= 1f;
                _phaseSub += dt1 * 0.5f; if (_phaseSub >= 1f) _phaseSub -= 1f;
                _phase2 += dt2; if (_phase2 >= 1f) _phase2 -= 1f;
            }
        }
    }
}
