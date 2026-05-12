// Engines/InharmonicStringEngine.cs — Plaits engine 11 (extended Karplus-Strong / inharmonic string).
//
// Manual:
//   HARMONICS: structure / inharmonicity — stiffness coefficient of an
//              in-loop allpass that pushes higher partials sharp. 0 =
//              perfectly harmonic (clean string), 127 = clangorous,
//              glass/metal territory (piano-stretch through bell-like
//              at the top). Quadratic mapping for finer low-end.
//   TIMBRE:    excitation brightness — cutoff of a one-pole LP applied
//              to the noise-burst exciter before it enters the loop.
//              0 = mellow (deep wooden pluck), 127 = bright (sharp
//              percussive attack).
//   MORPH:     decay time / energy absorption — feedback gain set so
//              T60 is independent of pitch. Swept exponentially from
//              ~50 ms (very damped pluck) to ~5 s (long sustained
//              ring).
//   AUX:       raw exciter signal — the windowed noise burst before
//              the loop. Short impulse-like texture for layering with
//              the sustained OUT.
//
// Architecture (v1.11): single-string Karplus-Strong delay loop with
//   (1) a first-order allpass for fractional-sample delay (sample-precise
//       pitch independent of integer sample count, important above
//       ~MIDI 80 where integer rounding becomes audibly off-pitch),
//   (2) a stiffness allpass introducing frequency-dependent phase shift
//       that pushes upper partials sharp — the "extended" part of
//       extended Karplus-Strong, controlling inharmonicity,
//   (3) a one-pole LP closing the loop that gives the basic damping
//       character (high frequencies decay faster than fundamentals),
//   (4) a feedback gain `g` set per-buffer from L = sr / freq so the
//       target T60 is pitch-independent: g = exp(-ln(1000) · L / (T60·sr)).
//
// Exciter reuses the same Hann-windowed noise burst the modal engine
// uses (96 samples ~ 2 ms at 48 kHz), LP-filtered before injection so
// TIMBRE controls how bright the strike sounds. NoteOn rearms the
// exciter without clearing the delay line, so retriggers overlap
// previous-note decay with fresh excitation — idiomatic Karplus-Strong.
//
// IsPercussive=true → bypasses the LPG (the delay loop owns the decay
// envelope per Plaits' design for this engine). Global Decay parameter
// is ignored; MORPH is the decay control.

using System;
using PedalPlaits.Util;

namespace PedalPlaits.Engines
{
    public class InharmonicStringEngine : IEngine
    {
        public bool IsPercussive => true;
        public bool IsSilent => _exciterPhase >= EXCITER_LEN && _peakState < SILENCE_THRESHOLD;

        // 8192 samples = ~6 Hz minimum at 48 kHz; covers the full Frequency
        // parameter range plus user note column transposition. Power-of-two
        // for clean modulo-by-mask if the hot loop ever needs squeezing.
        const int   MAX_DELAY        = 8192;
        const int   EXCITER_LEN      = 96;         // ~2 ms at 48 kHz
        const float SILENCE_THRESHOLD = 1e-5f;
        const float DENORMAL_FLUSH    = 1e-25f;

        float _sr     = 44100f;
        float _baseHz = 220f;

        // Delay line + write head
        readonly float[] _delayLine = new float[MAX_DELAY];
        int _writePos;

        // In-loop filter state. Each first-order allpass needs the previous
        // input AND the previous output; the damping LP needs only its own
        // previous output.
        float _fracInPrev,   _fracOutPrev;
        float _stiffInPrev,  _stiffOutPrev;
        float _dampingState;

        // Exciter state
        int   _exciterPhase = EXCITER_LEN;  // start "done"
        float _strikeAmp    = 1f;
        float _exciterLpState;
        uint  _rngState;

        // Tracks peak loop output from the last Render for IsSilent
        float _peakState;

        public void Init(float sr)
        {
            _sr = sr;
            _rngState = (uint)new Random().Next(1, int.MaxValue);
            Reset();
        }

        public void Reset()
        {
            Array.Clear(_delayLine, 0, MAX_DELAY);
            _writePos        = 0;
            _fracInPrev      = 0f;
            _fracOutPrev     = 0f;
            _stiffInPrev     = 0f;
            _stiffOutPrev    = 0f;
            _dampingState    = 0f;
            _exciterLpState  = 0f;
            _exciterPhase    = EXCITER_LEN;
            _peakState       = 0f;
        }

        public void NoteOn(int midiNote, float velocity)
        {
            _baseHz = DspUtil.MidiToHz(midiNote);
            // Rearm exciter without clearing delay line — retriggers overlap
            // the previous note's decay (idiomatic Karplus-Strong behaviour
            // and a major part of the engine's "alive" character).
            _exciterPhase = 0;
            _strikeAmp    = velocity <= 0f ? 1f : velocity;
        }

        public void NoteOff() { /* loop decay handles itself */ }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            // ── Per-buffer coefficient computation ──
            float harm  = DspUtil.Clamp01(p.Harmonics);
            float timb  = DspUtil.Clamp01(p.Timbre);
            float morph = DspUtil.Clamp01(p.Morph);

            // Delay length L = sr / freq, split into integer + fractional.
            // Floor at 4 to leave room for filter pre-roll past the read
            // position; ceiling at MAX_DELAY - 4 for the same reason.
            float Lf = _sr / MathF.Max(1f, _baseHz);
            if (Lf > MAX_DELAY - 4) Lf = MAX_DELAY - 4;
            if (Lf < 4f)            Lf = 4f;
            int   L_int  = (int)Lf;
            float L_frac = Lf - L_int;

            // Fractional-delay allpass coefficient (first-order, group-delay
            // flat at DC for fractional delay L_frac ∈ [0, 1)).
            float fracCoef = (1f - L_frac) / (1f + L_frac);

            // Stiffness allpass coefficient — HARMONICS → c ∈ [0, 0.5].
            // Quadratic so the lower half of the knob stays in mild-stretch
            // territory (piano-string range) and the upper half opens up to
            // clangorous bell/glass spectra.
            float stiffCoef = harm * harm * 0.5f;

            // Damping LP fixed coefficient — moderate rolloff so the loop
            // preserves enough harmonic content for the fundamental to ring
            // out, but high partials lose energy faster than low ones.
            // Macro decay length is controlled by the feedback gain below.
            const float DAMPING_COEF = 0.85f;

            // Feedback gain `g` chosen so target T60 is pitch-independent:
            //   g = exp(-ln(1000) · L / (T60 · sr))
            // Without this correction high notes (short L) would decay
            // dramatically faster than low notes for the same MORPH setting.
            float T60   = 0.05f * MathF.Pow(100f, morph);   // 50 ms → 5 s
            float fbGain = MathF.Exp(-MathF.Log(1000f) * Lf / (T60 * _sr));
            if (fbGain > 0.9999f) fbGain = 0.9999f;

            // Exciter LP cutoff — TIMBRE 0..1 → 200 Hz to Nyquist (exponential).
            // One-pole LP attenuates white-noise RMS by √(α/(2-α)); without
            // compensation, dark TIMBRE settings (low cutoff) produced burst
            // levels several times quieter than bright settings, and the
            // loop just circulates whatever energy is injected. Multiplying
            // by √((2-α)/α) cancels the LP's RMS attenuation so loudness
            // stays consistent across TIMBRE — only the spectral content of
            // the burst changes, which is what TIMBRE is supposed to control.
            float cutoffHz = 200f * MathF.Pow(_sr * 0.5f / 200f, timb);
            float exLpCoef = 1f - MathF.Exp(-2f * MathF.PI * cutoffHz / _sr);
            float exGain   = MathF.Sqrt((2f - exLpCoef) / exLpCoef);

            const float OUT_GAIN = 0.6f;
            const float AUX_GAIN = 0.5f;
            float peak = 0f;

            for (int i = 0; i < n; i++)
            {
                // ── 1. Exciter sample: Hann-windowed white noise burst ──
                float exciterRaw = 0f;
                if (_exciterPhase < EXCITER_LEN)
                {
                    float wPhase = (float)_exciterPhase / (EXCITER_LEN - 1);
                    float window = 0.5f * (1f - MathF.Cos(2f * MathF.PI * wPhase));
                    exciterRaw = NoiseSample() * window * _strikeAmp;
                    _exciterPhase++;
                }
                // TIMBRE-controlled LP for brightness, with RMS-flat
                // compensation so the burst's loudness doesn't fall off
                // with darker TIMBRE settings.
                _exciterLpState += (exciterRaw - _exciterLpState) * exLpCoef;
                float exciter = _exciterLpState * exGain;

                // ── 2. Read delay line at integer offset ──
                int readPos = _writePos - L_int;
                if (readPos < 0) readPos += MAX_DELAY;
                float delayed = _delayLine[readPos];

                // ── 3. Fractional-delay allpass ──
                //   y[n] = c·x[n] + x[n-1] - c·y[n-1]
                float fracOut = fracCoef * delayed + _fracInPrev - fracCoef * _fracOutPrev;
                _fracInPrev  = delayed;
                _fracOutPrev = fracOut;

                // ── 4. Stiffness allpass (same form, different coefficient) ──
                float stiffOut = stiffCoef * fracOut + _stiffInPrev - stiffCoef * _stiffOutPrev;
                _stiffInPrev  = fracOut;
                _stiffOutPrev = stiffOut;

                // ── 5. Damping LP (one-pole, fixed coefficient) ──
                _dampingState += (stiffOut - _dampingState) * DAMPING_COEF;
                float damped = _dampingState;

                // ── 6. Apply feedback gain (controls macro decay) ──
                float loopOut = damped * fbGain;

                // ── 7. Write back into delay line: exciter + loop ──
                float toWrite = exciter + loopOut;
                // Denormal flush (Core §30) — the loop can drift into
                // sub-denormal territory once amplitude is well below
                // audibility, and CPU stalls if those propagate.
                if (toWrite > -DENORMAL_FLUSH && toWrite < DENORMAL_FLUSH) toWrite = 0f;
                _delayLine[_writePos] = toWrite;

                _writePos++;
                if (_writePos >= MAX_DELAY) _writePos -= MAX_DELAY;

                // ── 8. Outputs ──
                outBuf[i] += loopOut * OUT_GAIN;
                auxBuf[i] += exciter * AUX_GAIN;

                float absLoop = MathF.Abs(loopOut);
                if (absLoop > peak) peak = absLoop;
            }

            _peakState = peak;
        }

        // xorshift32 — same idiom as ModalResonatorEngine / FilteredNoiseEngine.
        // Returns ±1 uniform.
        float NoiseSample()
        {
            uint x = _rngState;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _rngState = x;
            return (x * (2f / uint.MaxValue)) - 1f;
        }
    }
}
