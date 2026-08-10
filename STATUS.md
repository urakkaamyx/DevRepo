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
    Endo.Core.Tests/  — 44 xUnit tests, all passing
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
- GitHub/web *discovery* itself is not this layer's job — `tool.install` takes an already-identified
  `repository` and is what Phase 5's discovery flow calls once it has found a candidate.

### Phase 5 — GameModding discovery: **Implemented**
`AiOrchestrator.DiscoverToolsAsync` (`Endo.Core/Ai/AiOrchestrator.cs`) implements
05-TOOL-SYSTEM-SPEC.md's "Unknown Games" / "GitHub Discovery" flow:
1. Prompts Claude with `EnableWebSearch: true` to research real, currently-maintained third-party
   tools for a `Category/SubCategory` (e.g. `GameModding/Skyrim`), instructed to return only
   candidates it actually found a GitHub URL for via search, as a strict JSON array.
2. For every candidate, calls the exact same `tool.install` command a human would run — full
   source-first clone → Scratchpad → README check → build/validate → register-only-on-success
   pipeline from Phase 4. Discovery never registers anything itself; a name the provider claims
   without a real repository simply fails to clone, so the deterministic pipeline is what actually
   proves a candidate isn't invented, not the model's say-so.
3. Reports per-candidate success/failure back to the user — "Endo should notify the user of
   available tools" — via `endo ai discover <Category> <SubCategory>`.
- The hierarchy requirement itself (`GameModding/<Game>/<Project>`) is satisfied by Phase 2's
  generic `Category/SubCategory/Name` structure — GameModding is not special-cased anywhere in the
  code, matching the spec (GameModding is one category among others, not a distinct system).
- Not implemented: reading a candidate's README to auto-detect its actual build system
  (05-TOOL-SYSTEM-SPEC.md "README Requirement" implies more than presence-checking). Today
  `tool.install` only *locates* the README and records that it did; it doesn't parse build
  instructions out of it, so discovered candidates install with no `--build`/`--validate` command
  unless a human adds one afterward. Validation in that case falls back to "clone + checkout
  succeeded," which is honest but shallower than the spec's full intent.
- Not exercised end-to-end with a live credential in this session, same caveat as the rest of
  Phase 8 below — the JSON parsing, candidate-loop, and `tool.install` wiring are covered by the
  code path itself compiling and by Phase 4's existing install tests, but no live web-search
  round-trip has been run.

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

### Phase 8 — AI: **Implemented with a real Claude provider**
- `IAiProvider` / `AiCompletionRequest` / `AiCompletionResponse`: the provider-neutral contract.
- `AnthropicAiProvider` (`Endo.Core/Ai/AnthropicAiProvider.cs`): the default provider, using the
  official `Anthropic` NuGet SDK. It constructs a zero-arg `AnthropicClient`, which resolves
  credentials itself in order — `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, the active
  `ant auth login` OAuth profile, then Workload Identity Federation — so a user authenticated via
  the `ant` CLI needs no API key in the environment at all. No credential of any kind is read from
  or written to `environment.json`, per 06-AI-SPEC.md "Security". On an auth failure it catches
  `AnthropicUnauthorizedException` specifically and points the user at `ant auth login`; on any
  other failure (including a `stop_reason: "refusal"` safety decline) it returns
  `Available = false` with the real reason — never a fabricated success, per "No Invented State".
  Model is `claude-opus-5` (`Endo.Core/Ai/AnthropicAiProvider.cs`'s `Model` constant).
- `NullAiProvider`: kept as an explicit "no provider" option (e.g. for `endo setup` to select
  literally none) and as the honest-failure reference implementation; not registered by default
  now that a real provider exists.
- `AiOrchestrator`: sends only the command *catalog* (name + one-line description from
  `CommandEngine.ListCommands()`) to the provider, never the full environment — per "Context
  Sources": "Do not automatically send the entire environment to every model request." Whatever
  command name comes back is checked against the real registry before executing; an unrecognized
  name is refused.
- `endo ai ask "<request>"` and `endo ai discover <Category> <SubCategory>` are wired into the CLI.
  Verified manually end-to-end for the honest-failure path (no credential present →
  `Available = false` with a clear message, not a crash or a guess); a live successful call has
  **not** been exercised in this build session — this sandbox has outbound network access to
  `api.anthropic.com` but no `ant` CLI installed and no `ANTHROPIC_API_KEY`, so there was no
  credential to test a real round-trip with. Next session should run both commands under a real
  `ant auth login` session to confirm the full loop (catalog/candidates → Claude's JSON decision →
  validated dispatch through `CommandEngine`) end-to-end.
- `AiCompletionRequest.EnableWebSearch` is a hint, not a contract — providers that ignore it
  (`NullAiProvider`, any future local-model provider) just answer from what they already know.
  `AnthropicAiProvider` honors it by attaching Anthropic's server-side `WebSearchTool20260209`.
  One known gap: if the server-side search loop hits its default 10-iteration cap
  (`stop_reason: "pause_turn"`) before finishing, this provider does **not** resume the turn —
  resuming requires manually reconstructing the full response content as the next turn's assistant
  message (a nontrivial per-block conversion the C# SDK has no helper for), which was judged not
  worth the fragility for what should be an uncommon case on a bounded discovery query. A pause
  with no text yet is reported as unavailable rather than silently truncated or looped.

### Phase 9 — Dev Container: **Implemented and verified**
- `Dockerfile` (build root): `mcr.microsoft.com/dotnet/sdk:10.0` + `git` — nothing else. Publishes
  `endo` to `/opt/endo` and adds it to `PATH`. Deliberately bakes in no projects, tools, runtimes,
  or DevRepo state, per 09-DOCKER-DEVCONTAINER-SPEC.md's "Minimal base environment" and "Container
  is execution infrastructure, not the sole source of truth" — the container's only job is step 1
  of "Clean Machine" (install/bootstrap Endo); steps 2-7 (connect DevRepo, restore state,
  reconstruct runtimes/tools, reconnect projects) are exactly what `endo setup --restore all`
  already does, reused as-is rather than reimplemented for the container.
- `.devcontainer/devcontainer.json`: standard VS Code Dev Containers config pointing at the
  Dockerfile, `workspaceFolder: /workspace`.
- **Actually built and run** (Docker Desktop was not running at the start of this session; started
  it, then verified): `docker build` succeeds; inside a fresh container, `endo setup` (interactive,
  piped answers) → `endo project new GameModding Skyrim MyMod` → `endo project check` →
  `endo setup --restore projects` (correctly reports "Already present") → `endo devrepo checkpoint`
  all ran correctly end-to-end on Linux, cross-platform from the Windows dev machine this was built
  on. One real (non-Endo) finding from that run: a fresh container has no git identity configured,
  so the first `devrepo checkpoint` fails with git's own "Please tell me who you are" error —
  Endo surfaced that error honestly rather than crashing or silently skipping the commit; setting
  `git config --global user.email/user.name` resolved it. This is a normal git prerequisite on any
  fresh machine, not something Endo should silently paper over by injecting a fake identity.
- Not built: a `docker-compose.yml` or any multi-container orchestration — nothing in the spec asks
  for it, and Endo's own state model (environment.json + DevRepo) is the "second competing state
  system" the spec explicitly warns against re-inventing at the container layer.

### Phase 10 — Hardening: **Partially covered, expanded this session**
Covered by the test suite (44 tests): interrupted/invalid atomic writes, missing environment.json,
missing project directories (drift), unknown commands, empty DevRepo checkpoints, **and now**:
- Interrupted/failed build mid-`ToolService.Install` (`ToolServiceHardeningTests`): a failing build
  command leaves Scratchpad evidence in place, registers nothing, and records the bounded-retry
  attempts — verified against a real local git repo, not a mock.
- Unreachable/invalid source repository (`ToolServiceHardeningTests`), standing in for "offline
  operation" at the acquisition layer: clone failure is reported honestly with no crash and no
  registration.
- Multi-machine restore reconciliation (`RestoreServiceTests`): environment.json referencing a
  project/tool that isn't present on "this machine" is reported Missing + Unresolved, never
  fabricated as Restored; a directory that exists but is missing `project.json` is repaired from
  the already-known `ProjectRef` (not invented content); `RestoreAll` never reports
  `FullySuccessful` while anything remains unresolved.
- AI hallucination prevention (`AiOrchestratorHallucinationTests`): a stub `IAiProvider` proposing
  a command name that was never registered is refused by `AiOrchestrator` itself — proven
  deterministically, independent of live model behavior — with `CommandResult` staying `null`
  (nothing executed) and the refusal message naming the invented command. A stub returning
  non-JSON garbage also fails cleanly rather than throwing.
- The real Dev Container build/run described in Phase 9 is itself a hardening exercise —
  cross-platform behavior (Linux container vs. the Windows machine everything else was tested on)
  was not previously verified at all.

Still not covered: full offline operation *beyond* the single unreachable-repository case above
(no systematic "disconnect every network-touching path" sweep); retry-loop prevention beyond the
Phase 4 bounded-retry (no cyclic self-referential task instructions exist yet to test against,
since the 08-WORKFLOW-SPEC.md workflow engine isn't built); AI hallucination prevention against a
*live* model's actual behavior (the stub-provider tests prove `AiOrchestrator`'s refusal logic
works, but no live Claude response has been checked against it yet, per the Phase 8 caveat above).

## What a next session should pick up first

1. Exercise `endo ai ask` and `endo ai discover` end to end under a real `ant auth login`
   session — both are wired (`AnthropicAiProvider`, `AiOrchestrator.DiscoverToolsAsync`), but only
   the no-credential failure path has been verified so far; no live model response has been seen.
2. Decide the runtime-removal question above (add a spec'd verb, or explicitly declare runtimes
   removal-protected-forever) rather than leaving it silently absent.
3. Build the workflow engine (08-WORKFLOW-SPEC.md: cycles, task review states, self-referential
   instruction loop protection) — currently only the data shape (`tasks.active`) exists. This also
   unblocks the remaining Phase 10 gap (retry-loop prevention has nothing to test against yet).
4. Consider having discovered-tool README content actually parsed for build instructions (Phase 5
   currently only detects README presence, not its contents) and implementing `pause_turn`
   resumption in `AnthropicAiProvider` if long research queries turn out to need it in practice.
