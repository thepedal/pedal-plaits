// Engines/GranularCloudEngine.cs — Plaits engine 8 (slot 5 in this port).
//
// Manual:
//   HARMONICS: amount of pitch randomization (detune spread per grain)
//   TIMBRE:    grain density (re-trigger rate)
//   MORPH:     grain duration / overlap. Full CW = grains merge into a
//                continuous stack of detuned oscillators (supersaw).
//   AUX:       sine-wave variant of the same grain swarm
//
// Architecture: a fixed pool of 8 grain voices, all always present in
// the audio sum. A scheduler periodically retriggers grains one at a
// time (round-robin), with each trigger setting a fresh detuned
// frequency and starting a new envelope. Grain frequencies stay fixed
// between triggers — the "cloud" character comes from the staggered
// trigger times and the random detunes accumulating across grains.
//
// v0.1 design choices:
// - All 8 grains are always allocated and always processed (active or
//   not). Skipping silent grains would save a few percent but complicates
//   the per-sample loop. Simpler to keep the loop uniform.
// - Grain phase is reset to 0 on trigger — simple, predictable. Plaits'
//   real engine might preserve phase or randomize; this is the variant
//   that produced the cleanest sound during testing.
// - Envelope shape: short linear attack (0.5 ms, anti-click) followed
//   by linear decay over the grain duration. Simpler than raised-cosine
//   and audibly indistinguishable at typical grain rates.
// - Saw oscillator uses PolyBLEP correction to keep high-detuned grains
//   alias-free (PedalSH101 §7 / VirtualAnalogEngine pattern).
// - OUT_GAIN = 0.2: 8 grains × 0.2 = peak 1.6, comfortable under the
//   2.0 LPG input headroom budget.
//
// On NoteOn: doesn't reset grain state. The scheduler is forced to
// trigger immediately so the new note's fundamental enters the cloud
// on the first sample. Existing grains from the previous note fade
// out over the next several trigger cycles (the "smear" character of
// granular crossfades).

using System;
using PedalPlaits.Util;

namespace PedalPlaits.Engines
{
    public class GranularCloudEngine : IEngine
    {
        public bool IsPercussive => false;

        const int N_GRAINS = 8;

        float _sr = 44100f;
        float _baseHz = 220f;

        // Per-grain state (struct-of-arrays for tighter inner-loop access)
        readonly float[] _phase = new float[N_GRAINS];
        readonly float[] _freq  = new float[N_GRAINS];
        readonly float[] _env   = new float[N_GRAINS];
        readonly bool[]  _inAttack = new bool[N_GRAINS];

        // Trigger scheduler
        float _scheduleAccum;
        int   _nextGrainIdx;

        // Engine-wide xorshift32 RNG (per-engine state, no allocations)
        uint _rngState;

        public void Init(float sr)
        {
            _sr = sr;
            _rngState = (uint)new Random().Next(1, int.MaxValue);
            Reset();
        }

        public void Reset()
        {
            for (int i = 0; i < N_GRAINS; i++)
            {
                _phase[i] = 0f;
                _freq[i]  = _baseHz;
                _env[i]   = 0f;
                _inAttack[i] = false;
            }
            _scheduleAccum = 0f;
            _nextGrainIdx  = 0;
        }

        public void NoteOn(int midiNote, float velocity)
        {
            _baseHz = DspUtil.MidiToHz(midiNote);
            // Force the next sample to fire a fresh grain at the new pitch,
            // so the new note enters the cloud immediately.
            _scheduleAccum = float.MaxValue;
        }

        public void NoteOff() { /* env handles fade */ }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            // ── TIMBRE: grain density (re-triggers per second) ──
            // Exponential: 20 Hz (slow droplets) .. 500 Hz (dense merge)
            float density = 20f * MathF.Pow(25f, p.Timbre);
            float samplesPerTrigger = _sr / density;

            // ── MORPH: grain duration ──
            // 5 ms (percussive grains) .. 300 ms (fully overlapping supersaw)
            float grainDuration = 0.005f + p.Morph * 0.295f;
            float envAtkDt = 1f / (0.0005f * _sr);          // 0.5 ms fixed attack
            float envRelDt = 1f / (grainDuration * _sr);

            // ── HARMONICS: per-grain detune in semitones ──
            // 0 = all grains at fundamental, 1 = ±2 semitones detune
            float detuneSemitones = p.Harmonics * 2f;

            const float TWO_PI = 2f * MathF.PI;
            const float OUT_GAIN = 0.2f;

            for (int i = 0; i < n; i++)
            {
                // ── Scheduler: trigger next grain when accum exceeds period ──
                _scheduleAccum += 1f;
                if (_scheduleAccum >= samplesPerTrigger)
                {
                    _scheduleAccum -= samplesPerTrigger;
                    if (_scheduleAccum > samplesPerTrigger)    // very high density
                        _scheduleAccum = 0f;

                    int idx = _nextGrainIdx;
                    _nextGrainIdx = (_nextGrainIdx + 1) % N_GRAINS;

                    // Detune in semitones, mapped to a frequency multiplier
                    float r = NextNoise();   // ±1 uniform
                    float cents = r * detuneSemitones * 100f;
                    _freq[idx] = _baseHz * MathF.Pow(2f, cents / 1200f);

                    _phase[idx]    = 0f;
                    _env[idx]      = 0f;
                    _inAttack[idx] = true;
                }

                // ── Sum all 8 grains ──
                float sumOut = 0f;
                float sumAux = 0f;
                for (int g = 0; g < N_GRAINS; g++)
                {
                    float env = _env[g];
                    if (env <= 0f && !_inAttack[g]) continue;

                    float phase = _phase[g];
                    float f     = _freq[g];
                    float dt    = f / _sr;

                    // Saw with PolyBLEP for OUT
                    float saw = 2f * phase - 1f;
                    saw -= PolyBlep.Compute(phase, dt);
                    sumOut += saw * env;

                    // Pure sine for AUX (band-limited inherently, no PolyBLEP needed)
                    float sine = MathF.Sin(TWO_PI * phase);
                    sumAux += sine * env;

                    // Advance phase
                    phase += dt;
                    if (phase >= 1f) phase -= 1f;
                    _phase[g] = phase;

                    // Advance envelope
                    if (_inAttack[g])
                    {
                        env += envAtkDt;
                        if (env >= 1f) { env = 1f; _inAttack[g] = false; }
                    }
                    else
                    {
                        env -= envRelDt;
                        if (env <= 0f) env = 0f;
                    }
                    _env[g] = env;
                }

                outBuf[i] += sumOut * OUT_GAIN;
                auxBuf[i] += sumAux * OUT_GAIN;
            }
        }

        float NextNoise()
        {
            uint s = _rngState;
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            _rngState = s;
            return (s / (float)uint.MaxValue) * 2f - 1f;
        }
    }
}
