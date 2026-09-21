# Git and GitHub workflow

`main` is protected by review and CI. Work in one issue-focused branch, keep
commits reviewable, and never commit generated Unity data or local secrets.

## Standard flow

```text
git fetch origin
git switch main
git pull --ff-only
git switch -c feature/<issue>-<short-name>
python tests/repository_checks.py
git status
git add <explicit paths>
git commit -m "Describe the outcome"
git push -u origin feature/<issue>-<short-name>
```

Pull requests must describe scope, architecture impact, tests run, Unity Editor
changes, Quest evidence and known pending hardware/device verification. A
pending PR is proposed code until merged; documentation must not describe it as
integrated.

## Repository rules

- Keep `Assets`, `Packages` and `ProjectSettings`; never track `Library`, `Temp`,
  `Obj`, `Logs`, `.utmp`, builds, recordings or IDE state.
- Commit Unity `.meta` files with their assets; do not manually invent GUIDs.
- Never commit tokens, keystores, participant identity or station-local data.
- Do not create external-device, network, QR, second-controller or hardware
  fallback paths; the approved MVP is Quest-only.
- Package changes require a dedicated rationale and lockfile review.
- Preserve user work in dirty trees; use a separate worktree for isolated work.

## Verification

Run repository checks for every change. Run Unity EditMode/PlayMode tests for
affected modules and a clean import/compile for scene/package changes. Device
features require standalone Quest evidence; Editor behavior is not a substitute.

## Historical work

Old PRs and issues may describe the superseded architecture. Their disposition
and reusable parts are listed in `quest-only-migration.md`; do not merge an old
stack merely because its checks passed at the time.
