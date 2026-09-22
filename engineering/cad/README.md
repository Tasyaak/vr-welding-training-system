# Authoritative engineering CAD

`source/fixture.step` and `source/welded_part.step` preserve the exact supplied
exports. Their confirmed shared origin defines one nominal assembly with
identity fixture-to-workpiece pose. Neither file proves physical accuracy.
Original engineering inputs stay outside Unity Assets.

Edit nominal definitions in `authoring.json` and semantic selections in
`selections.json`; follow [the workflow](../../docs/spatial-content-authoring.md).
No authoritative seam/cleaning selection was supplied. Unknown marker/tool
measurements remain explicitly unknown.

| Source | SHA-256 |
| --- | --- |
| fixture.step | `f4532e591b0d7150a2988378a24e992b930580ca06074af60d434a7f6542d04c` |
| welded_part.step | `647e8ce51ce0f02cda545677b969d484517b526841dc4e88a75a8829103f1117` |

Hashes provide identity and traceability, not authentication. `.gitattributes`
preserves original line endings and source identity.
