# Endo — System Context Prompt

You are being given context on **Endo**, a portable, self-contained development environment
manager and orchestration system. This document exists so you can pick up work on it — or use
it — without a prior conversation. Read it fully before making changes.

## Mission

Endo manages tools, runtimes, and projects for a developer, with Git-aware workflows, a private
recovery repo, and a provider-neutral AI interface. The core architectural rule, stated in
01-ARCHITECTURE.md and enforced throughout the codebase:

> If AI can perform an operation, a deterministic Endo command must exist for it. AI orchestrates
> commands. AI must not become a second hidden implementation of Endo.

Concretely: every state-changing operation is a registered `ICommand`. The CLI, the GUI, and any
AI layer all call the *same* commands through the *same* `CommandEngine` — none of them is allowed
to mutate state on its own. This is not a style preference; code that violates it is a bug.

## Repository layout

The repo root (wherever it's cloned) contains four siblings:

```
Source/       — this codebase (what you're reading). Its own git history, pushed to
                github.com/urakkaamyx/DevRepo.
Build/        — build output. Contains exactly one file: endo.exe (self-contained,
                single-file win-x64 publish). Produced by Build.ps1; wiped and
                regenerated on every run.
Environment/  — the live, running Endo environment for *this machine*: config/, Tools/,
                Runtimes/, Libraries/, Cache/, DevRepo/. Created by `endo setup`.
Projects/     — default location for user projects, one per
                Projects/<Category>/<SubCategory>/<Name>.
```

`Build.ps1` publishes; `Setup.ps1` builds (aborting before launching anything if the build
fails), registers `Build/` on the user PATH, then launches `endo.exe` with no args to run
first-time setup.

Two GitHub repos, deliberately separated:
- `github.com/urakkaamyx/DevRepo` — Endo's own **source code** (this repo).
- `github.com/urakkaamyx/DevRepoEnviroment` (private) — the **environment state** checkpoint
  history (git-committed copies of `Environment/config/`), independent of project source. Per
  07-GIT-DEVREPO-SPEC.md: "Do not turn DevRepo into a binary dump of the entire computer" — it
  holds config/state, never tool binaries or project source.

Source itself is organized as:
```
Source/
  01-ARCHITECTURE.md ... 15-ACCEPTANCE-CRITERIA.md   — the original architecture spec, authoritative
  STATUS.md                                          — running human-readable build log
  Endo.slnx
  src/
    Endo.Core/    — all logic: commands, services, environment, AI, projects, tools, runtimes
    Endo.App/     — the actual published exe (unifies Cli+Gui, see below)
    Endo.Cli/     — CLI dispatch logic (Endo.Cli.CliHost), also independently runnable for F5 debugging
    Endo.Gui/     — WPF GUI, referenced as a library by Endo.App
  tests/
    Endo.Core.Tests/   — xUnit, run with `dotnet test`. 77 tests as of this writing.
```

**Important spec-vs-reality note:** the numbered spec files (01–15) describe the *original*
design intent and are still authoritative for philosophy and constraints. Some concrete details
in them are now stale — e.g. they describe a single `environment.json`; the real implementation
splits it into per-section files (see below). When spec and code disagree on a *mechanism*, prefer
what STATUS.md and the code actually do; when they disagree on a *principle or constraint*, the
spec still governs. If you're about to build something and can't tell which situation you're in,
say so explicitly rather than guessing — this codebase has a standing rule against inventing
unspec'd behavior silently.

## The unified executable

`endo.exe` (project `Endo.App`, `AssemblyName=endo`) is the single distributable. Its `Main`:
- No args → GUI mode: boots `Endo.Gui.App` directly (it's a referenced library, not a separate
  process — `App.xaml`'s build action is `Page`, not `ApplicationDefinition`, specifically so it
  doesn't generate its own entry point).
- Args present → CLI mode: attaches to the caller's console (or allocates one), then calls
  `Endo.Cli.CliHost.Run(args)`.

Both modes drive the exact same `CommandEngine` built by `EndoCommandEngineFactory.Build(root)` —
the GUI is a presentation layer, not a second implementation.

## Environment state

`EnvironmentRepository` (in `Endo.Core/Environment/`) persists state as **one JSON file per
top-level section** under `Environment/config/`: `paths.json`, `projects.json`, `tools.json`,
`runtimes.json`, `ai.json`, `schema.json`, `identity.json`, `workspace.json`, `repositories.json`,
`libraries.json`, `updates.json`, `preferences.json`, `restore.json`, `history.json`,
`metadata.json`. All writes are atomic (temp file → validate → rename). A legacy single-file
`environment.json` is auto-migrated on load if found.

`EnvironmentState` (the in-memory representation) is unchanged in shape — services still take a
loaded `EnvironmentState`. On top of it, `EnvironmentRepository.Open()` returns an
`EnvironmentAccessor` with typed, auto-persisting fluent managers:
```csharp
var env = repository.Open();
env.Projects.Add(category, subCategory, name, ide, template);
env.Projects.Remove(key);   // unregisters only — never deletes the directory or git history
env.Projects.Disable(key);  // soft-deactivate, reversible, excluded from default Search()
env.Projects.Search(category: "GameModding", includeDisabled: false);
env.Tools.Search(...);
env.Runtimes.Search(...);
```

**Portable paths:** `ProjectRef` (the registration record in `projects.json`) does **not** store
an absolute path — only `Category`/`SubCategory`/`Name`. The actual directory is always derived
via `projectRef.ResolvePath(state.Paths)` = `Workspace + Category/SubCategory/Name`. This means a
restored environment stays correct even if the workspace moves to a different drive/machine.
**Known gap:** Tool and Runtime install paths are *not yet* made portable this way — they still
store absolute paths. If you're asked to fix that, it's real remaining work, not done.

## Project system

`Projects/<Category>/<SubCategory>/<Name>/`, each with its own independent Git repo (separate
from DevRepo — 07-GIT-DEVREPO-SPEC.md), a `project.json`, and a `.agents/` directory (project-
specific AI instructions, explicitly *not* merged with Endo's own AI — see 06-AI-SPEC.md
"Separation"). GameModding projects require `Category="GameModding"`, `SubCategory=<game name>`
(never "Games" or "Mod" — this was a real bug fixed earlier in this project's history).

Every new project also gets:
- **Optional template** (`project.new ... --template dotnet-classlib`): scaffolds a real starter
  project via the actual `dotnet` CLI (`dotnet new classlib` + `dotnet new sln` — note: as of
  .NET 10 this produces `.slnx`, not classic `.sln`). Not spec'd in 04-PROJECT-SPEC.md — a
  directly-requested feature, kept explicit/opt-in rather than guessed from Category or IDE.
- **`docs/Bootstrap/BOOTSTRAP.md`** (always, regardless of template): a placeholder file for the
  user to paste a raw project spec into.

### The bootstrap pipeline

`endo project bootstrap <Category/SubCategory/Name> --agent <claude|codex|...>`:
1. Reads and validates `docs/Bootstrap/BOOTSTRAP.md` (refuses to run if it's still the unfilled
   placeholder).
2. Sends it to the **Builder** AI role (see below) — a free-form completion, asked to break the
   spec into a handful of numbered architecture documents — and writes the result into
   `docs/Architecture/*.md`.
3. Launches the chosen coding agent (`claude`, `codex`, or any executable name) **interactively,
   in its own new PowerShell window**, cwd = the project directory, seeded with an instruction to
   read those docs and start building. This process is fully independent of Endo once launched —
   Endo does not supervise or capture its output.

This is exactly the mechanism you may be reading this document *through* — if you were launched
by `endo project bootstrap`, you're that independent agent, working directly in a project
directory with `docs/Architecture/` (and this file, if present at the repo root) as your brief.

## AI system — two independent roles

`AiProviderFactory` resolves `IAiProvider` for two separate roles, each independently configured
in `environment.json`'s `ai` section (`ai.orchestrator.*` / `ai.builder.*`) and in `endo setup`:

- **Orchestrator** — Endo AI proper. Used by `AiOrchestrator`, which translates natural language
  into calls against the *real* command registry (`CommandEngine.ListCommands()`) and refuses
  anything that doesn't map to a registered command. This is the "no hidden second
  implementation" rule enforced in code. Falls back to the pre-role-split flat `ai.provider`/
  `ai.model` for environments set up before this split existed.
- **Builder** — free-form generation only (currently: BOOTSTRAP.md → architecture docs). Calls
  `IAiProvider.CompleteAsync` **directly**, never through `AiOrchestrator` — there's no reason to
  constrain "write some markdown" to the command-dispatch contract.

Three `IAiProvider` implementations exist: `AnthropicAiProvider` (direct API), `ClaudeCliAiProvider`
(shells out to an already-logged-in `claude` CLI session — no separate credentials), `OllamaAiProvider`
(local, fully offline). An unconfigured role resolves to `NullAiProvider` (`Available: false`),
never a silently-assumed default.

**A real, fixed bug worth knowing about:** every place this codebase shells out to a subprocess
and reads its stdout (`ClaudeCliAiProvider`, `GitProcess`, `ShellProcess`, `OllamaServerManager`,
`ProjectTemplates`) now explicitly sets `StandardOutputEncoding`/`StandardErrorEncoding = UTF8` on
`ProcessStartInfo`. Without it, .NET reads subprocess output through the Windows console codepage
instead of UTF-8, silently corrupting any multi-byte character (confirmed directly: an em-dash
round-tripped as "ΓÇö"). If you add a new process-shelling call site, set this explicitly — it's
easy to forget and the corruption is silent.

## Tool system

`ToolService` (05-TOOL-SYSTEM-SPEC.md): source-first acquisition (git clone, checkout ref, build,
validate) with release/archive fallback when source is unsuitable (e.g. a C++ tool nobody wants to
compile locally — this was a real case: `SM-DLL-Injector`, installed from its raw `.exe` release
asset). Release acquisition sniffs the download for the zip magic number and only extracts it as
an archive if it actually is one; otherwise the raw file is kept under its own name — not every
release asset is a zip, and assuming so was a real bug, now fixed. Tools are either **General**
(broadly available) or **Scoped** to a `Category/SubCategory` (e.g. `GameModding/Skyrim`) — a
scoped tool is never inherited outside its scope. Installation always stages in a disposable
`Cache/Scratchpad/` first; nothing is registered until validation passes.

## Working conventions specific to this codebase

- Never invent a command name — check `EndoCommandEngineFactory.Build` (or run `endo help`, which
  lists every real registered command with its actual parameter names) before assuming one exists.
- Prefer adding a real `ICommand` over doing state-changing work ad hoc, even from "AI-adjacent"
  code like the Builder role — the one exception already established is genuinely free-form
  generation (writing docs), not anything that touches `environment.json`/`project.json`.
- `.agents/` (per-project) and Endo's own AI configuration are deliberately separate; don't
  conflate them.
- Don't guess at scaffolding/behavior a spec file doesn't mention (e.g. "what should a new project
  contain") — either it's explicitly spec'd, explicitly requested, or you should say so and ask
  rather than assume.
- Tests: `dotnet test tests/Endo.Core.Tests/Endo.Core.Tests.csproj` from `Source/`. Keep it green;
  add tests for new behavior in the same style as existing ones (arrange a temp `EnvironmentState`/
  temp directory, assert on real filesystem/JSON output, not mocks).
- After any change to `Endo.App`/`Endo.Cli`/`Endo.Gui`/`Endo.Core`, `Build.ps1` must be re-run to
  actually update `Build/endo.exe` — `dotnet build` alone only updates the Debug output under
  `src/*/bin/`, which nothing in production actually runs.

## Current known gaps (as of this document)

- Tool/Runtime install paths are still absolute (not yet made portable like project paths).
- `Environment.Tools`/`Environment.Runtimes` fluent managers have `Search()` but no
  `Remove`/`Disable` wired to a CLI command yet (only `Projects` has the full set, and even that's
  only reachable via the C# API — no `endo project remove`/`disable` CLI verb exists yet).
- The SM-DLL-Injector tool loads modules the native Windows way (`LoadLibrary`); a plain C# class
  library won't have the native entry point that requires without a bridge (e.g. C++/CLI) — known,
  not yet solved.
