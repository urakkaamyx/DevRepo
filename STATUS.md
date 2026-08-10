# Endo — Implementation Status

This build implements the Endo Architecture Package (`../01-ARCHITECTURE.md` through
`../15-ACCEPTANCE-CRITERIA.md`) in C#/.NET 10, targeting a single `endo`/`endo.exe` binary.

Every claim below is checked against what the code actually does, not what it's meant to
eventually do. Where something is a deliberate simplification of an underspecified area, the
reasoning is stated so a future session can revisit it without re-deriving context.

## How to build and run

```
cd build
dotnet build
dotnet test
# binary at src/Endo.Cli/bin/Debug/net10.0/endo.exe
```

`endo` needs `git` on PATH (used for project Git, DevRepo, and tool source acquisition).

## Solution layout

```
build/
  Endo.sln
  src/
    Endo.Cli/    — argument parsing, interactive prompts, dispatch to CommandEngine
    Endo.Core/   — everything else, organized by roadmap phase (see below)
  tests/
    Endo.Core.Tests/  — 33 xUnit tests, all passing
```

`Endo.Core` is deliberately one assembly with feature-folder namespaces
(`Environment`, `Projects`, `Tools`, `Runtimes`, `Git`, `Restore`, `Ai`, `Commands`, `Setup`,
`Json`, `Diagnostics`, `Processes`) rather than one assembly per phase — splitting further isn't
justified by anything in the spec and would be an unnecessary abstraction.

## Architectural rule enforced in code

> "If AI can perform an operation, a deterministic Endo command must exist for it. AI orchestrates
> commands. AI must not become a second hidden implementation of Endo." — 01-ARCHITECTURE.md

`CommandEngine` (`Endo.Core/Commands/CommandEngine.cs`) is the single dispatch point. Both
`Endo.Cli/Program.cs` and `Endo.Core/Ai/AiOrchestrator.cs` call through it — neither performs
state-changing work any other way. `AiOrchestrator` only ever executes a command name it gets back
from `CommandEngine.ListCommands()`; an unrecognized name is refused, never improvised.

## Phase-by-phase status

### Phase 1 — Core: **Implemented**
- `RootLocator`: `ENDO_ROOT` env var, else a pointer file in the OS per-user config directory
  (written by `endo setup`). Windows path exercised manually; Unix branches exist but are
  untested on this machine.
- `AtomicJsonWriter`: temp file → parse-validate → flush+fsync → `File.Move(overwrite: true)`.
  Covered by `AtomicJsonWriterTests` including an "invalid write never touches existing target"
  case.
- `EnvironmentRepository`: load/save/force-save, `AppendHistory`, `DetectDrift` (compares
  environment.json against the filesystem for projects/tools/runtimes — exactly the four
  categories 03-ENVIRONMENT-SPEC.md's "Drift Detection" names, no more).
- `Logger`: structured JSON-lines to `Cache/Logs/endo.log`, mirrored to console at Info+.
- `CommandResult` / `ICommand` / `CommandEngine`: fields match 02-CLI-SPEC.md's "Command Results"
  list exactly (Success, ExitCode, Output, Error, AffectedState, ChangedFiles, Diagnostics,
  RecoveryInformation).

### Phase 2 — Projects: **Implemented**
- `ProjectService.CreateProject`: `Projects/<Category>/<SubCategory>/<Name>`, `project.json`
  matching the 11-JSON-SCHEMAS-DRAFT.md draft field-for-field, independent `git init`, `.agents/`.
  Registers a `ProjectRef` pointer in environment.json.
- `project.check`: validates directory/project.json/git presence and identity match.
- `project.open`: opens the directory by default; `--ide` overrides for that invocation only and
  is never persisted (per spec, this is an operation-level override, not a saved preference).
- `Tasks.Active` is a `List<string>` everywhere — never a singular field, per explicit spec
  requirement. Tested.
- CLI-level interactivity (`endo project new` with no args prompts for Category/SubCategory/Name)
  is a thin wrapper in `Program.cs` around the same deterministic `project.new` command an AI
  orchestrator would call — one code path either way.

### Phase 3 — Runtimes: **Partially implemented**
- Manifests, multiple coexisting versions, per-project selection (`runtime.set`, writes to
  `project.json`'s `runtime` map), and "latest installed" resolution
  (`RuntimeManifest.LatestInstalled`) are implemented and tested.
- `runtime install` **registers an already-present installation at a caller-given path** rather
  than downloading/building anything. The spec defines an explicit source-first
  clone-build-validate pipeline for *Tools* (05-TOOL-SYSTEM-SPEC.md) but never describes an
  acquisition mechanism for *Runtimes* — inventing one (e.g. auto-downloading Python/Node
  installers) would be exactly the kind of unspecified system this build was told not to invent.
  This is flagged here for a human decision, not silently assumed.
- **No `runtime remove` command.** 02-CLI-SPEC.md's "Runtimes" section defines exactly three
  verbs — `list`, `install`, `set` — and no `remove`. The roadmap's Phase 3 checklist says
  "Removal" as a capability, but since the CLI spec doesn't define that verb's name or signature
  for runtimes (unlike `tool remove <tool> --force`, which the CLI spec does define), adding one
  would be guessing a CLI surface the spec doesn't actually specify. Left out; needs a decision
  from whoever owns the spec, not an invented default.

### Phase 4 — Tools: **Implemented**
- `ToolService.Install` follows 05-TOOL-SYSTEM-SPEC.md's Validation Lifecycle in order: Acquire
  (git clone, source-first) → Scratchpad (always cleared/disposable first) → Documentation
  (locates README.md/.rst/.txt/bare README, recorded in the report) → Setup/Build (optional build
  command) → Test (optional validate command) → bounded retry (2 attempts per step, captured as
  "recovery attempts") → PASS → move out of Scratchpad and register, or FAIL → leave Scratchpad
  evidence in place and return a report with every field 05-TOOL-SYSTEM-SPEC.md's "Failure
  Reports" section lists (repo, ref, docs reviewed, succeeded/failed steps, errors, recovery
  attempts, final reason). Nothing is registered on failure.
- General vs. scoped tools (`Category/SubCategory` key), multiple versions retained indefinitely,
  channels (`latest` updated on install; `stable`/`develop`/`custom` are supported by the data
  model but nothing currently *writes* to them besides `latest` — promoting a version to
  `stable`/etc. has no CLI verb yet, since none is specified).
- `tool remove` checks every registered project's `project.json` for a `dependencies.tools` entry
  before removing; `--force` overrides, matching 02-CLI-SPEC.md exactly.
- Caveat: the "bounded repair budget" in this deterministic layer is a plain retry (rerun the same
  command up to twice) — it is not diagnosis-and-fix. Actual error diagnosis ("read the error,
  search GitHub issues, attempt a reasonable code-level fix") requires the AI layer described in
  06-AI-SPEC.md, which is intentionally a stub in this build (see Phase 8).
- GitHub/web *discovery* (05-TOOL-SYSTEM-SPEC.md "GitHub Discovery", "Unknown Games") is not
  implemented — it requires live web/GitHub search, which belongs to Endo AI (Phase 8), not this
  deterministic command layer. `tool.install` takes an already-identified `repository` — an AI
  orchestrator would do discovery first, then call this command with the result.

### Phase 5 — GameModding discovery: **Not implemented**
Depends entirely on Phase 8 (live AI + web/GitHub search). The hierarchy requirement itself
(`GameModding/<Game>/<Project>`) is already satisfied by Phase 2's generic
`Category/SubCategory/Name` structure — GameModding is not special-cased anywhere in the code,
which matches the spec (GameModding is just one category among others, not a distinct system).

### Phase 6 — Git/DevRepo: **Implemented**
- `DevRepoService.Checkpoint`: locates `PUSH.md` by bounded breadth-first search (DevRepo, then
  Endo root — never a hard-coded path, per spec), snapshots the live `config/` directory
  (environment.json) into the DevRepo working tree, `git add -A` + `git commit` with a message
  generated from actual `git status --porcelain` output (never invented). Returns success with no
  commit hash when there's nothing to checkpoint.
- Deliberately mirrors *only* `config/` into DevRepo — not project directories, not Tool binaries
  — per the explicit constraint against turning DevRepo into a machine backup. Tool/runtime
  *manifests* are sub-objects inside environment.json in this design, so mirroring
  environment.json already carries them; there's no separate manifest-file tree to mirror.
- Task-branch-per-task commits and cherry-pick workflows (07-GIT-DEVREPO-SPEC.md "Task Commits")
  are not implemented — nothing in Phases 1-4 yet produces the kind of multi-task work session
  this would apply to. `project.json`'s `tasks.active` list exists and is ready for this, but the
  workflow engine around it (cycle tracking, review states) described in 08-WORKFLOW-SPEC.md is
  not built.

### Phase 7 — Restore: **Implemented**
- `RestoreService` implements the exact reconciliation flow (Saved State → Inspect → Compare →
  Restore Missing → Reuse Compatible → Report Differences) for `projects`, `tools`, `runtimes`,
  and `all`.
- `RestoreReport` distinguishes all eight categories 10-RESTORE-MIGRATION-SPEC.md's "Final Restore
  Report" requires: Restored, Already present, Repaired, Changed, Missing, Unresolved, Existing
  but unmanaged, Warnings. `restore.command`'s `CommandResult` is `Success = false` whenever
  anything is Missing or Unresolved — it never reports "restore successful" with unresolved items,
  per the explicit requirement.
- Project repair reconstructs `project.json` from the environment.json `ProjectRef` when it's
  missing (the information needed already exists in Endo's own state — not fabricated), and
  re-runs `git init` when `.git` is missing, with an explicit Warning that history could not be
  recovered because none was recorded. A project whose *directory* is entirely missing is reported
  Unresolved, not silently skipped — Endo does not fabricate a project's Git history or content.
- Tool/runtime restore checks install paths and reports Missing + Unresolved with instructions to
  re-run `tool install`/`runtime install`; it does not silently re-fetch, since re-fetching a tool
  is itself the validated install pipeline, not a restore-time side effect.

### Phase 8 — AI: **Interface implemented, no real provider wired in**
- `IAiProvider` / `AiCompletionRequest` / `AiCompletionResponse`: the provider-neutral contract.
- `NullAiProvider`: the only provider registered in this build. Always returns
  `Available = false` with an honest reason — per 06-AI-SPEC.md "No Invented State", an
  unconfigured provider must not be reported as a success.
- `AiOrchestrator`: sends only the command *catalog* (name + one-line description from
  `CommandEngine.ListCommands()`) to the provider, never the full environment — per "Context
  Sources": "Do not automatically send the entire environment to every model request." Whatever
  command name comes back is checked against the real registry before executing; an unrecognized
  name is refused.
- `endo ai ask "<request>"` is wired into the CLI and currently reports "no provider configured"
  because no real LLM adapter exists yet. Wiring an actual cloud provider (OpenAI/Claude/etc.)
  behind `IAiProvider` is the natural next increment and requires no changes to `CommandEngine`,
  `AiOrchestrator`, or any existing command.

### Phase 9 — Dev Container: **Not implemented**
No container definition exists yet. The architecture this build produces is container-ready in
principle (managed root is fully self-contained under one directory tree, environment.json is the
portable state description, DevRepo is the portable recovery mechanism) but nothing has been
written or tested against an actual container.

### Phase 10 — Hardening: **Partially covered**
Covered by the current test suite: interrupted/invalid atomic writes, missing environment.json,
missing project directories (drift), unknown commands, empty DevRepo checkpoints. Not covered:
interrupted installs mid-build, offline operation end-to-end, multi-machine restore, AI
hallucination prevention beyond the catalog-validation described above (there's no real provider
to hallucinate yet), retry-loop prevention beyond the Phase 4 bounded-retry (no cyclic
self-referential task instructions exist yet to test against, since Phase 8/workflow engine isn't
built).

## What a next session should pick up first

1. Wire a real `IAiProvider` (start with one cloud adapter) and exercise `endo ai ask` end to end.
2. Decide the runtime-removal question above (add a spec'd verb, or explicitly declare runtimes
   removal-protected-forever) rather than leaving it silently absent.
3. Build the workflow engine (08-WORKFLOW-SPEC.md: cycles, task review states, self-referential
   instruction loop protection) — currently only the data shape (`tasks.active`) exists.
4. GameModding discovery (Phase 5) once Phase 8 has a real provider to search the web/GitHub with.
