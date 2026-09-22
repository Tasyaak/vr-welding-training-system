# Weld-path evaluation

`WeldPathEvaluator` is pure Group B math over Workpiece-local synthetic or mapped values. It
projects onto a directed polyline spline near prior arc length, reports normalized progress and
tangential/lateral/normal/total errors, detects reverse travel, skipped jumps and ambiguous
projections, and uses actual monotonic sample time for signed and exponentially filtered speed.

Speed classification uses frozen profile limits. Travel angle is signed about the authored outward
surface normal from seam tangent toward measured motion; it is invalid at rest. Work angle is the
unsigned angle between tool forward and the surface normal. Missing tracking, stale/nonmonotonic
time, registration/origin mismatch and gaps reset derivative history; no velocity bridges them.

Run `WeldPathEvaluatorTests` in Unity EditMode. Synthetic straight and curved splines require no
Group A source or Quest hardware. Issue #58 maps validated QR-registration/tool snapshots into the
same Workpiece-local contract without changing these rules.
