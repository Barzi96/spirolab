#!/usr/bin/env python3
"""Generate the deterministic synthetic phase-1 fixtures.

These bytes are NOT a MIR measurement. They exercise offsets, unknown-byte
preservation, packet display and the comparison UI without patient data.
"""
import csv
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FIX = ROOT / "fixtures"
FIX.mkdir(parents=True, exist_ok=True)

last = bytearray([0xA5] * 0x60)
# Observed source-level offsets; values chosen only to exercise the decoder.
last[0x0A:0x0C] = bytes([0x00, 0x01])       # unknown quality/reserved raw
last[0x10:0x12] = (432).to_bytes(2, "big")  # FVC 4.32 L
last[0x12:0x14] = (351).to_bytes(2, "big")  # FEV1 3.51 L
last[0x14:0x16] = (876).to_bytes(2, "big")  # PEF 8.76 L/s
last[0x1A:0x1C] = (642).to_bytes(2, "big")  # FET 6.42 s
last[0x50:0x52] = (12).to_bytes(2, "big")   # PEF time 0.12 s

packets = []
# Structural triangular flow, 0.02 s steps, 2.40 s total. PEF = 3.00 L/s.
# This is a plumbing fixture, not a physiological reconstruction.
for i in range(1, 121):
    t = i * 0.02
    if t <= 0.20:
        flow = 3.00 * (t / 0.20)
    else:
        flow = 3.00 * ((2.40 - t) / 2.20)
    raw = max(0, min(65535, int(round(flow * 100))))
    packets.append({
        "index": i - 1,
        "seconds": round(t, 4),
        "type": "0x10",
        "raw": raw,
        "hex": "10%04X" % raw,
        "notes": "SYNTHETIC structural packet; not MIR data",
    })

capture = {
    "schema": "mir-parity-capture/v1",
    "test_id": "SYN-STRUCT-001",
    "data_origin": "synthetic",
    "capture": {
        "recorded_at": "2026-01-15T09:00:00Z",
        "device_model": "SYNTHETIC fixture — not a device",
        "device_serial": "",
        "notes": "Deterministic synthetic capture for UI, parser and arithmetic tests only.",
    },
    "maneuvers": [
        {
            "maneuver_key": "SYN-STRUCT-001-M1",
            "sequence": 1,
            "pre_post": "PRE",
            "recorded_at": "2026-01-15T09:00:00Z",
            "association": "capture_bound_immediate_single_read",
            "association_evidence": "SYNTHETIC fixture metadata; not a real capture token.",
            "step_type": "0x40",
            "step": 0.02,
            "cod_last_hex": last.hex().upper(),
            "cod_curve_hex": "A5000102030405060708090A0B0C0D0E0F",
            "packets": packets,
        }
    ],
}
(FIX / "synthetic-capture.json").write_text(
    json.dumps(capture, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
)

rows = [
    ["SYN-STRUCT-001", "SYN-STRUCT-001-M1", "1", "PRE", "2026-01-15T09:00:00Z",
     "FVC", "4.32", "L", "mir_device_print", "0.01",
     "SYNTHETIC official-style value; not a real MIR printout", "yes",
     "Fixture only; no patient data"],
    ["SYN-STRUCT-001", "SYN-STRUCT-001-M1", "1", "PRE", "2026-01-15T09:00:00Z",
     "FEV1", "3.51", "L", "mir_device_print", "0.01",
     "SYNTHETIC official-style value; not a real MIR printout", "yes",
     "Fixture only; no patient data"],
    ["SYN-STRUCT-001", "SYN-STRUCT-001-M1", "1", "PRE", "2026-01-15T09:00:00Z",
     "PEF", "8.76", "L/s", "mir_device_print", "0.01",
     "SYNTHETIC official-style value; not a real MIR printout", "yes",
     "Fixture only; no patient data"],
    ["SYN-STRUCT-001", "SYN-STRUCT-001-M1", "1", "PRE", "2026-01-15T09:00:00Z",
     "FET", "6.42", "s", "mir_device_print", "0.01",
     "SYNTHETIC official-style value; not a real MIR printout", "yes",
     "Fixture only; no patient data"],
    ["SYN-STRUCT-001", "SYN-STRUCT-001-M1", "1", "PRE", "2026-01-15T09:00:00Z",
     "PEF time", "0.12", "s", "mir_device_print", "0.01",
     "SYNTHETIC official-style value; not a real MIR printout", "yes",
     "Fixture only; no patient data"],
    ["SYN-STRUCT-001", "SYN-STRUCT-001-M1", "1", "PRE", "2026-01-15T09:00:00Z",
     "FEV1/FVC", "81.3", "%", "mir_device_print", "0.1",
     "SYNTHETIC official-style value; not a real MIR printout", "yes",
     "Fixture only; no patient data"],
]
with (FIX / "synthetic-official.csv").open("w", newline="", encoding="utf-8") as fh:
    writer = csv.writer(fh)
    writer.writerow(["test_id", "maneuver_key", "sequence", "pre_post", "recorded_at",
                     "parameter", "value", "unit", "source", "precision", "evidence",
                     "same_maneuver_attested", "notes"])
    writer.writerows(rows)

print("wrote", FIX / "synthetic-capture.json")
print("wrote", FIX / "synthetic-official.csv")
