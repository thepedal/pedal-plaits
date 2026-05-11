// DecayEnv.cs — internal decaying envelope.
//
// Plaits has a single internal envelope generator triggered by the TRIG
// input. The decay time is shared with the LPG ringing (one knob in HW).
// This implements a simple exponential decay: trigger sets value to 1.0,
// each sample multiplies by `coef`. Coef is computed from Decay parameter
// and sample rate.
//
// We keep a per-block ProcessBlock(n) helper for control-rate use in Voice
// (LPG strike strength = current env value, advanced once per block).

using System;

namespace PedalPlaits
{
    public class DecayEnv
    {
        float _sr = 44100f;
        float _value;
        float _coef = 0.999f;

        public float Value => _value;

        public void Init(float sr) => _sr = sr;

        public void Trigger(float velocity, float decay01)
        {
            _value = velocity > 0f ? velocity : 1f;
            _coef = ComputeCoef(decay01);
        }

        // Advance n samples and return the value AFTER advancing.
        // Voice uses this to drive the LPG strike level at block rate.
        public float ProcessBlock(int n, float decay01)
        {
            _coef = ComputeCoef(decay01);
            // Vector-friendly: (coef^n) — a single Pow per block.
            _value *= MathF.Pow(_coef, n);
            return _value;
        }

        // Per-sample variant if you need it.
        public float ProcessSample(float decay01)
        {
            _coef = ComputeCoef(decay01);
            _value *= _coef;
            return _value;
        }

        // Decay 0..1 maps to time-constant in the range ~5 ms .. ~5 s.
        // Exponential so the knob feels natural.
        float ComputeCoef(float decay01)
        {
            if (decay01 < 0f) decay01 = 0f;
            if (decay01 > 1f) decay01 = 1f;
            // tau in seconds
            float tau = 0.005f * MathF.Pow(1000f, decay01);   // 0.005 .. 5
            // exp(-1/(tau*sr)) per sample
            return MathF.Exp(-1f / (tau * _sr));
        }
    }
}
