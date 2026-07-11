// DcBlocker.cs — one-pole DC-blocking high-pass (Core §43.2b)
//
// Catch-all DC removal for the engine outputs. Plaits' engines are a mixed
// bag: some are zero-mean by construction, some carry a large closed-form DC
// that we remove analytically at the source (Core §43.2a), and some leave a
// residue that has no closed form. This blocker handles the last group and
// backstops the others.
//
// Per-engine audit (v1.13):
//   Zero-mean by construction, no DC:
//     Harmonic (sum of sines), FM carrier (PM of a sine), PhaseDistortion
//     (sin of a monotonically remapped phase), Wavetable (NormaliseRaw already
//     subtracts the mean — required for its integrated/cumsum tables),
//     Waveshaping (its "asymmetry" is in TIME, not amplitude: an asymmetric
//     triangle still has a symmetric uniform value distribution, and tanh, the
//     cubic, sin() and TriFold are all ODD, so they preserve zero mean —
//     verified numerically across the whole TIMBRE/MORPH/HARMONICS space).
//
//   Large, closed-form DC — removed analytically at the source instead:
//     VirtualAnalog — the variable pulse (mean 2·pwm−1, reaching −0.9 at
//       TIMBRE=0) and the notched saw (mean notchWidth², +0.16 at MORPH=1).
//     FM AUX sub — was not a sub at all but a rectified half-sine hump
//       (mean 2/π ≈ 0.637); fixed properly with its own accumulator.
//
//   Residual DC with no closed form — THIS blocker's job:
//     InharmonicString — Karplus-Strong. The loop's one-pole LP has unity gain
//       at DC, so with feedback g≈0.999 any DC in the Hann-windowed noise burst
//       is amplified by ~g/(1−g) (hundreds of times) and decays only slowly.
//       Each burst's mean is random, so there's no constant to subtract.
//     GranularCloud — each grain is a bipolar carrier times a unipolar envelope
//       over a short window, so individual grains carry a windowed-mean residue
//       that doesn't cancel exactly across a sparse cloud.
//     ModalResonator / BassDrum / SnareDrum / HiHat / FilteredNoise — impulse-
//       and noise-excited resonators; nominally zero-mean, but excitation-
//       dependent residue is possible and costs nothing to mop up.
//
// Placement (see Voice.Render): applied to OUT and AUX *before* the LPG, i.e.
// before the VCA (Core §43.3). The engines' DC is a property of the tone, not
// the envelope, so pre-LPG the blocker sees a steady DC and removes it cleanly.
// Post-LPG it would instead see DC×env(t) — an envelope-shaped ramp — and
// high-pass that, thumping on every note-on. (The old FM sub bug was exactly
// this failure in the wild: a 0.637 pedestal enveloped by the LPG.)
//
// State is NOT cleared per note, only on Init/engine change — like the
// AC-coupling capacitor it models, it holds charge between notes, so
// successive notes see an already-settled blocker and no settle transient.
//
// y[n] = x[n] − x[n−1] + R·y[n−1];  zero at DC (z=1), pole at z=R.

using System;

namespace PedalPlaits
{
    internal sealed class DcBlocker
    {
        float _xPrev;
        float _yPrev;
        float _r = 0.9999f;   // sane default until SetSampleRate runs
        float _sr;

        // 10 Hz: clears DC while leaving the lowest musical fundamentals (and
        // all but sub-sonic content) untouched.
        public const float CornerHz = 10f;

        /// <summary>Recompute the pole for a new sample rate (Core §29). No-op if unchanged.</summary>
        public void SetSampleRate(float sr)
        {
            if (sr <= 0f || sr == _sr) return;
            _sr = sr;
            _r  = MathF.Exp(-2f * MathF.PI * CornerHz / sr);
        }

        public void Reset()
        {
            _xPrev = 0f;
            _yPrev = 0f;
        }

        /// <summary>In-place DC removal over a buffer.</summary>
        public void Process(float[] buf, int n)
        {
            float xPrev = _xPrev, yPrev = _yPrev, r = _r;

            for (int i = 0; i < n; i++)
            {
                float x = buf[i];
                float y = x - xPrev + r * yPrev;
                xPrev = x;

                // Denormal flush (Core §30). These engines decay to silence and
                // idle constantly, so the blocker's tail is a prime candidate for
                // slipping into denormal range and trapping to microcode.
                if (y > -1e-20f && y < 1e-20f) y = 0f;

                yPrev  = y;
                buf[i] = y;
            }

            _xPrev = xPrev;
            _yPrev = yPrev;
        }
    }
}
