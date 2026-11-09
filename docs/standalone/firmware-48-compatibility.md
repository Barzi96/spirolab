# Firmware 4.8 compatibility — SpiroLab 5.4.6

## Scope and evidence

The existing direct MIRUSB transport, FVC command, packet decoding, capture association and cancellation rules remain in use. No vendor DLL or firmware is bundled, and no firmware-writing or calibration path was added.

Read-only identification on 2026-11-09 observed a restored Spirolab with logical PID 107, USB revision 1000, firmware 4.8, 36-byte SDA, device type 208, BigEndian (1), CodProt 1. Manufacturer communication protocol query 0x0A was unsupported; legacy cod_ON followed by the SDA bulk read worked. The production build's existing maximum-92-byte SDA read also returned this shorter block successfully. No patient examination, FVC start, calibration command or live HLine write was performed.

Offsets were checked against the locally installed manufacturer's `MirParser.MirSpirolabFamily.GetUsbAreaLastStructure` and `MirSpirolabATS19.GetUsbAreaLastStructure`, and identification against `ParserBase.GetDeviceInfo`. This is implementation evidence, not independent clinical validation.

| Field | 4.8 / type 208 | ATS2019 / type 241 |
|---|---:|---:|
| FVC | 14 | 16 |
| FEV1 | 16 | 18 |
| PEF | 18 | 20 |
| FET | 24 | 26 |
| PEFTIME | 74 | 80 |
| Uninterpreted quality word | 10 | 12 |

Core values retain their existing big-endian hundredth-unit conversion. The `LegacySpirolab` result field persists the layout through cloning and XML records; schema-v2 JSON exposes an additive `deviceResult.layout` label. Old records default to their original ATS2019 layout. Device provenance rechecks the same layout's raw core fields; no submission bypass was added. Measurement-source descriptions now carry the actual byte offsets.

## Verification

- Both production and isolated test builds succeeded.
- Eleven suites: **1128 PASS, 0 failed suites, 10 SKIPPED** without the optional external MIR capture (previous baseline 1101 PASS).
- Separate `--require-pending-end` acceptance: **182 PASS**.
- Added synthetic coverage: 36/92-byte SDA and fixed-width serial identification, both core layouts, PEFTIME/quality offsets, unsupported byte order/version, truncated SDA, frozen ownership, XML reopening, old-layout defaults, raw tampering and changed-layout rejection.
- Production `DeviceSession.Connect` identified the connected 4.8 device through direct MIRUSB; the temporary probe was not included in the release.

Live maneuver/output parity on 4.8 and repeat hardware validation on 8.2, calibration/BTPS, clean-PC and production HLine validation remain **PENDING**. The owner's report of successful device recovery is not application measurement validation. Raw device identification and manufacturer binaries are kept outside Git.
