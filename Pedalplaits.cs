// Pedalplaits.cs — Pedal Plaits (Mutable Instruments Plaits port to ReBuzz)
//
// v0.1 skeleton — monophonic, 10 engines, 8 globals + Note/Velocity per track.
// Engine 0 (Virtual Analog) is implemented; engines 1-9 are stubs returning silence.
//
// Architecture:
//   ReBuzz Work() ──> Voice.Render(out, aux, n)
//                       │
//                       ├─ trigger pending? -> engine.Trigger() + LPG.Strike() + Env.Trigger()
//                       │
//                       ├─ chunk n into BLOCK_SIZE (12) blocks (Plaits control rate)
//                       │     for each block:
//                       │       engine.Render(rawOut, rawAux, blockSize, params)
//                       │       if !engine.IsPercussive: Lpg.Process(rawOut, rawAux, env)
//                       │       accumulate into out/aux
//                       │
//                       └─ apply Volume, scale to ±32768 (PedalComp §1)
//
// Source: Mutable Instruments Plaits firmware (MIT) — pichenettes/eurorack/plaits.

using System;
using System.Collections.Generic;
using Buzz.MachineInterface;
using BuzzGUI.Interfaces;
using PedalPlaits.Engines;

namespace PedalPlaits
{
    [MachineDecl(
        Name        = "Pedal Plaits",
        ShortName   = "Plaits",
        Author      = "ReBuzz port — DSP after Mutable Instruments (MIT)",
        MaxTracks   = 1,
        InputCount  = 0,
        OutputCount = 2)]   // 0 = OUT, 1 = AUX
    public class Pedalplaits : IBuzzMachine
    {
        // ───── ReBuzz constants (Core notes) ─────
        const float SAMPLE_SCALE = 32768f;
        const int   BLOCK_SIZE   = 12;          // Plaits control-rate block

        readonly IBuzzMachineHost _host;
        Voice _voice;

        // Pending events — drained at top of each Work() (SH101 §6.3 pattern)
        bool _hasNoteOn;
        bool _hasNoteOff;
        int  _pendingMidiNote;
        float _velocity = 100f / 127f;   // sticky — last value retains until changed
        bool _wasPlaying;                // for transport-stop edge detection (Core §27)

        // Block-scratch buffers (allocated once, reused — no audio-thread allocation)
        readonly float[] _scratchOut = new float[BLOCK_SIZE];
        readonly float[] _scratchAux = new float[BLOCK_SIZE];

        public Pedalplaits(IBuzzMachineHost host)
        {
            _host  = host;
            _voice = new Voice();
        }

        // Tracks the sample rate Voice was last initialized at. 0 = never
        // initialized. Re-init Voice whenever MasterInfo reports a different
        // rate so engines pick up the change (otherwise pitch drifts when
        // the audio device switches from 48k to 44.1k mid-session, etc).
        float _lastSr;

        // ─────────────────────────────────────────────────────────
        // Global parameters (Group 1) — declaration order is preset order
        // (Build §3.2 — keep PARAM_INDEX in any preset generator aligned).
        // All MinValues ≥ 0 per PedalComp §2.
        // ─────────────────────────────────────────────────────────

        [ParameterDecl(
            Name = "Engine",
            Description = "Synthesis model",
            MinValue = 0, MaxValue = 9, DefValue = 0,
            ValueDescriptions = new[]
            {
                "Virtual Analog", "Waveshaping", "Two-op FM", "Harmonic",
                "Wavetable", "Granular Cloud", "Filtered Noise",
                "Bass Drum", "Snare Drum", "Hi-Hat",
            })]
        public int Engine { get; set; } = 0;

        // Frequency offset, semitones; 48 = no offset, 0 = -48st, 96 = +48st.
        [ParameterDecl(
            Name = "Frequency",
            Description = "Coarse pitch offset (semitones, 48 = unison)",
            MinValue = 0, MaxValue = 96, DefValue = 48)]
        public int Frequency { get; set; } = 48;

        [ParameterDecl(Name = "Harmonics", MinValue = 0, MaxValue = 127, DefValue = 64)]
        public int Harmonics { get; set; } = 64;

        [ParameterDecl(Name = "Timbre",    MinValue = 0, MaxValue = 127, DefValue = 64)]
        public int Timbre { get; set; } = 64;

        [ParameterDecl(Name = "Morph",     MinValue = 0, MaxValue = 127, DefValue = 64)]
        public int Morph { get; set; } = 64;

        // LPG response: 0 = full VCF (filter + amp), 127 = pure VCA (amp only).
        [ParameterDecl(
            Name = "LPG Response",
            Description = "0 = VCFA (filter+amp)  127 = pure VCA",
            MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int LpgResponse { get; set; } = 0;

        // Decay = LPG ringing time AND internal envelope decay (one knob in HW).
        [ParameterDecl(
            Name = "Decay",
            Description = "LPG ringing time + internal envelope decay",
            MinValue = 0, MaxValue = 127, DefValue = 32)]
        public int Decay { get; set; } = 32;

        [ParameterDecl(Name = "Volume",    MinValue = 0, MaxValue = 127, DefValue = 100)]
        public int Volume { get; set; } = 100;

        // ─────────────────────────────────────────────────────────
        // Track parameters (Group 2)
        // ─────────────────────────────────────────────────────────

        // Note: type = Note (Core §3 — managed machines use the Note struct).
        // Note.Off = 255, Note.ToMIDINote() handles the byte→MIDI conversion.
        // [ParameterDecl] goes on the SETTER METHOD for per-track params.
        [ParameterDecl(Name = "Note", IsStateless = true,
            Description = "Root note — z=C-4, s=C#-4 …")]
        public void SetNote(Note value, int track)
        {
            if (value.Value == Note.Off)
            {
                _hasNoteOff = true;
            }
            else if (value.Value != 0)
            {
                _pendingMidiNote = value.ToMIDINote();
                _hasNoteOn = true;
            }
        }

        // Velocity / Level — strikes the LPG, also accents drums (Plaits' LEVEL).
        // Per-track parameter: [ParameterDecl] on the setter method.
        // Velocity is sticky — last value retains until pattern changes it.
        [ParameterDecl(Name = "Velocity",
            MinValue = 0, MaxValue = 127, DefValue = 100)]
        public void SetVelocity(int value, int track)
        {
            _velocity = value / 127f;
        }

        // ─────────────────────────────────────────────────────────
        // Work — multi-out (PedalTracker §12.1: defining IList<Sample[]>
        // overload sets MULTI_IO automatically). Output 0 = OUT, 1 = AUX.
        // Unconnected outputs arrive as null and we skip them.
        // ─────────────────────────────────────────────────────────
        public bool Work(IList<Sample[]> output, int n, WorkModes mode)
        {
            // Sample-rate setup AND change detection. Re-init Voice whenever
            // the host's reported rate differs from what we last initialized
            // with. Catches both first-call (lastSr=0) and runtime rate
            // changes (user switches audio device, etc).
            float currentSr = _host?.MasterInfo?.SamplesPerSec ?? 44100f;
            if (MathF.Abs(currentSr - _lastSr) > 0.5f)
            {
                _voice.Init(currentSr);
                _lastSr = currentSr;
            }

            // Transport-stop edge detection (Core §27) — force-fade voices on
            // the Playing→Stopped edge so a long Decay tail doesn't ring on.
            bool nowPlaying = _wasPlaying;
            try { nowPlaying = _host?.Machine?.Graph?.Buzz?.Playing ?? false; }
            catch { /* keep previous — never break audio on a poll glitch */ }
            if (_wasPlaying && !nowPlaying) _voice.ForceFade();
            _wasPlaying = nowPlaying;

            Sample[] outBuf = output.Count > 0 ? output[0] : null;
            Sample[] auxBuf = output.Count > 1 ? output[1] : null;
            if (outBuf == null && auxBuf == null) return false;
            if (n <= 0) return false;

            // Drain pending events
            if (_hasNoteOn)
            {
                _hasNoteOn = false;
                _voice.SetEngine(Engine);
                int midi = _pendingMidiNote + (Frequency - 48);
                _voice.NoteOn(midi, _velocity, ParamsSnapshot());
            }
            if (_hasNoteOff)
            {
                _hasNoteOff = false;
                _voice.NoteOff();
            }

            // Engine switching mid-note: just update; new params take effect next block.
            if (_voice.CurrentEngineIndex != Engine)
                _voice.SetEngine(Engine);

            float volScale = (Volume / 127f) * SAMPLE_SCALE;

            // Render in BLOCK_SIZE chunks
            int i = 0;
            var p = ParamsSnapshot();
            while (i < n)
            {
                int blk = Math.Min(BLOCK_SIZE, n - i);
                Array.Clear(_scratchOut, 0, blk);
                Array.Clear(_scratchAux, 0, blk);

                _voice.Render(_scratchOut, _scratchAux, blk, in p);

                if (outBuf != null)
                    for (int k = 0; k < blk; k++)
                    {
                        float s = _scratchOut[k] * volScale;
                        outBuf[i + k] = new Sample(s, s);
                    }
                if (auxBuf != null)
                    for (int k = 0; k < blk; k++)
                    {
                        float s = _scratchAux[k] * volScale;
                        auxBuf[i + k] = new Sample(s, s);
                    }
                i += blk;
            }

            return _voice.IsActive;
        }

        EngineParams ParamsSnapshot()
        {
            return new EngineParams
            {
                Harmonics = Harmonics / 127f,
                Timbre    = Timbre    / 127f,
                Morph     = Morph     / 127f,
                Decay     = Decay     / 127f,
                LpgResponse = LpgResponse / 127f,
            };
        }

        // ─────────────────────────────────────────────────────────
        // Channel naming for the connection menu (PedalTracker §12.4)
        // ─────────────────────────────────────────────────────────
        public string GetChannelName(bool input, int index)
        {
            if (input) return null;
            return index switch
            {
                0 => "OUT",
                1 => "AUX",
                _ => null,
            };
        }
    }
}
