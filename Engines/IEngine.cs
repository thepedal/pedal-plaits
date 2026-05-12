// Engines/IEngine.cs — common contract for all 12 synthesis engines.
//
// Lifecycle:
//   Init(sr)       — once at machine construction
//   Reset()        — zero state (called when engine is selected)
//   NoteOn(midi,v) — pitch latch + (for percussive engines) trigger
//   NoteOff()      — release (no-op for percussive engines)
//   Render(out, aux, n, params)
//                  — render n samples (n ≤ BLOCK_SIZE) into the
//                    pre-cleared buffers; ADD, don't overwrite, so
//                    Voice could later do per-voice mixing if poly.
//
// IsPercussive controls LPG routing in Voice:
//   false → engine output goes through the LPG (pitched models)
//   true  → engine output bypasses LPG (drums + modal — they have
//           their own internal envelope/filter, per Plaits manual).

namespace PedalPlaits.Engines
{
    public struct EngineParams
    {
        public float Harmonics;     // 0..1
        public float Timbre;        // 0..1
        public float Morph;         // 0..1
        public float Decay;         // 0..1 (engine's own decay if it has one)
        public float LpgResponse;   // 0..1 (Voice uses this; engines can ignore)
    }

    public interface IEngine
    {
        void Init(float samplesPerSec);
        void Reset();
        void NoteOn(int midiNote, float velocity);
        void NoteOff();
        void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p);

        /// True for engines that have their own internal envelope/filter
        /// (drums, modal). Voice will not apply the LPG to these.
        bool IsPercussive { get; }

        /// Percussive engines only — true when the internal envelope has
        /// decayed and there's no more audio to produce. Voice uses this
        /// to clear IsActive so ReBuzz can stop calling Work().
        /// Default false: pitched engines are managed by Voice's env, not
        /// by self-report, so they never need to override this.
        bool IsSilent => false;
    }
}
