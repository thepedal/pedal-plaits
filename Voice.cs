// Voice.cs — single-voice orchestrator.
//
// Owns:
//   - The 12 engines (constructed up front; switching just changes index)
//   - The LPG (low-pass gate, applied to pitched engines)
//   - The internal decay envelope (drives the LPG strike)
//
// Render flow per BLOCK:
//   engine.Render(rawOut, rawAux, n, params)
//   if !engine.IsPercussive:
//       lpg.Process(rawOut, rawAux, env, response, decay)
//   (Voice writes results back to scratch buffers in-place.)
//
// Plaits behavior: percussive engines (drums, modal) bypass the LPG —
// they have their own internal envelope. The LPG is only meaningful
// for the pitched/synth models.

using PedalPlaits.Engines;

namespace PedalPlaits
{
    public class Voice
    {
        readonly IEngine[] _engines;
        readonly Lpg       _lpg = new Lpg();
        readonly DecayEnv  _env = new DecayEnv();

        int   _currentEngine = 0;
        bool  _active;   // true while anything is producing audio
        bool  _forceFade; // transport-stop override (Core §27)

        public int  CurrentEngineIndex => _currentEngine;
        public bool IsActive => _active;

        public Voice()
        {
            // Order matches Pedalplaits.ENGINES — keep aligned.
            _engines = new IEngine[]
            {
                new VirtualAnalogEngine(),  // 0
                new WaveshapingEngine(),    // 1
                new FmEngine(),             // 2
                new HarmonicEngine(),       // 3
                new WavetableEngine(),      // 4
                new GranularCloudEngine(),  // 5
                new FilteredNoiseEngine(),  // 6
                new BassDrumEngine(),       // 7
                new SnareDrumEngine(),      // 8
                new HiHatEngine(),          // 9
                new ModalResonatorEngine(), // 10
                new InharmonicStringEngine(),// 11
            };
        }

        public void Init(float sr)
        {
            _lpg.Init(sr);
            _env.Init(sr);
            for (int i = 0; i < _engines.Length; i++) _engines[i].Init(sr);
        }

        public void SetEngine(int index)
        {
            if (index < 0 || index >= _engines.Length) return;
            if (index == _currentEngine) return;
            _currentEngine = index;
            _engines[_currentEngine].Reset();
            // Don't reset LPG/env here — let any current note tail decay naturally
            // even if the user mid-note flips the engine selector.
        }

        public void NoteOn(int midiNote, float velocity, in EngineParams p)
        {
            _active = true;
            _forceFade = false;                     // any new note clears the stop-fade
            _engines[_currentEngine].NoteOn(midiNote, velocity);
            _lpg.Strike(velocity);                  // strike triggers ringing
            _env.Trigger(velocity, p.Decay);        // internal decay env
        }

        public void NoteOff()
        {
            // Plaits' internal envelope is fire-and-forget. NoteOff is a hint
            // for engines that care (none of the current set do at this layer);
            // env and LPG continue their natural decay regardless.
            _engines[_currentEngine].NoteOff();
        }

        /// Transport-stop override — Core §27. ~5 ms forced fade overriding
        /// the user's Decay setting so a Stop while sustained doesn't ring on.
        public void ForceFade()
        {
            _forceFade = true;
        }

        public void Render(float[] outBuf, float[] auxBuf, int n, in EngineParams p)
        {
            // Early-exit when voice is inactive — skip the engine's DSP entirely
            // rather than running it and discarding the output. Without this,
            // engines kept doing their full per-sample math forever after a note
            // had decayed to silent, which kept CPU pinned (especially noticeable
            // for percussive engines with expensive inner loops like BassDrum).
            if (!_active) return;

            var engine = _engines[_currentEngine];
            engine.Render(outBuf, auxBuf, n, in p);

            // Effective decay — forced-fade overrides to ~5ms tau
            float decay = _forceFade ? 0f : p.Decay;   // 0 maps to ~5 ms in DecayEnv

            if (!engine.IsPercussive)
            {
                float envValue = _env.ProcessBlock(n, decay);
                _lpg.Process(outBuf, auxBuf, n, envValue, p.LpgResponse, decay);

                // Fire-and-forget: voice goes inactive once env has decayed.
                if (_env.Value < 1e-4f) _active = false;
            }
            else
            {
                // Percussive engines own their envelope; voice stays active
                // until the engine reports silent.
                _active = !engine.IsSilent;
            }
        }
    }
}
