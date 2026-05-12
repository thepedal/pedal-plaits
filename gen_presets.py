#!/usr/bin/env python3
"""
gen_presets.py — generates Pedal Plaits_Presets.prs.xml for ReBuzz.

Run from the project root:
    python gen_presets.py

Writes 'Pedal Plaits_Presets.prs.xml' (UTF-8 with BOM) to the current
directory. The csproj's post-build target then copies it to
C:\\Program Files\\ReBuzz\\Gear\\Generators alongside the .dll.

Sparse-override pattern per Build §3.4 — each preset is a dict of
name->value overrides; any parameter not mentioned takes the value
from DEFAULTS (which mirrors the source's DefValue attributes).

Naming convention (v1.10):
  "NN Category - Description"  where NN is the zero-padded engine
  index (00..10). Zero-padding ensures alphabetical sort matches
  engine order in any ReBuzz browser display. 10 presets per engine,
  110 total.

Adding new parameters in future Pedal Plaits versions:
  1. Append new entries at the END of PARAM_INDEX (do not reorder).
  2. Append matching entries to DEFAULTS.
  3. Existing preset overrides keep working — they reference parameters
     by name, and the new ones just default in.
"""

MACHINE_NAME = "Pedal Plaits"
OUTPUT_FILE  = "Pedal Plaits_Presets.prs.xml"

# Source declaration order (Pedalplaits.cs). Adding a new
# parameter? APPEND only — see Build §3.3.
PARAM_INDEX = {
    "Engine":        0,
    "Frequency":     1,
    "Harmonics":     2,
    "Timbre":        3,
    "Morph":         4,
    "LPG Response":  5,
    "Decay":         6,
    "Volume":        7,
    "Vel Harmonics": 8,
    "Vel Timbre":    9,
    "Vel Morph":     10,
    "Vel Decay":     11,
}

# Mirror of Pedalplaits.cs DefValue attributes.
DEFAULTS = {
    "Engine":        0,
    "Frequency":     48,
    "Harmonics":     64,
    "Timbre":        64,
    "Morph":         64,
    "LPG Response":  0,
    "Decay":         32,
    "Volume":        100,
    "Vel Harmonics": 64,
    "Vel Timbre":    64,
    "Vel Morph":     64,
    "Vel Decay":     64,
}

# 110 presets, 10 per engine. Names start with the zero-padded engine
# index for quick visual grouping in the ReBuzz preset browser.
# "(AUX)" suffix flags presets where the AUX output is the intended
# use case rather than OUT.
#
# Percussive engines (7-9) ignore LPG Response and Decay — they don't
# appear in those presets' overrides. Engine 10 (modal) similarly
# ignores both since each partial owns its decay; MORPH is its decay
# control.
PRESETS = {
    # ── 00 — Virtual Analog ──
    "00 Lead - Saw":            {"Engine": 0, "Harmonics": 30,  "Morph": 90,  "LPG Response": 80,  "Decay": 60},
    "00 Lead - Square":         {"Engine": 0, "Harmonics": 10,  "Timbre": 127, "Morph": 20, "LPG Response": 100, "Decay": 70},
    "00 Lead - Sync":           {"Engine": 0, "Harmonics": 50,  "Timbre": 100, "Morph": 70, "LPG Response": 100, "Decay": 50},
    "00 Bass - Pulse":          {"Engine": 0, "Harmonics": 10,  "Timbre": 30,  "Morph": 20, "LPG Response": 100, "Decay": 30},
    "00 Bass - Sub":            {"Engine": 0, "Frequency": 30, "Harmonics": 0,   "Timbre": 40,  "Morph": 50, "LPG Response": 100, "Decay": 40},
    "00 Pad - Detune":          {"Engine": 0, "Harmonics": 100, "Timbre": 80,  "Morph": 80, "LPG Response": 20,  "Decay": 120},
    "00 Pad - Tri":             {"Engine": 0, "Harmonics": 60,  "Timbre": 40,  "Morph": 0,  "LPG Response": 30,  "Decay": 110},
    "00 Brass - Sync":          {"Engine": 0, "Harmonics": 40,  "Timbre": 90,  "Morph": 64, "LPG Response": 100, "Decay": 30},
    "00 Pluck - Snap":          {"Engine": 0, "Harmonics": 20,  "Timbre": 64,  "Morph": 80, "LPG Response": 100, "Decay": 10},
    "00 FX - Ring (AUX)":       {"Engine": 0, "Harmonics": 50,  "Timbre": 60,  "Morph": 80, "LPG Response": 100, "Decay": 100},

    # ── 01 — Waveshaping ──
    "01 Lead - Tanh":           {"Engine": 1, "Harmonics": 70,  "Timbre": 40,  "Morph": 64, "LPG Response": 80,  "Decay": 50},
    "01 Lead - Soft Fold":      {"Engine": 1, "Harmonics": 40,  "Timbre": 60,  "Morph": 80, "LPG Response": 80,  "Decay": 50},
    "01 Bass - Acid":           {"Engine": 1, "Harmonics": 110, "Timbre": 60,  "Morph": 25, "LPG Response": 20,  "Decay": 50},
    "01 Bass - Sub Fold":       {"Engine": 1, "Frequency": 30, "Harmonics": 30,  "Timbre": 90,  "Morph": 30, "LPG Response": 100, "Decay": 40},
    "01 Drone - Fold":          {"Engine": 1, "Harmonics": 50,  "Timbre": 120, "Morph": 70, "LPG Response": 80,  "Decay": 127},
    "01 Pluck - Glass":         {"Engine": 1, "Harmonics": 90,  "Timbre": 30,  "Morph": 50, "LPG Response": 100, "Decay": 20},
    "01 Pluck - Wood":          {"Engine": 1, "Harmonics": 60,  "Timbre": 40,  "Morph": 40, "LPG Response": 100, "Decay": 15},
    "01 Pad - Soft":            {"Engine": 1, "Harmonics": 30,  "Timbre": 50,  "Morph": 70, "LPG Response": 20,  "Decay": 120},
    "01 FX - Aggressive":       {"Engine": 1, "Harmonics": 127, "Timbre": 127, "Morph": 80, "LPG Response": 80,  "Decay": 60},
    "01 FX - Chaos":            {"Engine": 1, "Harmonics": 90,  "Timbre": 127, "Morph": 100, "LPG Response": 60, "Decay": 90},

    # ── 02 — Two-op FM ──
    # HARMONICS snaps to ratios { 0.25, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 4.0, 5.0, 7.0, 11.0 }.
    # Each ratio occupies ~12 HARMONICS units. Index = (int)(Harmonics/127 * 11).
    # HARMONICS=40 → ratio 1.0; =50 → 1.5; =64 → 2.0; =80 → 3.0; =110 → 7.0.
    "02 EP - Tine":             {"Engine": 2, "Harmonics": 40,  "Timbre": 40,  "Morph": 64, "LPG Response": 100, "Decay": 60},
    "02 Bell":                  {"Engine": 2, "Harmonics": 80,  "Timbre": 80,  "Morph": 64, "LPG Response": 80,  "Decay": 90},
    "02 Bell - Glassy":         {"Engine": 2, "Harmonics": 110, "Timbre": 90,  "Morph": 70, "LPG Response": 80,  "Decay": 80},
    "02 Brass":                 {"Engine": 2, "Harmonics": 50,  "Timbre": 100, "Morph": 80, "LPG Response": 100, "Decay": 40},
    "02 Lead - Brassy":         {"Engine": 2, "Harmonics": 64,  "Timbre": 90,  "Morph": 70, "LPG Response": 100, "Decay": 35},
    "02 Bass - Clang":          {"Engine": 2, "Harmonics": 110, "Timbre": 80,  "Morph": 40, "LPG Response": 100, "Decay": 40},
    "02 Bass - Sub (AUX)":      {"Engine": 2, "Frequency": 30, "Harmonics": 40,  "Timbre": 40,  "Morph": 64, "LPG Response": 100, "Decay": 50},
    "02 Pluck - DX":            {"Engine": 2, "Harmonics": 50,  "Timbre": 70,  "Morph": 40, "LPG Response": 100, "Decay": 25},
    "02 Pad - DX":              {"Engine": 2, "Harmonics": 40,  "Timbre": 60,  "Morph": 64, "LPG Response": 10,  "Decay": 120},
    "02 FX - Inharmonic":       {"Engine": 2, "Harmonics": 110, "Timbre": 110, "Morph": 100, "LPG Response": 80, "Decay": 110},

    # ── 03 — Harmonic additive ──
    "03 Organ":                 {"Engine": 3, "Harmonics": 20,  "Timbre": 20,  "Morph": 80, "LPG Response": 127, "Decay": 120},
    "03 Drone - Hammond":       {"Engine": 3, "Harmonics": 30,  "Timbre": 10,  "Morph": 100, "LPG Response": 127, "Decay": 127},
    "03 Lead - Sine":           {"Engine": 3, "Harmonics": 10,  "Timbre": 64,  "Morph": 10, "LPG Response": 100, "Decay": 30},
    "03 Lead - Bright":         {"Engine": 3, "Harmonics": 80,  "Timbre": 100, "Morph": 30, "LPG Response": 100, "Decay": 40},
    "03 Bass - Pure":           {"Engine": 3, "Frequency": 30, "Harmonics": 20,  "Timbre": 40,  "Morph": 20, "LPG Response": 100, "Decay": 50},
    "03 Bell - Glass":          {"Engine": 3, "Harmonics": 64,  "Timbre": 110, "Morph": 20, "LPG Response": 80,  "Decay": 70},
    "03 Bell - Soft":           {"Engine": 3, "Harmonics": 40,  "Timbre": 80,  "Morph": 40, "LPG Response": 80,  "Decay": 80},
    "03 Pluck - Wood":          {"Engine": 3, "Harmonics": 40,  "Timbre": 64,  "Morph": 30, "LPG Response": 100, "Decay": 20},
    "03 Pad - Choir":           {"Engine": 3, "Harmonics": 70,  "Timbre": 60,  "Morph": 80, "LPG Response": 20,  "Decay": 120},
    "03 Pad - Vox":             {"Engine": 3, "Harmonics": 80,  "Timbre": 80,  "Morph": 70, "LPG Response": 20,  "Decay": 127},

    # ── 04 — Wavetable ──
    "04 Pad - Interp":          {"Engine": 4, "Harmonics": 30,  "Timbre": 80,  "Morph": 64, "LPG Response": 20,  "Decay": 120},
    "04 Pad - Drone":           {"Engine": 4, "Harmonics": 20,  "Timbre": 100, "Morph": 100, "LPG Response": 30,  "Decay": 127},
    "04 Lead - Tilt":           {"Engine": 4, "Harmonics": 0,   "Timbre": 110, "Morph": 80, "LPG Response": 100, "Decay": 60},
    "04 Lead - Bank3":          {"Engine": 4, "Harmonics": 40,  "Timbre": 64,  "Morph": 80, "LPG Response": 100, "Decay": 50},
    "04 Vox - Formant":         {"Engine": 4, "Harmonics": 70,  "Timbre": 80,  "Morph": 64, "LPG Response": 60,  "Decay": 110},
    "04 Bell - Inharm":         {"Engine": 4, "Harmonics": 50,  "Timbre": 64,  "Morph": 80, "LPG Response": 80,  "Decay": 70},
    "04 Bell - Voice":          {"Engine": 4, "Harmonics": 40,  "Timbre": 100, "Morph": 90, "LPG Response": 80,  "Decay": 80},
    "04 Pluck - Hammond":       {"Engine": 4, "Harmonics": 0,   "Timbre": 30,  "Morph": 30, "LPG Response": 100, "Decay": 25},
    "04 FX - 8-bit (AUX)":      {"Engine": 4, "Harmonics": 110, "Timbre": 64,  "Morph": 64, "LPG Response": 100, "Decay": 50},
    "04 FX - Stepped":          {"Engine": 4, "Harmonics": 90,  "Timbre": 100, "Morph": 70, "LPG Response": 100, "Decay": 70},

    # ── 05 — Granular cloud ──
    "05 Pad - Supersaw":        {"Engine": 5, "Harmonics": 40,  "Timbre": 100, "Morph": 127, "LPG Response": 100, "Decay": 110},
    "05 Pad - Cloud":           {"Engine": 5, "Harmonics": 90,  "Timbre": 40,  "Morph": 80,  "LPG Response": 40,  "Decay": 120},
    "05 Pad - Ambient":         {"Engine": 5, "Harmonics": 70,  "Timbre": 70,  "Morph": 110, "LPG Response": 30,  "Decay": 127},
    "05 Lead - Ensemble":       {"Engine": 5, "Harmonics": 30,  "Timbre": 80,  "Morph": 80,  "LPG Response": 80,  "Decay": 60},
    "05 Lead - Wide":           {"Engine": 5, "Harmonics": 60,  "Timbre": 100, "Morph": 90,  "LPG Response": 100, "Decay": 50},
    "05 FX - Sparkle (AUX)":    {"Engine": 5, "Harmonics": 64,  "Timbre": 110, "Morph": 110, "LPG Response": 100, "Decay": 90},
    "05 FX - Buzz":             {"Engine": 5, "Harmonics": 110, "Timbre": 100, "Morph": 60,  "LPG Response": 100, "Decay": 80},
    "05 Pluck - Pulses":        {"Engine": 5, "Harmonics": 10,  "Timbre": 20,  "Morph": 10,  "LPG Response": 100, "Decay": 30},
    "05 Pluck - Click":         {"Engine": 5, "Harmonics": 30,  "Timbre": 30,  "Morph": 20,  "LPG Response": 100, "Decay": 15},
    "05 Drone - Swarm":         {"Engine": 5, "Harmonics": 100, "Timbre": 80,  "Morph": 120, "LPG Response": 40,  "Decay": 127},

    # ── 06 — Filtered noise (note column sets cutoff, not pitch) ──
    "06 FX - Wind":             {"Engine": 6, "Harmonics": 64,  "Timbre": 100, "Morph": 40,  "LPG Response": 100, "Decay": 90},
    "06 FX - Hiss":             {"Engine": 6, "Harmonics": 30,  "Timbre": 110, "Morph": 30,  "LPG Response": 100, "Decay": 80},
    "06 FX - Resonant":         {"Engine": 6, "Harmonics": 64,  "Timbre": 100, "Morph": 120, "LPG Response": 100, "Decay": 90},
    "06 FX - Resonator":        {"Engine": 6, "Harmonics": 90,  "Timbre": 90,  "Morph": 120, "LPG Response": 100, "Decay": 100},
    "06 FX - Steppy":           {"Engine": 6, "Harmonics": 64,  "Timbre": 20,  "Morph": 80,  "LPG Response": 100, "Decay": 40},
    "06 Pluck - Breath":        {"Engine": 6, "Harmonics": 70,  "Timbre": 110, "Morph": 80,  "LPG Response": 100, "Decay": 20},
    "06 Pluck - Tick":          {"Engine": 6, "Harmonics": 30,  "Timbre": 100, "Morph": 100, "LPG Response": 100, "Decay": 10},
    "06 Pad - Sweep":           {"Engine": 6, "Harmonics": 0,   "Timbre": 80,  "Morph": 80,  "LPG Response": 20,  "Decay": 120},
    "06 Pad - White":           {"Engine": 6, "Harmonics": 30,  "Timbre": 64,  "Morph": 30,  "LPG Response": 20,  "Decay": 110},
    "06 Drone - Filter":        {"Engine": 6, "Harmonics": 90,  "Timbre": 64,  "Morph": 100, "LPG Response": 30,  "Decay": 127},

    # ── 07 — Bass drum (percussive — LPG/Decay no-op) ──
    "07 Kick - 808":            {"Engine": 7, "Harmonics": 80,  "Timbre": 80,  "Morph": 80},
    "07 Kick - 909 (AUX)":      {"Engine": 7, "Harmonics": 110, "Timbre": 64,  "Morph": 70},
    "07 Kick - Hard":           {"Engine": 7, "Harmonics": 127, "Timbre": 100, "Morph": 40},
    "07 Kick - Soft":           {"Engine": 7, "Harmonics": 50,  "Timbre": 60,  "Morph": 90},
    "07 Kick - Sub":            {"Engine": 7, "Harmonics": 20,  "Timbre": 20,  "Morph": 120},
    "07 Kick - Click":          {"Engine": 7, "Harmonics": 100, "Timbre": 127, "Morph": 20},
    "07 Kick - Long":           {"Engine": 7, "Harmonics": 70,  "Timbre": 80,  "Morph": 110},
    "07 Tom":                   {"Engine": 7, "Harmonics": 60,  "Timbre": 80,  "Morph": 50},
    "07 Tom - Low":             {"Engine": 7, "Frequency": 36, "Harmonics": 60,  "Timbre": 60,  "Morph": 70},
    "07 Tom - High":            {"Engine": 7, "Frequency": 60, "Harmonics": 60,  "Timbre": 80,  "Morph": 50},

    # ── 08 — Snare drum (percussive) ──
    "08 Snare - 909":           {"Engine": 8, "Harmonics": 64,  "Timbre": 64,  "Morph": 64},
    "08 Snare - 808":           {"Engine": 8, "Harmonics": 50,  "Timbre": 64,  "Morph": 70},
    "08 Snare - Hard":          {"Engine": 8, "Harmonics": 80,  "Timbre": 80,  "Morph": 80},
    "08 Snare - Bright":        {"Engine": 8, "Harmonics": 100, "Timbre": 100, "Morph": 60},
    "08 Snare - Soft":          {"Engine": 8, "Harmonics": 40,  "Timbre": 40,  "Morph": 90},
    "08 Snare - FM (AUX)":      {"Engine": 8, "Harmonics": 70,  "Timbre": 80,  "Morph": 70},
    "08 Rim":                   {"Engine": 8, "Harmonics": 20,  "Timbre": 40,  "Morph": 20},
    "08 Rim - Tonal":           {"Engine": 8, "Harmonics": 10,  "Timbre": 100, "Morph": 30},
    "08 Clap":                  {"Engine": 8, "Harmonics": 30,  "Timbre": 100, "Morph": 50},
    "08 Tom-Snare":             {"Engine": 8, "Harmonics": 100, "Timbre": 110, "Morph": 80},

    # ── 09 — Hi-hat (percussive) ──
    "09 Hat - 808 Closed":      {"Engine": 9, "Harmonics": 100, "Timbre": 80,  "Morph": 20},
    "09 Hat - 808 Open":        {"Engine": 9, "Harmonics": 100, "Timbre": 80,  "Morph": 80},
    "09 Hat - Bright Closed":   {"Engine": 9, "Harmonics": 110, "Timbre": 110, "Morph": 15},
    "09 Hat - Long Open":       {"Engine": 9, "Harmonics": 90,  "Timbre": 90,  "Morph": 110},
    "09 Hat - Metallic":        {"Engine": 9, "Harmonics": 127, "Timbre": 70,  "Morph": 60},
    "09 Hat - 707 (AUX)":       {"Engine": 9, "Harmonics": 80,  "Timbre": 80,  "Morph": 40},
    "09 Hat - Pedal":           {"Engine": 9, "Harmonics": 70,  "Timbre": 70,  "Morph": 25},
    "09 Hat - Noise":           {"Engine": 9, "Harmonics": 30,  "Timbre": 110, "Morph": 40},
    "09 Cymbal":                {"Engine": 9, "Harmonics": 110, "Timbre": 60,  "Morph": 110},
    "09 Cymbal - Crash":        {"Engine": 9, "Harmonics": 120, "Timbre": 50,  "Morph": 127},

    # ── 10 — Modal resonator (Rings-style, self-enveloped) ──
    # LPG and global Decay ignored; MORPH controls decay time.
    "10 Pluck - String":        {"Engine": 10, "Harmonics": 0,   "Timbre": 64,  "Morph": 40},
    "10 Pluck - Marimba":       {"Engine": 10, "Harmonics": 50,  "Timbre": 30,  "Morph": 25},
    "10 Pluck - Glass":         {"Engine": 10, "Harmonics": 120, "Timbre": 100, "Morph": 30},
    "10 Pluck - Gamelan":       {"Engine": 10, "Harmonics": 95,  "Timbre": 50,  "Morph": 50},
    "10 Pluck - Wood (AUX)":    {"Engine": 10, "Harmonics": 30,  "Timbre": 30,  "Morph": 25},
    "10 Bell - Tubular":        {"Engine": 10, "Harmonics": 85,  "Timbre": 90,  "Morph": 80},
    "10 Bell - Crystal":        {"Engine": 10, "Harmonics": 110, "Timbre": 110, "Morph": 90},
    "10 Pad - Wash":            {"Engine": 10, "Harmonics": 40,  "Timbre": 100, "Morph": 115},
    "10 Drone - Sustained":     {"Engine": 10, "Harmonics": 10,  "Timbre": 127, "Morph": 127},
    "10 FX - Clang":            {"Engine": 10, "Harmonics": 127, "Timbre": 80,  "Morph": 70},
}


def emit_xml() -> str:
    """Build the full preset bundle XML in declaration order."""
    lines = ['<?xml version="1.0" encoding="utf-8"?>']
    lines.append('<PresetDictionary>')
    for preset_name, overrides in PRESETS.items():
        lines.append(f'  <Item Key="{preset_name}">')
        lines.append(f'    <Preset Machine="{MACHINE_NAME}">')
        lines.append('      <Parameters>')
        # Emit parameters in declaration-order index (per Build §3.3).
        for pname, idx in PARAM_INDEX.items():
            value = overrides.get(pname, DEFAULTS[pname])
            lines.append(
                f'        <Parameter Name="{pname}" Group="1" '
                f'Index="{idx}" Track="0" Value="{value}" />'
            )
        lines.append('      </Parameters>')
        lines.append('      <Attributes />')
        lines.append('      <Comment></Comment>')
        lines.append('    </Preset>')
        lines.append('  </Item>')
    lines.append('</PresetDictionary>')
    return "\n".join(lines) + "\n"


if __name__ == "__main__":
    xml = emit_xml()
    # UTF-8 with BOM per Build §3.1.
    with open(OUTPUT_FILE, "w", encoding="utf-8-sig") as f:
        f.write(xml)
    print(f"Wrote {len(PRESETS)} presets to {OUTPUT_FILE}")
