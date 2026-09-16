# Git and GitHub Guide

This document explains what Git and GitHub are and how to use them in this project.

The intended workflow is:

```text
updated main
    ↓
task branch
    ↓
implement and test
    ↓
commit
    ↓
push branch to GitHub
    ↓
Pull Request
    ↓
review + CI + hardware testing when relevant
    ↓
Squash and merge
    ↓
update local main
```

The repository is a monorepo containing the Unity application, ESP32 firmware, protocol documentation, tests, analysis tools, and project documentation.

---

## 1. Git vs GitHub

### Git

**Git** is a distributed version-control system.

Git runs on your computer and records the history of files in the repository. It allows developers to:

- see what changed;
- create checkpoints of work;
- work on separate branches;
- compare versions;
- combine changes from multiple developers;
- return to earlier states when necessary.

Git does not require GitHub to create commits or branches.

### GitHub

**GitHub** hosts a shared Git repository online and adds collaboration features such as:

- Pull Requests;
- Issues;
- code review;
- branch protection;
- GitHub Actions / CI;
- web-based repository browsing.

In this project, Git manages the source history and GitHub is the shared collaboration platform.

---

## 2. Repository terms

### Working tree

The **working tree** is the set of files currently present in your local clone.

When you edit a C# file, Unity asset, Markdown document, or firmware file, you are changing the working tree.

Check its state with:

```bash
git status
```

A shorter form is:

```bash
git status --short
```

### Staging area

The **staging area** contains the changes selected for the next commit.

For example:

```bash
git add unity/Assets/Trainer/Domain
git add protocol/specification.md
```

`git add` does not upload anything to GitHub. It only selects changes for the next local commit.

Inspect staged changes with:

```bash
git diff --cached
```

### Commit

A **commit** is a local recorded checkpoint in repository history.

Create one with:

```bash
git commit -m "Add weld travel-speed evaluation"
```

A good commit should describe one coherent change.

Examples:

```text
Add QR workpiece registration
Handle stale Hall telemetry
Document tool coordinate frames
Fix UDP reconnect state
```

A commit remains only on your computer until it is pushed.

### Remote

A **remote** is a named connection to another Git repository.

The GitHub repository is normally called:

```text
origin
```

Check it with:

```bash
git remote -v
```

### Push

A **push** uploads your local commits and branch reference to GitHub.

Example:

```bash
git push
```

Pushing a feature branch does not merge it into `main`.

### Fetch

`git fetch` downloads information about remote commits and branches without changing your current working files.

```bash
git fetch origin
```

### Pull

`git pull` downloads remote changes and integrates them into the current local branch.

For local `main`, this project uses:

```bash
git pull --ff-only
```

`--ff-only` prevents Git from silently creating an unexpected merge commit if local and remote history diverge.

---

## 3. Branches

A **branch** is a named line of development.

The authoritative integrated branch is:

```text
main
```

Normal implementation work should not be performed directly on `main`.

Create a short-lived branch for one task:

```bash
git switch -c feature/qr-registration
```

Suggested branch prefixes:

```text
feature/   new functionality
fix/       bug fix
docs/      documentation only
refactor/  restructuring without intended behavior change
chore/     tooling, dependencies, repository maintenance
```

Examples:

```text
feature/qr-registration
feature/travel-speed-feedback
feature/12-hall-telemetry
fix/udp-reconnect
docs/quest-setup
chore/update-meta-xr
```

Branches are useful because developers can work on independent tasks without changing shared `main`.

There are no permanent `unity`, `firmware`, `developer-name`, or `develop` branches. A branch represents a task, not a person or subsystem.

---

## 4. Switching branches

Before switching branches:

```bash
git status
```

Prefer a clean working tree.

If Unity is open and the branches contain different scenes, assets, packages, or ProjectSettings, close Unity before switching.

Switch to an existing branch:

```bash
git switch branch-name
```

Switch back to `main`:

```bash
git switch main
```

Create and switch to a new branch:

```bash
git switch -c feature/example
```

After changing branches, reopen Unity and allow it to reimport any changed assets.

---

## 5. `.gitignore`

`.gitignore` tells Git which untracked files or directories should normally be ignored.

This project excludes generated/local data such as:

```text
unity/Library/
unity/Temp/
unity/Logs/
unity/UserSettings/
```

These files are recreated locally by Unity and should not be shared through Git.

`.gitignore` also protects local firmware configuration such as secrets when corresponding patterns are configured.

Important:

- `.gitignore` does not remove a file that is already tracked;
- do not add important source assets to `.gitignore` simply to avoid a Git problem;
- do not ignore required Unity `.meta` files.

To check whether a path is ignored:

```bash
git check-ignore -v path/to/file
```

---

## 6. `.gitattributes`

`.gitattributes` controls how Git treats specific file types.

Typical responsibilities in this project include:

- normalizing line endings between Windows and macOS;
- identifying text files;
- identifying binary files that should not be text-merged.

This is especially useful because the team develops on both Windows and macOS.

`.gitattributes` is version-controlled and should normally be changed only deliberately.

It is different from `.gitignore`:

- `.gitignore` decides whether untracked files should be ignored;
- `.gitattributes` defines handling rules for files that are part of the repository.

---

## 7. Unity-specific Git rules

### Commit `.meta` files

Unity `.meta` files contain asset identifiers and must be committed with the assets they belong to.

For example:

```text
MyPrefab.prefab
MyPrefab.prefab.meta
```

Do not casually delete or regenerate another asset's `.meta` file.

### Keep generated folders out of Git

Do not commit:

```text
unity/Library/
unity/Temp/
unity/Logs/
unity/UserSettings/
```

### Keep shared project configuration

The following are important shared project files and should be committed:

```text
unity/Assets/
unity/Packages/manifest.json
unity/Packages/packages-lock.json
unity/ProjectSettings/
```

### Package changes are shared changes

Installing, removing, or updating a Unity package can modify:

```text
unity/Packages/manifest.json
unity/Packages/packages-lock.json
```

Treat dependency changes as explicit project changes, not personal local configuration.

### Avoid simultaneous scene/prefab editing

Git can merge normal text files more reliably than complex Unity scenes and prefabs.

When practical, avoid having multiple developers make major changes to the same scene or prefab at the same time.

---

## 8. Starting work on a task

Start from an updated `main`.

```bash
git status
git switch main
git pull --ff-only
```

Create the task branch:

```bash
git switch -c feature/travel-speed-feedback
```

Now implement the task.

Before committing:

```bash
git status --short
git diff
```

Stage only relevant files:

```bash
git add unity/Assets/Trainer
```

or individual files:

```bash
git add unity/Assets/Trainer/Domain/TravelSpeedEvaluator.cs
git add unity/Assets/Trainer/Domain/TravelSpeedEvaluator.cs.meta
```

Inspect staged content:

```bash
git diff --cached
```

Commit:

```bash
git commit -m "Add travel-speed evaluation"
```

You can make several commits while working on a task.

---

## 9. Publishing a branch

First push:

```bash
git push -u origin feature/travel-speed-feedback
```

After the upstream branch is configured:

```bash
git push
```

The branch is now available on GitHub for review.

---

## 10. Issues

A GitHub **Issue** represents a unit of work, problem, investigation, or discussion.

Typical Issues in this project could be:

```text
Implement QR workpiece registration
Add Hall telemetry protocol
Create travel-speed evaluator
Investigate MR anchor drift
Fix session log rotation
```

Issues help the team:

- define work before implementation;
- assign responsibility;
- discuss requirements;
- record decisions;
- link implementation to a specific task.

### Creating an Issue on GitHub

Open:

```text
Repository → Issues → New issue
```

A useful Issue should include:

- a clear title;
- the problem or goal;
- acceptance criteria when useful;
- hardware requirements if relevant;
- dependencies or known constraints.

Assign the Issue to the developer responsible for it.

### Linking a branch/PR to an Issue

If the Issue number is `12`, a branch may be named:

```text
feature/12-hall-telemetry
```

A Pull Request description can include:

```text
Closes #12
```

GitHub will close Issue `#12` automatically when that PR is merged.

### GitHub CLI

Create an Issue:

```bash
gh issue create
```

List Issues:

```bash
gh issue list
```

View one:

```bash
gh issue view 12
```

Open it in the browser:

```bash
gh issue view 12 --web
```

---

## 11. Pull Requests

A **Pull Request (PR)** is a proposal to merge one branch into another, normally:

```text
feature branch → main
```

A PR is used to:

- show exactly what changed;
- discuss the implementation;
- run CI;
- record test evidence;
- request review;
- approve or reject the change before it reaches `main`.

A PR is not the same thing as `git pull`.

### Create a PR using the GitHub website

After pushing the branch:

1. open the repository on GitHub;
2. choose **Compare & pull request**, or open **Pull requests → New pull request**;
3. verify:
   - base: `main`;
   - compare: your task branch;
4. describe what changed and how it was tested;
5. create a Draft PR if implementation/testing is incomplete;
6. request another developer's review when ready.

If more commits are pushed to the same branch, the existing PR updates automatically.

Do not open a new PR for every review fix.

### Create a PR with GitHub CLI

From the task branch:

```bash
gh pr create --base main --fill
```

Create a draft:

```bash
gh pr create --base main --draft --fill
```

Open the web form:

```bash
gh pr create --web
```

Inspect the current PR:

```bash
gh pr view
```

Open it in a browser:

```bash
gh pr view --web
```

Check CI:

```bash
gh pr checks
```

Mark a draft ready:

```bash
gh pr ready
```

---

## 12. Reviewing a Pull Request

The reviewer should inspect:

- correctness;
- scope;
- architecture boundaries;
- protocol compatibility;
- generated/unwanted files;
- test results;
- Quest/ESP32 evidence when the change affects hardware.

GitHub review choices are:

- **Comment** — discussion without approval;
- **Approve** — ready to merge subject to repository checks;
- **Request changes** — blocking problems remain.

Do not approve your own PR.

### Review using GitHub CLI

Inspect:

```bash
gh pr view 42
gh pr diff 42
```

Check out locally:

```bash
gh pr checkout 42
```

Approve:

```bash
gh pr review 42 --approve
```

Request changes:

```bash
gh pr review 42 --request-changes --body "Describe the blocking issue."
```

Comment:

```bash
gh pr review 42 --comment --body "Review comment."
```

---

## 13. Synchronizing a branch with `main`

If `main` changes while your branch is in progress, first commit your current work.

While still on the task branch:

```bash
git fetch origin
git merge --no-edit origin/main
```

Then resolve conflicts if necessary, retest, and push:

```bash
git push
```

For this project, use merge-based synchronization for already published task branches.

Do not routinely rebase shared/published branches and do not force-push merely to keep history visually clean.

---

## 14. Merge conflicts

A **merge conflict** occurs when Git cannot automatically determine how two sets of changes should be combined.

Start with:

```bash
git status
```

For normal source/text files, use VS Code's merge editor.

Do not blindly choose **Accept Both**. The final file must contain the intended combined logic.

After fixing a conflicted file:

```bash
git add path/to/file
```

Inspect:

```bash
git diff --cached
```

Finish the merge:

```bash
git commit
```

Then retest.

For Unity scenes, prefabs, ScriptableObjects, and `.meta` files, resolve conflicts carefully and verify the result in Unity.

---

## 15. Testing before merge

A green CI result is not sufficient for every change.

### Repository/documentation changes

Ensure repository checks pass.

### Unity changes

At minimum:

- open the project using the pinned Unity version;
- let scripts/assets compile;
- verify there are no unexpected Console errors;
- run relevant Edit Mode / Play Mode tests if available.

### Quest/MR changes

Test on a physical Meta Quest 3 when the change affects:

- passthrough;
- QR registration;
- Spatial Anchors;
- Touch Plus tracking;
- controller haptics;
- Android permissions;
- standalone lifecycle.

### Firmware changes

Compile and test the ESP32 firmware using the project's Arduino IDE setup.

### Protocol changes

Test Quest and ESP32 together and update:

```text
protocol/specification.md
```

### Hardware-sensitive changes

Record the actual test result in the PR.

Examples include:

- scan QR, then hide the QR and verify the workpiece remains registered;
- Hall proximity response near workpiece magnets;
- ESP32 reconnect after router/network interruption;
- optional haptic safe-off behavior;
- session logging and ADB export.

Write `Not tested` or `Pending` when a required physical test has not yet been performed.

---

## 16. Merging a PR

The project uses **Squash and merge** for normal task PRs.

Before merging, verify:

- the requested task is complete;
- relevant tests passed;
- required CI passed;
- blocking comments are resolved;
- another developer approved the PR when review is required.

### GitHub website

On the PR page:

1. verify checks and review status;
2. select **Squash and merge**;
3. use a clear final commit title;
4. confirm;
5. delete the merged remote source branch.

### GitHub CLI

```bash
gh pr merge 42 --squash --delete-branch
```

Do not bypass required tests/reviews just to merge quickly.

---

## 17. After merge

Update local `main`:

```bash
git switch main
git pull --ff-only
git fetch --prune origin
```

Delete the old local task branch:

```bash
git branch -d feature/travel-speed-feedback
```

Because squash merging replaces the branch's individual commits with one new commit on `main`, Git can sometimes refuse `-d`.

Only after confirming:

- the PR is marked **Merged** on GitHub;
- local `main` is current;
- there is no unpushed work on the old branch;

remove it with:

```bash
git branch -D feature/travel-speed-feedback
```

Start the next task from updated `main`.

---

## 18. Common mistakes to avoid

Do not:

- develop normal features directly on `main`;
- commit `unity/Library/`, build outputs, logs, or local caches;
- commit Wi-Fi passwords, API tokens, signing secrets, or private configuration;
- delete Unity `.meta` files casually;
- update Unity/package versions inside an unrelated feature;
- force-push `main`;
- routinely force-push shared feature branches;
- assume a PR is correct because Git reports "no conflicts";
- assume CI proves physical MR/hardware behavior;
- create a new PR for every review correction;
- continue new work on an already merged task branch.

Prefer small, coherent, reviewable tasks and merge them frequently.

---

## 19. Quick daily workflow

Start a task:

```bash
git status
git switch main
git pull --ff-only
git switch -c feature/my-task
```

Work, inspect, and commit:

```bash
git status --short
git diff
git add <files>
git diff --cached
git commit -m "Describe the change"
```

Publish and create PR:

```bash
git push -u origin feature/my-task
gh pr create --base main --fill
```

After review fixes:

```bash
git add <files>
git commit -m "Address review feedback"
git push
```

After merge:

```bash
git switch main
git pull --ff-only
git fetch --prune origin
git branch -d feature/my-task
```
