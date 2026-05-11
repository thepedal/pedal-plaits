// Util/PolyBlep.cs — PolyBLEP band-limited step correction.
//
// Same implementation pattern as PedalSH101 §7. Adds a polynomial
// correction near each waveform discontinuity, smoothing the step over
// ~2 samples and removing audible aliasing.
//
// dt = freq / samplerate (normalized phase increment per sample).

namespace PedalPlaits.Util
{
    public static class PolyBlep
    {
        public static float Compute(float t, float dt)
        {
            if (t < dt)
            {
                t /= dt;
                return t + t - t * t - 1f;
            }
            if (t > 1f - dt)
            {
                t = (t - 1f) / dt;
                return t * t + t + t + 1f;
            }
            return 0f;
        }
    }
}
