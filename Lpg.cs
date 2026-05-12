// Lpg.cs — Low-Pass Gate with vactrol modeling (v1.4 + v1.5 LDR curve).
//
// Plaits' LPG is the classic "vactrol" model: a strike triggers an envelope
// that controls both filter cutoff and amplitude through a vactrol (LED-LDR
// pair). The vactrol's thermal lag — fast but non-instant LED rise, slower
// LDR cool-down — gives the gate a characteristic "thwack" attack and
// gentle release that defines its sound.
//
// v1.4 added per-sample vactrol-state tracking with separate attack (5 ms)
// and release (30 ms) time constants applied uniformly to BOTH the VCA gain
// AND the VCF cutoff (since a real vactrol drives both via one physical
// resistance change).
//
// v1.5 adds a power-law LDR transfer curve between the smoothed exposure
// state and the audio-path response. A real LDR's gain-vs-illumination
// curve is non-linear — gain rises slowly during initial exposure (thermal
// lag region) then accelerates. The power LDR_POWER = 1.4 compresses low
// state values relative to high values, producing a soft-knee feel on hard
// strikes ("ducking" before the full peak) that linear scaling misses.
//
// Side effects unchanged from v1.4: perceived decay times lengthen by ~30
// ms compared to v1.3 builds because the vactrol's release adds to the
// DecayEnv shape.
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
        // response 0 = full VCFA, 1 = pure VCA. decay01 unused in v1.4+.
        public void Process(float[] outBuf, float[] auxBuf, int n,
                             float envValue, float response, float decay01)
        {
            const float MIN_CUT_HZ = 30f;
            const float LN_CUT_RANGE = 6.397f;   // ln(18000 / 30) — top cutoff 18 kHz
            const float TWO_PI = 2f * MathF.PI;
            // v1.5 LDR transfer curve. >1 compresses low states (soft-knee
            // "ducking" feel on hard strikes), <1 expands them (sharper
            // attack response). 1.4 is a moderate compression matching
            // typical vactrol audio-path measurements.
            const float LDR_POWER = 1.4f;

            float env = MathF.Max(0f, MathF.Min(1f, envValue));
            float dt = 1f / _sr;

            for (int i = 0; i < n; i++)
            {
                // Vactrol state tracks env with asymmetric time constants.
                float coef = (env > _vactrolState) ? _attCoef : _decCoef;
                _vactrolState += (env - _vactrolState) * coef;

                // LDR non-linearity — map exposure state to physical
                // audio-path response. Shared by VCA and VCF since both
                // are driven by the same vactrol resistance.
                float physicalState = MathF.Pow(_vactrolState, LDR_POWER);

                // VCA gain — strike scaled by physical (post-LDR) state.
                float vca = _strikeLevel * physicalState;

                // VCF cutoff — physical-state-modulated when response=0,
                // held at maximum when response=1. Exponential mapping for
                // musical sweep across the 30 Hz – 18 kHz range.
                float cutoffDrive = response + (1f - response) * physicalState;
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
