# Pedal Plaits

ReBuzz managed machine — port of Mutable Instruments' Plaits macro-oscillator.

**Status:** v1.12 — 13 engines, mono voice, OUT + AUX outputs, per-Work
parameter smoothing (v1.2) + velocity sensitivity routing (v1.3) +
Plaits-faithful wavetable banks 0/4 and 3/7 + hardsync-formants region
on engine 0 + vactrol-modeled LPG (v1.4) + AUX faithful rendering for
engine 0 + scaled-PolyBLEP hardsync AA + LDR non-linearity in vactrol
(v1.5) + Plaits bank_3 (Braids-derived) for wavetable banks 2/6 +
wrap-side notched-saw AA (v1.6) + integrated wavetable playback
(Franck-Valimaki K=1) + entry-side notched-saw AA (v1.7) + FM sideband
soft-clip AA + two-stage vactrol LPG cascade (v1.8) + Rings-style
modal resonator engine (v1.9), 110-preset factory bank organised by
engine (v1.10) + extended Karplus-Strong inharmonic string engine
(v1.11) + phase distortion / modulation engine (v1.12),
130-preset factory bank.

**Original source:** https://github.com/pichenettes/eurorack/tree/master/plaits
**License:** MIT — see the `LICENSE` file, which reproduces the MIT notice
and retains Émilie Gillet's original copyright as MIT requires (the Plaits
firmware is MIT-licensed; this port preserves that license). See also Credits.

---

## Overview

Pedal Plaits provides 10 monophonic synthesis engines in a single machine
slot, ranging from virtual analog and FM through granular and wavetable
to a complete electronic-drum-machine voice (kick, snare, hi-hat). Each
engine responds to three character knobs — HARMONICS, TIMBRE, MORPH —
plus a shared internal envelope and low-pass gate (LPG) for note
shaping. Each engine also provides an AUX output with a distinct variant
of the same sound, giving 20 musically-different timbres across the
engine bank.

The 13 engines covered are a curated subset of Plaits' original 16. See
the appendix for what was skipped and why.

## Parameters

### Global — core

| Parameter      | Range  | Function |
|----------------|--------|----------|
| Engine         | 0–9    | Engine selection (see Engine Reference) |
| Frequency      | 0–127  | Pitch transpose (48 = no offset, 60 = +octave, 36 = −octave) |
| Harmonics      | 0–127  | Engine-specific character — see engine reference |
| Timbre         | 0–127  | Engine-specific character — see engine reference |
| Morph          | 0–127  | Engine-specific character — see engine reference |
| LPG Response   | 0–127  | 0 = pure filter modulation; 127 = pure amplitude (VCA). Both modes pass through the vactrol envelope follower (5 ms attack / 30 ms release) which adds a small softening to the attack and a slight extension to the release. |
| Decay          | 0–127  | Internal envelope decay time (~5 ms to ~5 s, exponential) |
| Volume         | 0–127  | Final output level |

All six continuous parameters (Harmonics through Volume) are smoothed
with a 10 ms time constant — external LFO / envelope-follower /
MIDI-CC modulation targeting them produces clean continuous changes
rather than buffer-rate zipper noise. See Core §32 for the design.

### Global — velocity routing

| Parameter      | Range  | Function |
|----------------|--------|----------|
| Vel Harmonics  | 0–127  | Velocity sensitivity for Harmonics (64 = no modulation, bipolar) |
| Vel Timbre     | 0–127  | Velocity sensitivity for Timbre |
| Vel Morph      | 0–127  | Velocity sensitivity for Morph |
| Vel Decay      | 0–127  | Velocity sensitivity for Decay |

Each routing depth is bipolar centered at 64. Values above 64 make the
target brighten/intensify on harder hits; values below 64 invert that
relationship. Max offset is ±0.5 of the target parameter's range at
velocity 1.0 with extreme depth. Default 64 = no modulation; pre-v1.3
patches play identically.

### Per-track

| Column   | Function |
|----------|----------|
| Note     | Pitch (or trigger event on percussive engines) |
| Velocity | Trigger strength — drives envelope start level and any active velocity-routing depths |

---

## Engine Reference

Engines 7, 8, 9 are percussive — they bypass the LPG and use their own
internal envelopes. **LPG Response and Decay have no effect** on those
three engines; everything else is shared.

### 0 — Virtual Analog

Two PolyBLEP oscillators with detune. Analog-style leads, pads, basses.

- **HARMONICS:** detuning between osc 1 and osc 2 — 0 = unison, 127 ≈ semitone.
  Same parameter also controls the detuning between the two hardsync pairs on AUX.
- **TIMBRE:** narrow pulse → full square (low half) → hardsync formants (high half).
  Hardsync slave fades in past noon, its frequency rising exponentially from
  1× to 8× master — sweep MORPH-shaped saw against bright formant peak.
- **MORPH:** osc 1 waveform — triangle → saw → notched saw (Braids' CSAW).
  Also shapes the two hardsync slaves on AUX through the same triangle/saw/
  notched-saw vocabulary.
- **AUX:** sum of two hardsync'd waveforms — two (master, slave) pairs where
  master B runs at the same detuned frequency as osc 2, each slave at a fixed
  2× ratio, both MORPH-shaped. Bright twin-sync chorus character.

Notched-saw discontinuities are anti-aliased on both edges as of v1.7: a
PolyBLEP at the saw wrap (`phase = 0/1`, inherited from the saw's standard
correction) plus a phase-shifted PolyBLEP at the notch-entry transition
(`phase = 1 − notchWidth`, scaled by `wNotch × notchWidth`).

### 1 — Waveshaping

Asymmetric-triangle → waveshaper → wavefolder chain. Buzzy, gritty,
FM-like timbres.

- **HARMONICS:** waveshaper curve — identity → tanh → cubic
- **TIMBRE:** wavefolder gain — 1× (none) → 8× (aggressive)
- **MORPH:** triangle asymmetry — 0.15 to 0.85 (clamped for anti-alias)
- **AUX:** same chain but with a triangle-fold wavefolder (sharper edges)

### 2 — Two-op FM

Sine-on-sine FM with self-feedback. Bells, electric pianos, brass, clangs.

- **HARMONICS:** modulator/carrier ratio — snaps to a table of 11 musically-useful
  values: 0.25, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 4.0, 5.0, 7.0, 11.0
- **TIMBRE:** modulation index — 0 to 6 radians, soft-clipped (v1.8) to keep
  peak FM sidebands below 90% of Nyquist. At low pitches the response is
  unchanged; at high pitches the index is smoothly bounded to prevent aliasing.
- **MORPH:** self-feedback — carrier-self-fb below noon, clean FM at noon, mod-self-fb above
- **AUX:** sub-oscillator at half the carrier frequency

### 3 — Harmonic additive

24-partial sine bank with parametric spectral envelope. Additive timbres,
organs, bells.

- **HARMONICS:** spectral envelope multiplicity — 1 = single peak, 5 = formant comb
- **TIMBRE:** centre partial position — BPF-cutoff feel
- **MORPH:** envelope width — BPF-resonance feel
- **AUX:** same envelope at Hammond-drawbar partials only (1, 2, 3, 4, 6, 8, 10, 12)

### 4 — Wavetable

8 banks × 8 × 8 grid of wavetables, generated at load time and shared
statically across machine instances (~264 KB once). A navigable 2D map
of timbres per bank.

- **HARMONICS:** bank selection (discrete jump between banks)
  - 0/4: Plaits bank_1 — mild additive (sines, drawbars, comb, pair, tri/saw stacks)
  - 1/5: sine wavefolder with asymmetry (algorithmic)
  - 2/6: Plaits bank_3 — Braids-derived voice/digital/metal/drone/fant
    families (loaded from embedded `waves.bin`; falls back to
    algorithmic inharmonic if the resource is missing)
  - 3/7: Plaits bank_2 — formantish (trisaw, sawtri, burst, formants, pulse, sine-power)

  Banks 0–3 are *interpolated* (smooth TIMBRE/MORPH sweeps).
  Banks 4–7 are *stepped* (click-jumps between cells — deliberate digital character).
- **TIMBRE:** row position within bank
- **MORPH:** column position within bank
- **AUX:** same waveform quantised to 5-bit values (low-fi crunch)

v1.7 stores each cell as the cumulative sum of a normalised 128-sample
waveform (Plaits-native size) plus 4 padding samples for cross-wrap
interpolation. Playback computes `y = (I(φ + dt) − I(φ)) / (dt · N)`,
which is mathematically the average of the original waveform over the
playback step. At low pitches the response matches direct sample
playback; at high pitches the averaging length grows with `dt` and
naturally low-passes (Franck & Valimaki, *Higher-order integrated
wavetable synthesis*, DAFX-12; here K=1, linear interpolation).

### 5 — Granular cloud

8 grain voices in a swarm, each periodically retriggered with random
detune. Supersaws, ensemble pads, granular clouds.

- **HARMONICS:** per-grain detune spread — 0 to ±2 semitones
- **TIMBRE:** grain density — 20 to 500 retriggers per second
- **MORPH:** grain duration — 5 ms (percussive blips) to 300 ms (full overlap)
- **AUX:** same swarm but with sine oscillators instead of PolyBLEP saws

High TIMBRE + high MORPH = classic supersaw (all 8 grains overlap
continuously). Lower settings give audible granular pulsing.

### 6 — Filtered noise

Variable-clock noise → resonant Chamberlin SVF. Winds, breaths, playable
resonance.

**Note column sets the filter CUTOFF, not pitch.** At high MORPH
(resonance), the filter self-oscillates and you can play melodies via
the note column.

- **HARMONICS:** filter response — LP → BP → HP
- **TIMBRE:** noise clock rate — 20 Hz (stepped) to 40 kHz (white)
- **MORPH:** resonance — 0.5 (broad) to 12 (self-oscillation)
- **AUX:** two parallel BPFs with separation controlled by HARMONICS

### 7 — Bass drum *(percussive)*

Bridged-T resonator + FM sine kick. 808 and 909 kick flavours in one engine.

- **HARMONICS:** attack sharpness (1–41 sample impulse) + overdrive (1×–5×)
- **TIMBRE:** brightness — pitch-sweep top (2×–8× base) and filter Q (20–50)
- **MORPH:** decay time — 50 ms to 1 s
- **OUT:** bridged-T resonator (Chamberlin SVF) with overdrive (808 character)
- **AUX:** FM kick — sine carrier + 2× sine modulator, diode soft-clip (909 character)

Note column sets the kick fundamental — low notes = traditional kick,
mid = tom, high = synth-drum / zap.

### 8 — Snare drum *(percussive)*

Two impulse-excited SVF resonators + BP/HP-filtered noise.

- **HARMONICS:** tonal/noise balance — 0 = rim-shot, 127 = tonal-tom
- **TIMBRE:** second resonator mode ratio — 1× (harmonic) to 3× (spread); 1.83× ≈ classic 909
- **MORPH:** decay — body 50–450 ms, wire buzz 100–700 ms (separate envelopes)
- **OUT:** tonal resonators + BP-filtered noise around 5 kHz (909 flavour)
- **AUX:** FM sine pair + HP-filtered noise (early FM-drum flavour)

### 9 — Hi-hat *(percussive)*

6 inharmonic-ratio squares + filtered noise. 808 and 707/708 hat
flavours.

- **HARMONICS:** metallic vs noise balance — 0 = filtered noise, 127 = pure squares
- **TIMBRE:** HPF cutoff — 1 kHz to 12 kHz (exponential)
- **MORPH:** decay — 30 ms to 500 ms
- **OUT:** 6-square sum + noise → HPF → tanh dirty VCA (808 flavour)
- **AUX:** 3 ring-modulated square pairs → HPF → clean VCA (707/708 flavour)

### 10 — Modal resonator *(self-enveloped)*

Rings-style modal partial bank — 24 biquad resonators in parallel,
excited by a Hann-windowed white-noise burst on NoteOn. Plucked
strings, marimba, bell, glass, gamelan.

- **HARMONICS:** structure — stiffness coefficient B in the modal
  ratio formula `f_n = n · f_0 · √(1 + B · n²)`. At 0 the partials sit
  at perfect integer ratios (string-like); the upper half of the knob
  opens up to bell/marimba/glass spectra. Quadratic mapping so the
  lower half stays in string-stretch territory.
- **TIMBRE:** brightness — per-partial decay rolloff. 0 = high partials
  decay sharply faster than the fundamental (mellow, damped); 127 = all
  partials share the same decay (bright, even ring).
- **MORPH:** position / damping — fundamental T60 swept exponentially
  from ~50 ms (percussive pluck) to ~3 s (long sustained ring).
- **OUT:** sum of all 24 partials.
- **AUX:** sum of even-indexed partials only (fundamental + odd
  harmonics) — hollow, clarinet-like character.

Bypasses the LPG since each partial owns its decay; the global Decay
parameter is therefore ignored by this engine (MORPH is the decay
control). Partials whose frequency would exceed 95% of Nyquist are
deactivated to prevent aliasing at extreme pitches.

### 11 — Inharmonic string *(self-enveloped)*

Rings-style extended Karplus-Strong — single delay-loop string model
with in-loop stiffness for inharmonicity. Plucks, mallet hits, bowed
strings, glass/metal/bell territory depending on stiffness. The
pitched companion to the modal engine: where modal gives discrete
bell-like resonances, this gives continuous string-like ringing.

- **HARMONICS:** structure — stiffness coefficient of an in-loop
  allpass that pushes higher partials sharp. 0 = perfectly harmonic
  (clean nylon/steel string); upper half opens up to piano-stretch
  through clangorous bell territory at the top. Quadratic mapping for
  finer low-end control.
- **TIMBRE:** excitation brightness — cutoff of a one-pole LP on the
  noise-burst exciter. 0 = mellow wooden pluck; 127 = sharp percussive
  attack.
- **MORPH:** decay time — pitch-independent T60 swept exponentially
  from ~50 ms (heavily damped pluck) to ~5 s (long sustained ring).
  Feedback gain is recomputed per buffer from `L = sr / freq` so
  decay length is consistent across the keyboard.
- **OUT:** the sustained ringing string.
- **AUX:** raw exciter signal — the windowed noise burst before it
  enters the loop. Brief percussive texture for layering against the
  OUT's sustain.

Same self-envelope handling as the modal engine: bypasses the LPG,
ignores the global Decay parameter, reports IsSilent when both the
exciter burst is done and the loop's peak amplitude drops below
threshold so the voice can free CPU. Single voice — Plaits' module
supports 3-voice sympathetic-string polyphony in this engine, but
Pedal Plaits is mono throughout and a polyphonic re-architecture is
a v2.0 question.

### 12 — Phase distortion

Casio CZ-style phase distortion combined with phase modulation —
asymmetric piecewise mapping of the carrier phase before the sine
lookup, plus a modulator that phase-modulates the result. The two
outputs demonstrate the same engine under different sync regimes.

- **HARMONICS:** distortion frequency — modulator/carrier ratio
  swept smoothly from 0.5× to 8× (quadratic mapping for finer
  control in the musical 1–3× range, opens up to clangorous
  inharmonic ratios at the top).
- **TIMBRE:** distortion amount — modulator depth in radians, 0 to
  2, soft-clipped per buffer with the same analytical sideband bound
  as the FM engine so peak sidebands stay below 90 % of Nyquist
  regardless of pitch.
- **MORPH:** distortion asymmetry — break point of the piecewise
  phase mapping applied to the carrier. 0.5 = symmetric (smooth
  sine), extremes = strongly skewed phase advance (saw/reverse-saw
  character). Mapped to [0.1, 0.9] so the extremes still produce a
  meaningful waveform.
- **OUT:** carrier hard-sync'd to the modulator — modulator phase =
  ratio × carrier_phase mod 1, so sidebands land on exact integer
  multiples of the carrier. The "phase distortion" half: strict
  harmonic skirts, classic Casio CZ flavour.
- **AUX:** carrier and modulator both free-running. The "modulation"
  half: smooth FM-like sidebands, inharmonic when the ratio isn't an
  integer.

---

## Architecture

```
ReBuzz Work(IList<Sample[]>, n, mode)
   │
   ├─ sample-rate-change detection (Core §29)
   ├─ transport-stop detection (Core §27)
   ├─ drain pending Note On / Note Off  (NoteOn uses raw param values)
   ├─ compute smoothed-param end-of-buffer values (Core §32)
   ├─ compute velocity-routing offsets from current Vel* depths
   ├─ for each BLOCK_SIZE (12) chunk:
   │     scratchOut[] = scratchAux[] = 0
   │     lerp smoothed params, add velocity offsets, clamp to [0,1]
   │     Voice.Render(scratchOut, scratchAux, blk, params)
   │       │
   │       ├─ early-exit when !IsActive  (CPU saving — Core §30 discussion)
   │       ├─ engine.Render(...)
   │       ├─ if !engine.IsPercussive:
   │       │     env = DecayEnv.Process(blk)
   │       │     LPG.Process(scratchOut, scratchAux, env, response, decay)
   │       └─ tracks IsActive — pitched: env-based; percussive: IEngine.IsSilent
   │     copy scratch → output[i..i+blk] with smoothed Volume scaling
   └─ return Voice.IsActive
```

| Path | Role |
|------|------|
| `Pedalplaits.cs`                  | `IBuzzMachine`, `MachineDecl`, parameters, `Work()` |
| `Voice.cs`                        | Engine switching, LPG/envelope routing, IsActive |
| `Lpg.cs`                          | Low-pass gate (one-pole + amp) |
| `DecayEnv.cs`                     | Internal exponential decay envelope |
| `Engines/IEngine.cs`              | Engine contract + `EngineParams` struct + `IsSilent` default |
| `Engines/VirtualAnalogEngine.cs`  | Engine 0 |
| `Engines/WaveshapingEngine.cs`    | Engine 1 |
| `Engines/FmEngine.cs`             | Engine 2 |
| `Engines/HarmonicEngine.cs`       | Engine 3 |
| `Engines/WavetableEngine.cs`      | Engine 4 (static wavetables, shared across instances) |
| `Engines/PlaitsWavetables.cs`     | Plaits-faithful wave generators (banks 1, 2, 3 — v1.4/v1.6) |
| `Resources/waves.bin`             | Plaits' Braids-derived wavetable data, embedded into the DLL (v1.6) |
| `Engines/GranularCloudEngine.cs`  | Engine 5 |
| `Engines/FilteredNoiseEngine.cs`  | Engine 6 |
| `Engines/BassDrumEngine.cs`       | Engine 7 (first percussive) |
| `Engines/SnareDrumEngine.cs`      | Engine 8 (with 1/f tonal normalisation — Core §31) |
| `Engines/HiHatEngine.cs`          | Engine 9 |
| `Engines/ModalResonatorEngine.cs` | Engine 10 (Rings-style modal resonator, v1.9) |
| `Engines/InharmonicStringEngine.cs`| Engine 11 (extended Karplus-Strong inharmonic string, v1.11) |
| `Engines/PhaseDistortionEngine.cs`| Engine 12 (Casio CZ phase distortion + modulation, v1.12) |
| `Util/PolyBlep.cs`                | PolyBLEP step correction |
| `Util/DspUtil.cs`                 | MidiToHz, lerp, clamp helpers |

---

## Build

```
dotnet build -c Release
```

The csproj's post-build target copies `Pedal Plaits.NET.dll` into
`C:\Program Files\ReBuzz\Gear\Generators\` automatically. Close ReBuzz
before rebuilding — a running ReBuzz holds a lock on the DLL and the
copy will be silently skipped (`ContinueOnError="true"` on the Copy task).

Per Build §1.2 / §1.3, the csproj suppresses `.pdb` and `.deps.json`
output (`DebugType=none`, `DebugSymbols=false`, `GenerateDependencyFile=false`)
and silences MSB3277. Only the `.dll` is deployed.

---

## Known limitations

- **Wavetable engine 4 — banks 1 and 5** (the wavefolder archetype)
  remain algorithmically generated. Banks 0/4 (Plaits bank_1: mild
  additive), 3/7 (Plaits bank_2: formantish), and 2/6 (Plaits bank_3:
  shruthi/ambika/braids-derived) are built from the same generator
  functions as the Plaits firmware. Bank_3 requires `Resources/waves.bin`
  to be embedded in the assembly; if the file is missing the engine
  silently falls back to an algorithmic inharmonic bank.
- **Engine 0 hardsync AA** (v1.5) uses a scaled-PolyBLEP correction at
  post-master-wrap samples, sized to the actual `-2 × frac(syncRatio)`
  discontinuity. The MORPH notched-saw component on osc 1 and the AUX
  slaves gains wrap-side AA in v1.6 and entry-side AA in v1.7, so both
  notch discontinuities are now corrected. Residual aliasing at very
  high pitches with extreme MORPH/HARMONICS comes from the AUX
  hardsync interaction with the notched waveform shape, which is
  inherently rich and would benefit from oversampling on extreme
  patches rather than additional BLEP terms.
- **Engine 2 (FM)** uses a hard ratio table on HARMONICS (no continuous
  sweep — see the engine reference). Sideband AA via mod-index soft-clip
  (v1.8) holds peak sidebands below 90% Nyquist; self-feedback paths
  remain unbounded analytically and may grit at extreme MORPH settings
  on high notes, but this is musically idiomatic for FM feedback.
- **LPG vactrol modeling** (v1.4–v1.8) is a two-stage RC cascade — fast
  symmetric LED stage feeding a slow asymmetric LDR stage — with a
  power-law LDR transfer curve (exponent 1.4) on the audio path. The
  LDR exponent is a single tunable rather than a fitted model; a more
  detailed saturation-aware LDR transfer curve and per-component
  temperature drift would be the next refinement, but neither is
  audibly necessary for typical patches.
- **Mono only.** Polyphony would require significant per-voice memory
  expansion (wavetable + harmonic engines have large per-voice state).

---

## Appendix: Engines not ported from the original

From Plaits' 16 engines, these are deliberately not in this port:

- **Speech (Plaits engine 8)** — LPC/SAM speech-synthesis tables, marginal
  value in a tracker context relative to porting cost.
- **String chord (Plaits engine 7)** — PedalChord already covers chord
  generation in the project's ecosystem.
- **Particle noise (Plaits engine 11)** — character overlaps with the
  filtered noise engine.

The `IEngine` contract supports adding any of these later without
architectural change.

---

## Credits

DSP architecture and engine designs by Émilie Gillet (Mutable Instruments),
released under the MIT license. This C# port preserves that license and is
not affiliated with or endorsed by Mutable Instruments. The wavetable
generation functions in `Engines/PlaitsWavetables.cs` (banks 0/4, 2/6, and
3/7 of engine 4) are direct ports of the algorithms in
`plaits/resources/wavetables.py` from the Plaits firmware source, and
`Resources/waves.bin` is the original Plaits Braids-derived waveform data
file redistributed under the same MIT license.

Core ReBuzz managed-machine findings used by this port are documented in
the project's `ReBuzz_ManagedMachine_Notes_Core.md` (§27 transport stop,
§29 sample-rate change, §30 denormal protection, §31 SVF impulse-response
normalisation, §32 per-Work parameter smoothing) and `_Build.md`
(§1.2 mandatory csproj properties, §1.3 post-build deploy target,
§6 namespace separation).
