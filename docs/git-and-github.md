# Git and GitHub workflow

Work in reviewable issue-focused branches; keep main protected by review/CI.
Do not commit generated Unity data, credentials or participant information.

## Standard flow

```text
git fetch origin
git switch main
git pull --ff-only
git switch -c codex/<issue>-<short-name>
python tests/repository_checks.py
git add <explicit paths>
git commit -m "Describe the outcome"
git push -u origin codex/<issue>-<short-name>
```

Preserve dirty work; use an isolated worktree when necessary. PRs describe final
behavior, architecture impact, relevant checks and actual/pending device tests.
Proposed code is not integrated code until merged.

## Parallel ownership

Use [roadmap](roadmap.md) and [contract v1](parallel-development-contract.md).
A spatial issues and B training issues have no cross-group implementation
blockers; #58 owns the production join. Commit synthetic fixtures with B and
adapter tests with A. Do not change another group's contract semantics without
updating the document and both conformance suites.

Existing unmerged PRs are excluded from this revised plan. Do not wait for or
adopt them as prerequisites. Historical comments may describe older decisions;
the revised issue body and current architecture documents define active scope.

## Repository rules and verification

- Commit Unity assets with their actual .meta files; never invent GUIDs.
- Do not track Library/Temp/Obj/Logs/.utmp/builds/recordings.
- Keep firmware, Hall, coil and external networking removed.
- Fixture-mounted QR is required; no four-point fallback or left-controller path.
- Package/config changes need a rationale and lockfile review.
- Run repository checks for every PR; run relevant model/PlayMode tests for code.
- Unity/Quest behavior needs actual evidence; docs-only checks do not qualify it.
- Close redundant issues with an explanatory replacement link rather than
  deleting their history; avoid multiple active trackers for the same feature.
