# Authoritative engineering CAD

`source/fixture.step` and `source/welded_part.step` preserve the exact supplied
exports. Their confirmed shared origin defines one nominal assembly with
identity fixture-to-workpiece pose. Neither file proves physical accuracy.
Original engineering inputs stay outside Unity Assets.

Edit nominal definitions in `authoring.json` and semantic selections in
`selections.json`; follow [the workflow](../../docs/spatial-content-authoring.md).
The owner-selected T-joint, directed travel and 15 mm L-shaped cleaning bands
are recorded in `semantic-selection.md` and baked from finite CAD supports.
Two known alternative QR print specifications are recorded without selection.
Installed marker plane, station accuracy and tool measurements remain unqualified.

| Source | SHA-256 |
| --- | --- |
| fixture.step | `f4532e591b0d7150a2988378a24e992b930580ca06074af60d434a7f6542d04c` |
| welded_part.step | `647e8ce51ce0f02cda545677b969d484517b526841dc4e88a75a8829103f1117` |

Hashes provide identity and traceability, not authentication. `.gitattributes`
preserves original line endings and source identity.
