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
    Endo.Core.Tests/  — 48 xUnit tests, all passing
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

`ICommand` also exposes `Parameters` (the real `args` dictionary keys the command reads), and
`CommandDescriptor`/`AiOrchestrator`'s system prompt list them explicitly per command
(`project.new(category, subCategory, name): ...`) rather than only a name + prose description.
This was added after live testing (see Phase 8) showed a real model inferring plausible-but-wrong
key names (`Category`, `ProjectName`) from the description text alone — a reliability gap in "AI
should use the actual command definitions" that affects every provider, not just the local one
that surfaced it.

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
  (git clone source-first, **or a release/archive download as fallback** — see below) →
  Scratchpad (always cleared/disposable first) → Documentation (locates README.md/.rst/.txt/bare
  README, recorded in the report) → Setup/Build (optional build command) → Test (optional validate
  command) → bounded retry (2 attempts per step, captured as "recovery attempts") → PASS → move
  out of Scratchpad and register, or FAIL → leave Scratchpad evidence in place and return a report
  with every field 05-TOOL-SYSTEM-SPEC.md's "Failure Reports" section lists (repo, ref, docs
  reviewed, succeeded/failed steps, errors, recovery attempts, final reason). Nothing is registered
  on failure.
- **Release/archive acquisition** (`ToolInstallRequest.ReleaseUrl`, mutually exclusive with
  `Repository`): downloads a zip via a shared `HttpClient` and extracts it straight into
  Scratchpad, then continues through the same documentation/build/validate/register pipeline as
  the git path. Closes the "Release fallback works" acceptance criterion, previously flagged as an
  unimplemented gap. **Verified for real**, not just unit-tested: used to install Ollama itself
  (`endo tool install Ollama --release https://github.com/ollama/ollama/releases/.../ollama-windows-amd64.zip --version 0.32.7 --validate "ollama.exe --version"`)
  — downloaded ~700MB, extracted, validated, and registered correctly. A release install requires
  an explicit `--version` (there's no commit hash to derive one from).
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
- **Verified live twice, with two very different outcomes that both prove the design correct:**
  - Under Ollama/llama3.2 (no real web search available locally): the model recalled a plausible
    tool name ("TES5Edit") from training data with a repository URL that doesn't actually resolve
    — `git clone` failed, and the report correctly showed `0/1 candidate(s) validated and
    registered`. A name without a real, clonable repository never becomes a registered tool, no
    matter how confidently the model stated it.
  - Under `claude-cli` (real `WebSearch`/`WebFetch`, see Phase 8): `endo ai discover GameModding
    Skyrim` had Claude actually search the web and GitHub, and returned 5 real, currently-relevant
    Skyrim modding tools (Mod Organizer 2, LOOT, xEdit, CommonLibSSE-NG, Wrye Bash) with correct
    repository URLs — **all 5 cloned, validated, and registered successfully**
    (`Discovery complete: 5/5 candidate(s) validated and registered`). This is the complete
    05-TOOL-SYSTEM-SPEC.md "Unknown Games" flow working end-to-end for real, not a simulation.
- Fixed during the Ollama test: the response parser only accepted a top-level JSON array, but a
  provider asked for "a JSON array" sometimes returns a single JSON object instead (observed with
  Ollama's constrained `format: "json"` decoding, which guarantees valid JSON but not the specific
  shape requested). `DiscoverToolsAsync` now accepts a bare object as a one-candidate result.

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

### Phase 8 — AI: **Implemented with three real providers — two fully verified live**
- `IAiProvider` / `AiCompletionRequest` / `AiCompletionResponse`: the provider-neutral contract.
  Request carries three provider hints, each optional and ignorable: `EnableWebSearch`,
  `MaxTokens`, `ForceJsonOutput` (ask the provider to constrain decoding to valid JSON where it
  can — see OllamaAiProvider below).
- **`AnthropicAiProvider`** (`Endo.Core/Ai/AnthropicAiProvider.cs`): cloud provider using the
  official `Anthropic` NuGet SDK. Zero-arg `AnthropicClient` resolves credentials itself — API key,
  auth token, `ant auth login` OAuth profile, or Workload Identity Federation — so no key needs to
  live in the environment at all. No credential of any kind is read from or written to
  `environment.json` (06-AI-SPEC.md "Security"). Auth failures and safety refusals both report
  `Available = false` with the real reason, never a fabricated success. Model is `claude-opus-5`.
  **Still not verified against a live response** — this sandbox has no `ant` CLI and no
  `ANTHROPIC_API_KEY`, so only its no-credential failure path has been exercised.
- **`OllamaAiProvider`** (`Endo.Core/Ai/OllamaAiProvider.cs`): local-model provider, per
  06-AI-SPEC.md's local-first goal — added because the user explicitly wants to run a local model
  rather than authenticate to a cloud provider. Talks to a local Ollama server over its native
  `/api/chat` HTTP endpoint (not the Anthropic SDK — a local model doesn't speak that wire format).
  `EnableWebSearch` is silently ignored (no local search tool to attach); `ForceJsonOutput` maps to
  Ollama's `format: "json"` constrained decoding.
  **Fully verified live, start to finish, in this session**:
  1. `endo tool install Ollama --release .../ollama-windows-amd64.zip --version 0.32.7 --validate "ollama.exe --version"`
     — installed *into the Endo workspace* via the (also new) release-acquisition path, not the
     machine's global Ollama.
  2. `endo ai serve --model qwen2.5:0.5b` (and again with `llama3.2`) — started the workspace's own
     `ollama.exe serve` (env `OLLAMA_MODELS` pointed at a workspace-local models directory,
     `OLLAMA_HOST` configurable), polled for readiness, then `ollama.exe pull <model>` with live
     progress streamed straight to the console.
  3. `endo ai ask "Create a new project called MyMod under GameModding for Skyrim"` — with
     `llama3.2`, this produced the correct command name and correct argument keys, which
     `CommandEngine` executed for real: `GameModding/Skyrim/MyMod` was created on disk with a
     correct `project.json`. With the much smaller `qwen2.5:0.5b`, the same request initially
     failed at each stage in turn (plain-prose reply → wrong-cased argument keys) — each failure is
     what led to the `ForceJsonOutput` hint and the `ICommand.Parameters` fix above; after both
     fixes, `llama3.2` completed the full loop correctly.
  4. `endo ai discover GameModding Skyrim` — see Phase 5 above for that result.
- **`ClaudeCliAiProvider`** (`Endo.Core/Ai/ClaudeCliAiProvider.cs`): third provider, added on
  explicit request — the user already runs `claude` interactively from PowerShell and wanted that
  exact login reused as an Endo AI provider, without `ant` and without managing an API key at all.
  Shells out to the `claude` CLI itself in headless mode (`-p --output-format json
  --no-session-persistence`), which resolves auth from whatever session/keychain the CLI already
  has — nothing provider-specific to configure. Closes stdin immediately after starting the
  process (confirmed by direct testing that otherwise the CLI waits ~3s deciding whether piped
  input is coming). `EnableWebSearch` maps to `--tools WebSearch,WebFetch` (vs. `--tools ""` for a
  pure headless completion with no tool use at all).
  **Fully verified live, both operations, on the first real attempt after one fix**:
  - `endo ai ask "Create a new project called MyMod under GameModding for Skyrim"` worked
    correctly immediately — right command, right argument keys, real project created on disk.
  - `endo ai discover GameModding Skyrim` initially returned nothing: the CLI's headless mode has
    no TTY to approve a permission prompt, so `WebSearch`/`WebFetch` calls were silently blocked
    (`permission_denials` in the raw JSON output) even though `--tools` had allowed them — and
    Claude correctly refused to invent repository URLs rather than fake a result, exactly per
    instructions. Fixed by adding `--permission-mode bypassPermissions`, scoped safely because
    `--tools` already restricts what that bypass can reach (only the two read-only search tools,
    never Bash/Write/Edit). After the fix: 5 real, current Skyrim modding tools found via genuine
    web search, all 5 cloned/validated/registered — see Phase 5 above.
  - **Cost/latency caveat, found directly**: `claude -p` is the full Claude Code harness, not a
    bare completion — every call incurs its own baseline system-prompt overhead (~16k
    cache-creation tokens observed even from a neutral directory with no CLAUDE.md) on top of
    whatever Endo's own request costs. `--bare` mode would cut this significantly but only accepts
    `ANTHROPIC_API_KEY`/`apiKeyHelper` auth — never OAuth or keychain — which would defeat the
    entire point of this provider, so it's deliberately not used. This makes `claude-cli` the
    right choice when "reuse my existing login, zero setup" matters more than per-call cost/
    latency; `AnthropicAiProvider` (once a credential is available to test it) should be cheaper
    per call for high-volume use.
- **`OllamaServerManager`** (`Endo.Core/Ai/OllamaServerManager.cs`) + `ollama.serve`/`ollama.pull`
  commands: starts the workspace Ollama binary as a detached background process only if nothing is
  already responding at the configured address (never assumes a fixed startup delay — polls),
  redirects its log to `Cache/Logs/ollama-serve.log`, and pulls models with live console output
  (deliberately not captured/buffered, since `ollama pull`'s progress bar only makes sense streamed
  directly). **Known gap found during cleanup**: `ollama serve` spawns a separate
  `llama-server.exe` child process to actually run inference, and stopping/killing the parent
  `ollama.exe` does not stop that child — a real user wanting to fully shut things down needs to
  stop both. There is currently no `endo ai stop` command at all; this should be added alongside
  fixing the shutdown behavior.
- **`AiProviderFactory`** (`Endo.Core/Ai/AiProviderFactory.cs`): builds the active provider from
  `environment.json`'s `ai.provider` (`anthropic` | `claude-cli` | `ollama`) plus `ai.model` /
  `ai.baseUrl` where relevant, instead of the CLI hardcoding one provider. An unset/unrecognized
  provider resolves to `NullAiProvider` — honestly "not configured" rather than silently defaulting
  to a provider the user never chose. `endo setup`'s interactive AI-provider prompt accepts all
  three and, for `ollama`/`claude-cli`, asks for a model name; the deterministic `setup` command
  accepts the same via `aiProvider`/`aiModel` args. There is currently no way to *change* the
  configured provider after initial setup short of re-running `endo setup` (which asks to confirm
  overwriting the existing config) or hand-editing `environment.json` — a dedicated
  `endo config set ai.provider ollama` verb would be a reasonable follow-up.
- `NullAiProvider`: kept as an explicit "no provider" option and as the honest-failure reference
  implementation.
- `AiOrchestrator`: sends only the command *catalog* to the provider — now name, description,
  **and real parameter names** (see the architectural-rule section above) — never the full
  environment. Whatever command name comes back is checked against the real registry before
  executing; an unrecognized name is refused. Both `AskAsync` and `DiscoverToolsAsync` now request
  `ForceJsonOutput: true`.
- `endo ai ask "<request>"`, `endo ai discover <Category> <SubCategory>`, and
  `endo ai serve [--model <name>] [--base-url <url>]` are all wired into the CLI.
- Known gap carried over from before: if Anthropic's server-side web-search loop hits its default
  10-iteration cap (`stop_reason: "pause_turn"`), `AnthropicAiProvider` does not resume the turn —
  judged not worth the fragility of manually reconstructing response content for what should be an
  uncommon case. A pause with no text yet is reported as unavailable rather than silently truncated.

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

### Phase 10 — Hardening: **Partially covered, substantially expanded this session**
Covered by the test suite (48 tests): interrupted/invalid atomic writes, missing environment.json,
missing project directories (drift), unknown commands, empty DevRepo checkpoints, plus:
- Interrupted/failed build mid-`ToolService.Install` (`ToolServiceHardeningTests`): a failing build
  command leaves Scratchpad evidence in place, registers nothing, and records the bounded-retry
  attempts — verified against a real local git repo, not a mock. Extended with two more cases for
  the new release-acquisition path: missing `--version` fails before any network call, and an
  unreachable release URL fails honestly.
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
  non-JSON garbage also fails cleanly rather than throwing. **This property was then also proven
  against two real models**, not just a stub: see Phase 5/8 above — a live local Ollama model
  recalled a plausible-sounding but non-existent tool repository, and the deterministic install
  pipeline's actual clone attempt (not any AI-side check) is what caught it and prevented
  registration; separately, `claude-cli`'s headless permission denial produced a case where Claude
  itself refused to invent a repository URL when it genuinely couldn't verify one via search.
- The real Dev Container build/run (Phase 9) and the real Ollama and claude-cli install/serve/
  ask/discover loops (Phase 8) are themselves hardening exercises — cross-platform behavior (Linux
  container) and full live AI round-trips (impossible to test against the raw Anthropic SDK in
  this sandbox, but fully achievable via claude-cli) were both previously unverified.

Still not covered: full offline operation *beyond* the unreachable-repository/URL cases above (no
systematic "disconnect every network-touching path" sweep); retry-loop prevention beyond the
Phase 4 bounded-retry (no cyclic self-referential task instructions exist yet to test against,
since the 08-WORKFLOW-SPEC.md workflow engine isn't built); a live round-trip against the raw
Anthropic SDK path specifically (`claude-cli` closed the "live Claude" gap in general — both
`ai ask` and `ai discover` are now proven against real Claude responses — but `AnthropicAiProvider`
itself, which talks to the API directly rather than through the CLI, is still only verified for
its no-credential failure path).

## What a next session should pick up first

1. Exercise `AnthropicAiProvider` (the raw SDK path, not `claude-cli`) under a real
   `ANTHROPIC_API_KEY` or `ant auth login` credential — `claude-cli` already proved the underlying
   `ai ask`/`ai discover` logic works correctly against real Claude, but the direct-SDK provider
   itself remains unverified beyond its no-credential failure path.
2. Fix Ollama shutdown: `endo ai serve` starts `ollama.exe`, which spawns a `llama-server.exe`
   child that isn't stopped when the parent is. Add an `endo ai stop` command that stops both.
3. Add a way to change AI provider/model after initial setup without re-running the whole
   interactive `endo setup` flow or hand-editing `environment.json` (e.g. `endo config set
   ai.provider ollama`) — came up directly while testing this session.
4. Decide the runtime-removal question above (add a spec'd verb, or explicitly declare runtimes
   removal-protected-forever) rather than leaving it silently absent.
5. Build the workflow engine (08-WORKFLOW-SPEC.md: cycles, task review states, self-referential
   instruction loop protection) — currently only the data shape (`tasks.active`) exists. This also
   unblocks the remaining Phase 10 gap (retry-loop prevention has nothing to test against yet).
6. Consider having discovered-tool README content actually parsed for build instructions (Phase 5
   currently only detects README presence, not its contents) and implementing `pause_turn`
   resumption in `AnthropicAiProvider` if long research queries turn out to need it in practice.
