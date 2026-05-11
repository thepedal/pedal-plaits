// Util/DspUtil.cs — small DSP helpers used by engines.

using System;

namespace PedalPlaits.Util
{
    public static class DspUtil
    {
        // MIDI 69 (A4) = 440 Hz reference. Plaits uses A4 = 440 by default.
        public static float MidiToHz(float midi)
        {
            return 440f * MathF.Pow(2f, (midi - 69f) / 12f);
        }

        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public static float Clamp01(float x)
        {
            if (x < 0f) return 0f;
            if (x > 1f) return 1f;
            return x;
        }

        public static float Clamp(float x, float lo, float hi)
        {
            if (x < lo) return lo;
            if (x > hi) return hi;
            return x;
        }
    }
}
