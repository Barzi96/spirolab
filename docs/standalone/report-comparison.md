> Current version5.2: use [ui52-delivery.md](ui52-delivery.md) and [ui52-schema.md](ui52-schema.md). The earlier phase record below is retained as history; explicit radio/checkbox selections supersede its UI instructions.

# Report comparison — 5.0 to 5.1

Both MIR photos were inspected alongside v5 JPG/JSON. Photos establish section order and numeric references, not raw signal data. Their identities/stamps are not redistributed.

| Area | Retained / corrected in 5.1 | Remaining difference |
|---|---|---|
| Header | Saved configured center/operator, Persian shaping | Independent application status |
| Date | Saved selected date in Gregorian and Jalali | No date borrowed from HTML |
| Curves | Shared volume x-axis, flow up left, time down right, monochrome styles, dotted grid | Reconstructed curves only; no fabricated predicted curve |
| Legend | Original PRE indices, at most three displayed complete maneuvers | All maneuvers remain in JSON |
| Best table | Five measured/predicted/% rows | Independent ratio policy still firmware-unverified |
| Details | Added *FVC, *FEV1, *PEF repeated maxima, first displayed PRE percent, per-maneuver EVol calculation | EVol timing unverified |
| Interpretation | Saved clinician text or labeled independent algorithm | No copied Normal Spirometry / Grade A default |
| Quality/VEXT | Short status; reviewed manual VEXT or unavailable | Automatic grading/selection pending |
| Footer | Independent5.1, exam ID, actual PRE index | No copied stamp, signature or fake serial |

JPG remains actual JPEG quality97, 2400x3394, 300DPI. Shared renderer serves preview/export/print. Added details fit y1170–1456; interpretation starts1462 and quality1505, with no overlap in inspected output.

Delivery Comparison-5.1/Before-5.0 and After-5.1 contain synthetic JPG/JSON pairs; index.html displays them side by side. Both use existing simulator seeds100–102, age35/height175/weight75. Header/date fields demonstrate the new settings/date behavior. These are not paired live acquisitions or medical parity evidence. Default-center output and separate UI/settings previews were visually inspected on this host.

## 5.1.1 correction
The chosen primary is explicitly identified in header, legend and footer with its original PRE number and selection reviewer. Primary plus up to two recommended traces replaces the purely automatic display subset; the aggregate best-values table is unchanged. The inspected development example has129 attempts, primary PRE125 and recommendation PRE45. See [selection addendum](maneuver-selection-addendum.md).

## 5.3.2 — printed-sheet layout

The JPG sheet now follows the clinic's printed MIR/Spirolab sheet instead of the previous independent design:

| Area | Now printed |
|---|---|
| Header | Small centred centre name; left `ERS / ECCS 1993`; centre `PRE \| Site: #n`; right operator. No status banner. |
| Patient block | `Test Date … BIPS 1.092 ATS/ERS`, `Name`, `Birth Date … #ID`, `Age … Height cm … Weight kg … Gender`, `Origin … Predicted ERS`, `PRE File N°`. |
| Curves | Two white panels (flow–volume left, volume–time right), thin dotted grid, small ticks, `FLOW (L/s)`, `VOLUME (L)`, `TIME (s)`. Legend `PRE #1 PRE #2 PRE #3 Pred.`; no `[SITE]` marker. |
| BEST VALUES | Parameter, `Pred.`, `Measured`, `%Pred.` — no UNIT column. |
| Parameter table | `PARAMETER | Pred. | PRE#1 | %Pred. | PRE#2 | PRE#3`; units beside parameter names; no section title. |
| Interpretation | `INTERPRETATION:` + one short line, `QUALITY CONTROL GRADE`, `EXHALE ALL air in the lungs`, `REPEATABILITY: FVC, FEV1`. |
| Footer | `Made by spirolab    Ver 4.8` (report layout version) and `SN <actual device serial or —>`. |

Development status banners, unvalidated/algorithmic labels, examination IDs, selection provenance lines and engineering notes are no longer printed. The predicted curve is a schematic envelope built only from the ERS predicted FVC, FEV1 and PEF (a reference shape, never presented as a recorded measurement). `QUALITY CONTROL GRADE` prints the reviewed grade only; when no grade was reviewed it prints `—` rather than an invented `A`. The footer keeps the real device serial (or `—`) and does not copy the reference photo's serial. An optional clinic stamp is drawn at the lower right when `%APPDATA%\SpiroLab\stamp.png` exists; otherwise the area stays blank without moving other blocks.
