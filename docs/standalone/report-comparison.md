# Report comparison

The supplied photograph defines section order: header, combined flow/volume and inverted time/volume axes, PRE legend, BEST VALUES, maneuver parameter table, interpretation/quality/repeatability and footer. The independent renderer follows this structure with white portrait paper, monochrome differentiated curves, dotted grid, plain numeric tables and Persian-capable names.

The output deliberately differs where copying the photograph would be misleading:

- No stamp, signature, scanner noise, original center identity or original patient data is copied into synthetic samples.
- Application footer identifies Independent SpiroLab 5, not original MIR output.
- No predicted flow curve is invented from a handful of predicted indices; only supported scalar reference values are displayed.
- Original maneuver PRE indices are preserved, including when three maneuvers are selected from five or more.
- Estimated BEV is not displayed as verified extrapolated volume; VEXT is unavailable in both outputs.
- Algorithmic interpretation is explicitly UNVALIDATED. Quality grade is unavailable unless entered manually. Test samples are prominently NON-CLINICAL TEST DATA. No clinical grade or diagnosis from the photograph is copied.

The package Examples/Spirometry-Report.jpg and Examples/Spirometry-Data.json are the same synthetic finalized snapshot. Preview and export use the same renderer; JPEG uses a real encoder at 97 by default, 2400×3394, 300 DPI. Original reference remains Downloads/spiro.jpg on the development PC and is intentionally not redistributed with patient information.
