# Semantic feedback engine

`SemanticFeedbackEngine` is the single engine-independent source of truth for visual, audio, and haptic feedback consumers. It consumes immutable evaluator/interlock semantics and emits one `FeedbackState`; presentation code must not reinterpret activation permission or recompute tolerances.

Mandatory operational cues (tracking/registration loss, E-stop, reflection/safety state, and activation blockers) override coaching immediately and remain visible at every assistance level. Optional coaching scales from 0 to 100 percent across visual, audio, haptic, and cadence mappings. Assistance never changes geometry, thresholds, input semantics, metric eligibility, progress, or scores.

The engine rejects non-monotonic time, marks invalid or generation-mismatched samples ineligible for metrics, and applies a configurable minimum duration only to ordinary coaching. Tests use synthetic inputs and require no QR, CAD, Meta, scene, or Group A implementation. Issue #15 adapts this state to Unity modalities; issue #57 exposes the assistance setting. Production wiring belongs to #58.
