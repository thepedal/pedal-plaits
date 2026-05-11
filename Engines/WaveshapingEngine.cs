// Engines/WaveshapingEngine.cs — Plaits engine 1: Waveshaping.
//
// Manual:
//   HARMONICS: waveshaper waveform
//   TIMBRE:    wavefolder amount
//   MORPH:     waveform asymmetry
//   AUX:       variant with a different wavefolder curve (Warps-style)
//
// Signal chain: asymmetric tri → waveshaper → wavefolder → output.
// Same architecture as Tides when it runs at audio rate.
//
// v0.1 implementation choices:
// - HARMONICS blends through three waveshape curves: identity → tanh
//   → cubic. Three is enough to give audibly distinct positions across
//   the knob; the original Plaits has more curves and could be
//   expanded with a small LUT later.
// - OUT wavefolder uses sin(πx) — smooth, classic sine wavefolder.
// - AUX wavefolder uses triangle-fold — sharper, more aggressive
//   harmonics. The two folders give noticeably different character
//   especially at high fold amounts.
// - MORPH clamped to [0.15, 0.85] to avoid the worst aliasing from
//   near-instantaneous slope changes at extreme asymmetry. Full-range
//   asymmetry would need PolyBLEP or oversampling.
// - No anti-aliasing on the wavefolder output — high TIMBRE values
//   produce significant harmonic content above Nyquist. Documented
//   limitation; acceptable for v0.1.

using System;
using PedalPlaits.Util;

namespace PedalPlaits.Engines
{
    public class WaveshapingEngine : IEngine
    {
        public bool IsPercussive => false;

        float _sr = 44100f;
        float _baseHz = 220f;
        float _phase;   // free-running across notes (SH101 §7 convention)

        public void Init(float sr) { _sr = sr; Reset(); }
        public void Reset() { _phase = 0f; }

        public void NoteOn(int midiNote, float velocity)
        {
            _baseHz = DspUtil.MidiToHz(midiNote);
        }

        public void NoteOff() { /* env handles fade */ }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            // ── MORPH: asymmetry, clamped [0.15, 0.85] ──
            // 0.5 = symmetric tri; lower = saw-like (fast rise, slow fall);
            // higher = reverse saw (slow rise, fast fall).
            float asymmetry = DspUtil.Lerp(0.15f, 0.85f, DspUtil.Clamp01(p.Morph));
            float invRise = 1f / asymmetry;
            float invFall = 1f / (1f - asymmetry);

            // ── HARMONICS: waveshaper curve selection ──
            float harm = DspUtil.Clamp01(p.Harmonics);

            // ── TIMBRE: wavefolder amount, 1 (no fold) .. 8 (aggressive) ──
            float fold = 1f + p.Timbre * 7f;

            float dt = _baseHz / _sr;
            const float OUT_GAIN = 0.5f;

            for (int i = 0; i < n; i++)
            {
                // ── Asymmetric triangle ──
                // Linear ramp up from -1 to +1 in [0, asymmetry];
                // linear ramp down from +1 to -1 in [asymmetry, 1].
                float tri;
                if (_phase < asymmetry)
                    tri = -1f + 2f * _phase * invRise;
                else
                    tri = 1f - 2f * (_phase - asymmetry) * invFall;

                // ── Waveshaper ──
                float shaped = WaveShape(tri, harm);

                // ── Wavefolder, two variants ──
                // OUT: sin(π * x * fold) — smooth sine folder
                float folded = MathF.Sin(MathF.PI * shaped * fold * 0.5f);
                outBuf[i] += folded * OUT_GAIN;

                // AUX: triangle wavefold — sharper edges, more harmonics
                float foldedAux = TriFold(shaped * fold);
                auxBuf[i] += foldedAux * OUT_GAIN;

                _phase += dt;
                if (_phase >= 1f) _phase -= 1f;
            }
        }

        // Waveshaper: blend through identity → tanh → cubic.
        // Each adds different harmonic content (clean → smooth saturation
        // → odd-harmonic emphasis).
        static float WaveShape(float x, float h)
        {
            if (h < 0.5f)
            {
                float t = h * 2f;
                float sat = MathF.Tanh(x * 2f);
                return x * (1f - t) + sat * t;
            }
            else
            {
                float t = (h - 0.5f) * 2f;
                float sat   = MathF.Tanh(x * 2f);
                // Cubic emphasizing odd harmonics; passes through ±1 exactly.
                float cubic = 1.5f * x - 0.5f * x * x * x;
                return sat * (1f - t) + cubic * t;
            }
        }

        // Triangle wavefolder: folds any input x into [-1, +1] via
        // reflection. Output is a triangle wave of x. No loops — bounded
        // single-step reduction is sufficient for our TIMBRE range.
        static float TriFold(float x)
        {
            // Reduce x to one fundamental period [-2, +2] first
            if (x > 2f || x < -2f)
                x = x - 4f * MathF.Floor((x + 2f) * 0.25f);
            // Fold to [-1, +1]
            if (x >  1f) return  2f - x;
            if (x < -1f) return -2f - x;
            return x;
        }
    }
}
