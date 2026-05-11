# Pedal Plaits

ReBuzz managed machine — port of Mutable Instruments' Plaits macro-oscillator.

**Status:** v1.0 — 10 engines functional, mono voice, OUT + AUX outputs.

**Original source:** https://github.com/pichenettes/eurorack/tree/master/plaits
**License:** MIT (Plaits firmware is MIT-licensed; this port preserves
that license and adds attribution — see Credits)

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

The 10 engines covered are a curated subset of Plaits' original 16. See
the appendix for what was skipped and why.

## Parameters

### Global

| Parameter      | Range  | Function |
|----------------|--------|----------|
| Engine         | 0–9    | Engine selection (see Engine Reference) |
| Frequency      | 0–127  | Pitch transpose (48 = no offset, 60 = +octave, 36 = −octave) |
| Harmonics      | 0–127  | Engine-specific character — see engine reference |
| Timbre         | 0–127  | Engine-specific character — see engine reference |
| Morph          | 0–127  | Engine-specific character — see engine reference |
| LPG Response   | 0–127  | 0 = pure filter modulation; 127 = pure amplitude (VCA) |
| Decay          | 0–127  | Internal envelope decay time (~5 ms to ~5 s, exponential) |
| Volume         | 0–127  | Final output level |

### Per-track

| Column   | Function |
|----------|----------|
| Note     | Pitch (or trigger event on percussive engines) |
| Velocity | Trigger strength — sets envelope start level |

---

## Engine Reference

Engines 7, 8, 9 are percussive — they bypass the LPG and use their own
internal envelopes. **LPG Response and Decay have no effect** on those
three engines; everything else is shared.

### 0 — Virtual Analog

Two PolyBLEP oscillators with detune. Analog-style leads, pads, basses.

- **HARMONICS:** detuning between osc 1 and osc 2 — 0 = unison, 127 ≈ semitone
- **TIMBRE:** variable-pulse width on osc 2 — narrow pulse → full square
- **MORPH:** osc 1 waveform — triangle → saw → notched saw
- **AUX:** ring modulation of osc 1 and osc 2

### 1 — Waveshaping

Asymmetric-triangle → waveshaper → wavefolder chain. Buzzy, gritty,
FM-like timbres.

- **HARMONICS:** waveshaper curve — identity → tanh → cubic
- **TIMBRE:** wavefolder gain — 1× (none) → 8× (aggressive)
- **MORPH:** triangle asymmetry — 0.15 to 0.85 (clamped for anti-alias)
- **AUX:** same chain but with a triangle-fold wavefolder (sharper edges)

### 2 — Two-op FM

Sine-on-sine FM with self-feedback. Bells, electric pianos, brass, clangs.

- **HARMONICS:** modulator/carrier ratio — smooth sweep 0.25× to 16×
- **TIMBRE:** modulation index — 0 to 6 radians
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

8 banks × 8 × 8 grid of wavetables, generated algorithmically at load
time and shared statically across machine instances (~512 KB once). A
navigable 2D map of timbres per bank.

- **HARMONICS:** bank selection (discrete jump between banks)
  - 0/4: harmonic series with spectral tilt
  - 1/5: sine wavefolder with asymmetry
  - 2/6: inharmonic partial sums
  - 3/7: narrow-band formant peaks

  Banks 0–3 are *interpolated* (smooth TIMBRE/MORPH sweeps).
  Banks 4–7 are *stepped* (click-jumps between cells — deliberate digital character).
- **TIMBRE:** row position within bank
- **MORPH:** column position within bank
- **AUX:** same waveform quantised to 5-bit values (low-fi crunch)

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

---

## Architecture

```
ReBuzz Work(IList<Sample[]>, n, mode)
   │
   ├─ sample-rate-change detection (Core §29)
   ├─ transport-stop detection (Core §27)
   ├─ drain pending Note On / Note Off
   ├─ for each BLOCK_SIZE (12) chunk:
   │     scratchOut[] = scratchAux[] = 0
   │     Voice.Render(scratchOut, scratchAux, blk, params)
   │       │
   │       ├─ early-exit when !IsActive  (CPU saving — Core §30 discussion)
   │       ├─ engine.Render(...)
   │       ├─ if !engine.IsPercussive:
   │       │     env = DecayEnv.Process(blk)
   │       │     LPG.Process(scratchOut, scratchAux, env, response, decay)
   │       └─ tracks IsActive — pitched: env-based; percussive: IEngine.IsSilent
   │     copy scratch → output[i..i+blk] with Volume scaling
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
| `Engines/GranularCloudEngine.cs`  | Engine 5 |
| `Engines/FilteredNoiseEngine.cs`  | Engine 6 |
| `Engines/BassDrumEngine.cs`       | Engine 7 (first percussive) |
| `Engines/SnareDrumEngine.cs`      | Engine 8 (with 1/f tonal normalisation — Core §31) |
| `Engines/HiHatEngine.cs`          | Engine 9 |
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

- **Wavetable engine 4** uses algorithmically-generated tables rather than
  the original Plaits wavetable data. Character is Plaits-*like* but not
  bit-identical. A `Resources.cs` binary loader could swap in extracted
  tables in a future revision; the architecture supports it.
- **Wavetable aliasing at very high pitches** (C-7 and above) — no
  mip-mapped table variants. Stay within typical musical ranges for clean
  output.
- **Engine 0** takes a shortcut on the `hardsync formants` end of TIMBRE,
  treating it as a continuation of the variable-pulse range rather than
  the separate hardsync mode of the original.
- **Engine 2 (FM)** uses a smooth ratio sweep on HARMONICS rather than
  snapping to musically-useful ratios (0.5, 1, 2, 3, 4, 5, 7, 11…).
  No anti-aliasing on high-modulation-index sidebands either, so very
  high pitches with TIMBRE near maximum may grit slightly.
- **LPG** is a simplified 1-pole + amp rather than a full vactrol-modelled
  low-pass gate. Audible difference is minimal except at very short Decay.
- **Mono only.** Polyphony would require significant per-voice memory
  expansion (wavetable + harmonic engines have large per-voice state).

---

## Appendix: Engines not ported from the original

From Plaits' 16 engines, these are deliberately not in this port:

- **Speech (Plaits engine 8)** — LPC/SAM speech-synthesis tables, marginal
  value in a tracker context relative to porting cost.
- **String chord (Plaits engine 7)** — PedalChord already covers chord
  generation in the project's ecosystem.
- **Formant/PD (Plaits engine 4)** — phase-distortion formant synthesis;
  PedalInvFFT covers related territory differently.
- **Particle noise (Plaits engine 11)** — character overlaps with the
  filtered noise engine.
- **Modal/inharmonic string (Plaits engines 12–13)** — Rings-style modal
  resonator; warrants a separate PedalModal port. PedalInvFFT covers
  FFT-based partial work already.

The `IEngine` contract supports adding any of these later without
architectural change.

---

## Credits

DSP architecture and engine designs by Émilie Gillet (Mutable Instruments),
released under the MIT license. This C# port preserves that license and is
not affiliated with or endorsed by Mutable Instruments.

Core ReBuzz managed-machine findings used by this port are documented in
the project's `ReBuzz_ManagedMachine_Notes_Core.md` (§27 transport stop,
§29 sample-rate change, §30 denormal protection, §31 SVF impulse-response
normalisation) and `_Build.md` (§1.2 mandatory csproj properties,
§1.3 post-build deploy target, §6 namespace separation).
