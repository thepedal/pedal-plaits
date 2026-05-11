// Lpg.cs — Low-Pass Gate with vactrol modeling (v1.4).
//
// Plaits' LPG is the classic "vactrol" model: a strike triggers an envelope
// that controls both filter cutoff and amplitude through a vactrol (LED-LDR
// pair). The vactrol's thermal lag — fast but non-instant LED rise, slower
// LDR cool-down — gives the gate a characteristic "thwack" attack and
// gentle release that defines its sound.
//
// v1.4 adds per-sample vactrol-state tracking with separate attack (5 ms)
// and release (30 ms) time constants applied uniformly to BOTH the VCA gain
// AND the VCF cutoff (since a real vactrol drives both via one physical
// resistance change). This replaces the v0.1 model which used the raw
// DecayEnv value directly with no smoothing.
//
// Side effect — perceived decay times lengthen by ~30 ms compared to v1.3:
// the vactrol's release tail adds to whatever the DecayEnv produces. For
// short percussive decays this is audible as a small softening of the
// attack and a slight extension of the release. This matches Plaits'
// character; the Decay parameter may need re-trimming on patches that
// relied on the previous tight gating.
//
// Response parameter unchanged: 0 = VCFA (vactrol drives both cutoff and
// amp), 1 = pure VCA (cutoff held at maximum, amp still vactrol-tracked).

using System;

namespace PedalPlaits
{
    public class Lpg
    {
        float _sr = 44100f;

        // Strike level — the latest velocity-driven amplitude. Held constant
        // until the next Strike(); env handles all time-decay.
        float _strikeLevel;

        // One-pole filter state per channel
        float _yL, _yR;

        // v1.4 vactrol model — current state tracking the input envelope
        // with asymmetric attack/release time constants. Continuous across
        // strikes so rapid retriggers produce a smoothed envelope shape.
        float _vactrolState;
        float _attCoef;   // attack-rate coefficient
        float _decCoef;   // release-rate coefficient

        public void Init(float sr)
        {
            _sr = sr;
            _yL = _yR = 0f;
            _strikeLevel = 0f;
            _vactrolState = 0f;
            UpdateVactrolCoefs();
        }

        void UpdateVactrolCoefs()
        {
            // Time constants for typical vactrol thermal response.
            // 5 ms attack, 30 ms release — characteristic asymmetry.
            const float ATTACK_TAU  = 0.005f;
            const float RELEASE_TAU = 0.030f;
            _attCoef = 1f - MathF.Exp(-1f / (ATTACK_TAU  * _sr));
            _decCoef = 1f - MathF.Exp(-1f / (RELEASE_TAU * _sr));
        }

        public void Strike(float velocity)
        {
            // Overwrite, don't max-clamp — a soft strike after a hard one
            // should actually be softer, not stay at the previous peak.
            _strikeLevel = velocity <= 0f ? 1f : velocity;
        }

        // Process in-place. envValue 0..1 is the current state of the
        // internal decay envelope (treated as the vactrol's target).
        // response 0 = full VCFA, 1 = pure VCA. decay01 unused in v1.4.
        public void Process(float[] outBuf, float[] auxBuf, int n,
                             float envValue, float response, float decay01)
        {
            const float MIN_CUT_HZ = 30f;
            const float MAX_CUT_HZ = 18000f;
            const float LN_CUT_RANGE = 6.397f;   // ln(MAX_CUT_HZ / MIN_CUT_HZ)
            const float TWO_PI = 2f * MathF.PI;

            float env = MathF.Max(0f, MathF.Min(1f, envValue));
            float dt = 1f / _sr;

            for (int i = 0; i < n; i++)
            {
                // Vactrol state tracks env with asymmetric time constants.
                float coef = (env > _vactrolState) ? _attCoef : _decCoef;
                _vactrolState += (env - _vactrolState) * coef;

                // VCA gain — strike scaled by vactrol state.
                float vca = _strikeLevel * _vactrolState;

                // VCF cutoff — vactrol-modulated when response=0, held at
                // maximum when response=1. Exponential mapping for musical
                // sweep across the 30 Hz – 18 kHz range.
                float cutoffDrive = response + (1f - response) * _vactrolState;
                float cutHz = MIN_CUT_HZ * MathF.Exp(LN_CUT_RANGE * cutoffDrive);
                float rc = 1f / (TWO_PI * cutHz);
                float a  = dt / (rc + dt);

                // OUT path
                float xL = outBuf[i];
                _yL += a * (xL - _yL);
                outBuf[i] = _yL * vca;

                // AUX path (same LPG; AUX is "variant of main", same gating behavior)
                float xR = auxBuf[i];
                _yR += a * (xR - _yR);
                auxBuf[i] = _yR * vca;
            }
        }
    }
}
