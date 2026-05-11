// Lpg.cs — Low-Pass Gate.
//
// Plaits' LPG is the classic "vactrol" model: a strike triggers an envelope
// that controls both filter cutoff and amplitude. The Response parameter
// blends from VCFA mode (filter contributes most of the gating character,
// quasi-Buchla "thwack") at 0 to pure VCA (amplitude only, no filter
// contribution) at 1.
//
// This v0.1 implementation:
//   - One-pole lowpass per channel (cheap, characterful enough for skeleton)
//   - Filter cutoff and amplitude both modulated by the same env value
//   - Response blends "filter+amp gain" → "amp gain only"
//
// A full vactrol model with diode-curve nonlinearity is a v2 upgrade —
// document and revisit when the engine ports are stable.

using System;

namespace PedalPlaits
{
    public class Lpg
    {
        float _sr = 44100f;

        // Strike level — the latest velocity-driven amplitude. Held constant
        // until the next Strike(); env handles all time-decay. Earlier v0.1
        // had this auto-decaying per Process() call which dominated the audible
        // tail length and prevented the LPG filter sweep from ever being heard.
        float _strikeLevel;

        // One-pole filter state per channel
        float _yL, _yR;

        public void Init(float sr)
        {
            _sr = sr;
            _yL = _yR = 0f;
            _strikeLevel = 0f;
        }

        public void Strike(float velocity)
        {
            // Overwrite, don't max-clamp — a soft strike after a hard one
            // should actually be softer, not stay at the previous peak.
            _strikeLevel = velocity <= 0f ? 1f : velocity;
        }

        // Process in-place. envValue 0..1 is the current state of the
        // internal decay envelope. response 0 = full VCFA, 1 = pure VCA.
        // decay01 is reused to set a max LPG cutoff range.
        public void Process(float[] outBuf, float[] auxBuf, int n,
                             float envValue, float response, float decay01)
        {
            // VCA gain = strikeLevel * envValue (always)
            // VCF cutoff varies with envValue when in VCFA mode
            //   minCutHz at env=0, maxCutHz at env=1 (envelope opens filter)
            // response blends: at response=1 we set cutoff to maxCutHz
            //   regardless of env (filter is "always open" → pure VCA)
            const float MIN_CUT_HZ = 30f;
            const float MAX_CUT_HZ = 18000f;

            float env = MathF.Max(0f, MathF.Min(1f, envValue));
            float vca = _strikeLevel * env;

            // Cutoff: env-modulated when response=0, constant max when response=1
            float envForCutoff = response + (1f - response) * env;   // lerp
            float cutHz = MIN_CUT_HZ * MathF.Pow(MAX_CUT_HZ / MIN_CUT_HZ, envForCutoff);
            float dt = 1f / _sr;
            float rc = 1f / (2f * MathF.PI * cutHz);
            float a  = dt / (rc + dt);   // one-pole coefficient

            for (int i = 0; i < n; i++)
            {
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
