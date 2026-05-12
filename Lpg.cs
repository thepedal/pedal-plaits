// Lpg.cs — Low-Pass Gate with two-stage vactrol modeling.
//
// Plaits' LPG is the classic "vactrol" model: a strike triggers an envelope
// that controls both filter cutoff and amplitude through a vactrol (LED-LDR
// pair). The vactrol's thermal lag — fast but non-instant LED rise, slower
// LDR cool-down — gives the gate a characteristic "thwack" attack and
// gentle release that defines its sound.
//
// v1.4: per-sample vactrol-state tracking with separate attack (5 ms)
//   and release (30 ms) time constants applied uniformly to BOTH the VCA
//   gain AND the VCF cutoff (since a real vactrol drives both via one
//   physical resistance change).
//
// v1.5: power-law LDR transfer curve between the smoothed exposure
//   state and the audio-path response. A real LDR's gain-vs-illumination
//   curve is non-linear — gain rises slowly during initial exposure
//   (thermal lag region) then accelerates.
//
// v1.8: two-stage cascade replacing the v1.4 single-stage follower.
//   Models the physical vactrol more accurately as
//     input → LED brightness → LDR resistance → output
//   where the LED stage is fast and symmetric (electrical response of
//   the diode), and the LDR stage is slow and asymmetric (thermal
//   response of the photoresistor — heats up quicker than it cools
//   down). The cascade produces a subtly different transient shape on
//   hard strikes: stage 1 follows the input attack ramp tightly, then
//   stage 2 lags slightly behind, giving a brief "ducking" character
//   on the leading edge that single-stage smoothing misses. Slow
//   sustains and releases are dominated by stage 2 and sound nearly
//   identical to the v1.4-v1.7 model.
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

        // v1.8 two-stage vactrol model. Stage 1 represents the LED's
        // electrical response (fast, symmetric); stage 2 represents the
        // LDR's thermal response (slower, asymmetric attack/release).
        // Continuous across strikes so rapid retriggers produce a smooth
        // envelope shape rather than discontinuities.
        float _stage1State;        // LED brightness
        float _stage2State;        // LDR resistance proxy
        float _stage1Coef;         // LED RC (symmetric)
        float _stage2AttCoef;      // LDR attack
        float _stage2RelCoef;      // LDR release

        public void Init(float sr)
        {
            _sr = sr;
            _yL = _yR = 0f;
            _strikeLevel = 0f;
            _stage1State = 0f;
            _stage2State = 0f;
            UpdateVactrolCoefs();
        }

        void UpdateVactrolCoefs()
        {
            // Stage 1 (LED) — 1.5 ms, symmetric. Real LED rise/fall is
            // sub-millisecond electrically; 1.5 ms approximates the
            // combined RC of the LED drive circuit and a small filter cap.
            const float STAGE1_TAU     = 0.0015f;
            // Stage 2 (LDR) — asymmetric thermal response. Cadmium-sulphide
            // photoresistors (the Vactec VTL5C family Buchla used) drop
            // resistance fairly quickly when illuminated (~5 ms) but rise
            // back to dark resistance over tens of milliseconds.
            const float STAGE2_ATT_TAU = 0.005f;
            const float STAGE2_REL_TAU = 0.035f;

            _stage1Coef    = 1f - MathF.Exp(-1f / (STAGE1_TAU     * _sr));
            _stage2AttCoef = 1f - MathF.Exp(-1f / (STAGE2_ATT_TAU * _sr));
            _stage2RelCoef = 1f - MathF.Exp(-1f / (STAGE2_REL_TAU * _sr));
        }

        public void Strike(float velocity)
        {
            // Overwrite, don't max-clamp — a soft strike after a hard one
            // should actually be softer, not stay at the previous peak.
            _strikeLevel = velocity <= 0f ? 1f : velocity;
        }

        // Process in-place. envValue 0..1 is the current state of the
        // internal decay envelope (treated as the vactrol's drive signal).
        // response 0 = full VCFA, 1 = pure VCA. decay01 unused.
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
                // Stage 1 — LED brightness follows the input drive
                // signal (fast, symmetric one-pole).
                _stage1State += (env - _stage1State) * _stage1Coef;

                // Stage 2 — LDR resistance follows LED brightness
                // (asymmetric: faster attack, slower release).
                float coef2 = (_stage1State > _stage2State) ? _stage2AttCoef : _stage2RelCoef;
                _stage2State += (_stage1State - _stage2State) * coef2;

                // LDR non-linearity — map exposure state to physical
                // audio-path response. Shared by VCA and VCF since both
                // are driven by the same vactrol resistance.
                float physicalState = MathF.Pow(_stage2State, LDR_POWER);

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
