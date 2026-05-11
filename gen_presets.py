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

Adding new parameters in future Pedal Plaits versions:
  1. Append new entries at the END of PARAM_INDEX (do not reorder).
  2. Append matching entries to DEFAULTS.
  3. Existing preset overrides keep working — they reference parameters
     by name, and the new ones just default in.
"""

MACHINE_NAME = "Pedal Plaits"
OUTPUT_FILE  = "Pedal Plaits_Presets.prs.xml"

# Source declaration order (Pedalplaits.cs lines 84-117). Adding a new
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

# 50 presets, 5 per engine. Names follow PedalInvFFT §23.1 convention:
# "Category - Description". "(AUX)" suffix flags presets where the AUX
# output is the intended use case rather than OUT.
#
# Percussive engines (7-9) ignore LPG Response and Decay — they don't
# appear in those presets' overrides.
PRESETS = {
    # ── Engine 0 — Virtual Analog ──
    "Lead - Saw":          {"Engine": 0, "Harmonics": 30,  "Morph": 90,  "LPG Response": 80,  "Decay": 60},
    "Lead - Square":       {"Engine": 0, "Harmonics": 10,  "Timbre": 127, "Morph": 20, "LPG Response": 100, "Decay": 70},
    "Bass - Pulse":        {"Engine": 0, "Harmonics": 10,  "Timbre": 30,  "Morph": 20, "LPG Response": 100, "Decay": 30},
    "Pad - Detune":        {"Engine": 0, "Harmonics": 100, "Timbre": 80,  "Morph": 80, "LPG Response": 20,  "Decay": 120},
    "FX - Ring (AUX)":     {"Engine": 0, "Harmonics": 50,  "Timbre": 60,  "Morph": 80, "LPG Response": 100, "Decay": 100},

    # ── Engine 1 — Waveshaping ──
    "Lead - Tanh":         {"Engine": 1, "Harmonics": 70,  "Timbre": 40,  "Morph": 64, "LPG Response": 80,  "Decay": 50},
    "Bass - Acid":         {"Engine": 1, "Harmonics": 110, "Timbre": 60,  "Morph": 25, "LPG Response": 20,  "Decay": 50},
    "Drone - Fold":        {"Engine": 1, "Harmonics": 50,  "Timbre": 120, "Morph": 70, "LPG Response": 80,  "Decay": 127},
    "Pluck - Glass":       {"Engine": 1, "Harmonics": 90,  "Timbre": 30,  "Morph": 50, "LPG Response": 100, "Decay": 20},
    "FX - Aggressive":     {"Engine": 1, "Harmonics": 127, "Timbre": 127, "Morph": 80, "LPG Response": 80,  "Decay": 60},

    # ── Engine 2 — Two-op FM ──
    # v1.1 — HARMONICS now snaps to ratios { 0.25, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 4.0, 5.0, 7.0, 11.0 }.
    # Each ratio occupies ~12 HARMONICS units. Index = (int)(Harmonics/127 * 11).
    # HARMONICS=40 → ratio 1.0; =50 → 1.5; =64 → 2.0; =80 → 3.0; =110 → 7.0.
    "EP - Tine":           {"Engine": 2, "Harmonics": 40,  "Timbre": 40,  "Morph": 64, "LPG Response": 100, "Decay": 60},
    "Bell":                {"Engine": 2, "Harmonics": 80,  "Timbre": 80,  "Morph": 64, "LPG Response": 80,  "Decay": 90},
    "Brass":               {"Engine": 2, "Harmonics": 50,  "Timbre": 100, "Morph": 80, "LPG Response": 100, "Decay": 40},
    "Bass - Clang":        {"Engine": 2, "Harmonics": 110, "Timbre": 80,  "Morph": 40, "LPG Response": 100, "Decay": 40},
    "Pad - DX":            {"Engine": 2, "Harmonics": 40,  "Timbre": 60,  "Morph": 64, "LPG Response": 10,  "Decay": 120},

    # ── Engine 3 — Harmonic additive ──
    "Organ":               {"Engine": 3, "Harmonics": 20,  "Timbre": 20,  "Morph": 80, "LPG Response": 127, "Decay": 120},
    "Lead - Sine":         {"Engine": 3, "Harmonics": 10,  "Timbre": 64,  "Morph": 10, "LPG Response": 100, "Decay": 30},
    "Bell - Glass":        {"Engine": 3, "Harmonics": 64,  "Timbre": 110, "Morph": 20, "LPG Response": 80,  "Decay": 70},
    "Pluck - Wood":        {"Engine": 3, "Harmonics": 40,  "Timbre": 64,  "Morph": 30, "LPG Response": 100, "Decay": 20},
    "Pad - Vox":           {"Engine": 3, "Harmonics": 80,  "Timbre": 80,  "Morph": 70, "LPG Response": 20,  "Decay": 127},

    # ── Engine 4 — Wavetable ──
    "Pad - Interp":        {"Engine": 4, "Harmonics": 30,  "Timbre": 80,  "Morph": 64, "LPG Response": 20,  "Decay": 120},
    "Lead - Tilt":         {"Engine": 4, "Harmonics": 0,   "Timbre": 110, "Morph": 80, "LPG Response": 100, "Decay": 60},
    "Vox - Formant":       {"Engine": 4, "Harmonics": 70,  "Timbre": 80,  "Morph": 64, "LPG Response": 60,  "Decay": 110},
    "Bell - Inharm":       {"Engine": 4, "Harmonics": 50,  "Timbre": 64,  "Morph": 80, "LPG Response": 80,  "Decay": 70},
    "FX - 8-bit (AUX)":    {"Engine": 4, "Harmonics": 110, "Timbre": 64,  "Morph": 64, "LPG Response": 100, "Decay": 50},

    # ── Engine 5 — Granular cloud ──
    "Pad - Supersaw":      {"Engine": 5, "Harmonics": 40,  "Timbre": 100, "Morph": 127, "LPG Response": 100, "Decay": 110},
    "Pad - Cloud":         {"Engine": 5, "Harmonics": 90,  "Timbre": 40,  "Morph": 80,  "LPG Response": 40,  "Decay": 120},
    "Lead - Ensemble":     {"Engine": 5, "Harmonics": 30,  "Timbre": 80,  "Morph": 80,  "LPG Response": 80,  "Decay": 60},
    "FX - Sparkle (AUX)":  {"Engine": 5, "Harmonics": 64,  "Timbre": 110, "Morph": 110, "LPG Response": 100, "Decay": 90},
    "Pluck - Pulses":      {"Engine": 5, "Harmonics": 10,  "Timbre": 20,  "Morph": 10,  "LPG Response": 100, "Decay": 30},

    # ── Engine 6 — Filtered noise (note column sets cutoff, not pitch) ──
    "FX - Wind":           {"Engine": 6, "Harmonics": 64,  "Timbre": 100, "Morph": 40,  "LPG Response": 100, "Decay": 90},
    "FX - Resonant":       {"Engine": 6, "Harmonics": 64,  "Timbre": 100, "Morph": 120, "LPG Response": 100, "Decay": 90},
    "Pluck - Breath":      {"Engine": 6, "Harmonics": 70,  "Timbre": 110, "Morph": 80,  "LPG Response": 100, "Decay": 20},
    "Pad - Sweep":         {"Engine": 6, "Harmonics": 0,   "Timbre": 80,  "Morph": 80,  "LPG Response": 20,  "Decay": 120},
    "FX - Steppy":         {"Engine": 6, "Harmonics": 64,  "Timbre": 20,  "Morph": 80,  "LPG Response": 100, "Decay": 40},

    # ── Engine 7 — Bass drum (percussive — LPG/Decay no-op) ──
    "Kick - 808":          {"Engine": 7, "Harmonics": 80,  "Timbre": 80,  "Morph": 80},
    "Kick - 909 (AUX)":    {"Engine": 7, "Harmonics": 110, "Timbre": 64,  "Morph": 70},
    "Kick - Hard":         {"Engine": 7, "Harmonics": 127, "Timbre": 100, "Morph": 40},
    "Kick - Sub":          {"Engine": 7, "Harmonics": 20,  "Timbre": 20,  "Morph": 120},
    "Tom":                 {"Engine": 7, "Harmonics": 60,  "Timbre": 80,  "Morph": 50},

    # ── Engine 8 — Snare drum (percussive) ──
    "Snare - 909":         {"Engine": 8, "Harmonics": 64,  "Timbre": 64,  "Morph": 64},
    "Snare - Hard":        {"Engine": 8, "Harmonics": 80,  "Timbre": 80,  "Morph": 80},
    "Rim":                 {"Engine": 8, "Harmonics": 20,  "Timbre": 40,  "Morph": 20},
    "Tom-Snare":           {"Engine": 8, "Harmonics": 100, "Timbre": 110, "Morph": 80},
    "Snare - FM (AUX)":    {"Engine": 8, "Harmonics": 70,  "Timbre": 80,  "Morph": 70},

    # ── Engine 9 — Hi-hat (percussive) ──
    "Hat - 808 Closed":    {"Engine": 9, "Harmonics": 100, "Timbre": 80,  "Morph": 20},
    "Hat - 808 Open":      {"Engine": 9, "Harmonics": 100, "Timbre": 80,  "Morph": 80},
    "Hat - 707 (AUX)":     {"Engine": 9, "Harmonics": 80,  "Timbre": 80,  "Morph": 40},
    "Cymbal":              {"Engine": 9, "Harmonics": 110, "Timbre": 60,  "Morph": 110},
    "Hat - Noise":         {"Engine": 9, "Harmonics": 30,  "Timbre": 110, "Morph": 40},
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
