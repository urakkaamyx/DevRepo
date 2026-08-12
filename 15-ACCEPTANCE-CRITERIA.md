# Acceptance Criteria

## Core

- [ ] endo works on Windows.
- [ ] Unix-like support is architecturally accommodated.
- [ ] Endo has a self-contained managed root.
- [ ] Setup is interactive.
- [ ] Endo does not assume universal third-party tools.

## Environment

- [ ] environment.json exists.
- [ ] environment.json is comprehensive and organized.
- [ ] Important changes persist immediately.
- [ ] Writes are crash-safe.
- [ ] Drift can be detected.
- [ ] Restore does not silently lose unknown state.

## Projects

- [ ] Interactive project creation works.
- [ ] Explicit project creation works.
- [ ] GameModding/<Game>/<Project> works.
- [ ] project.json is created.
- [ ] Independent project Git works.
- [ ] Multiple active tasks are supported.
- [ ] project open opens the directory.
- [ ] Project-configured IDE works.
- [ ] --ide overrides the default.
- [ ] .agents/ is project-specific.

## Runtimes

- [ ] Multiple runtime versions coexist.
- [ ] Versions can be selected independently.
- [ ] Default templates use latest installed compatible versions.
- [ ] Availability and selection are separate.

## Tools

- [ ] General tools work.
- [ ] Scoped tools work.
- [ ] Scope inheritance works.
- [ ] Explicit dependencies work.
- [ ] Multiple versions coexist.
- [ ] Channels work.
- [ ] latest means newest validated installed version.
- [ ] Source-first acquisition works.
- [ ] Release fallback works.
- [ ] README/setup documentation is read.
- [ ] Scratchpad validation works.
- [ ] Build failures trigger diagnosis/retry.
- [ ] Failed candidates are not registered.
- [ ] Failure evidence is preserved.
- [ ] Provenance is recorded.
- [ ] Older versions remain available.
- [ ] Updates do not force projects to migrate.
- [ ] Update notifications work.
- [ ] Per-tool update preferences work.
- [ ] Removal checks dependencies.
- [ ] --force overrides protection.

## GameModding

- [ ] Unknown games can be created.
- [ ] Web research works.
- [ ] GitHub discovery works.
- [ ] README inspection works.
- [ ] Third-party tools are reported.
- [ ] Candidates are validated before registration.
- [ ] Game-specific tools are scoped correctly.

## Git/DevRepo

- [ ] DevRepo can be private.
- [ ] Environment state is versioned.
- [ ] Tool manifests are versioned.
- [ ] Custom tool definitions can be preserved.
- [ ] Project Git remains independent.
- [ ] Essential changes create checkpoints.
- [ ] Cycle boundaries create checkpoints.
- [ ] PUSH.md is located before commit-message generation.
- [ ] Recommended Push commit message is generated from actual changes.
- [ ] Tasks can be independently committed.
- [ ] Tasks can be cherry-picked.
- [ ] Denied tasks enter revision.
- [ ] Abandoned tasks can be explicitly removed.

## AI

- [ ] Provider-neutral interface exists.
- [ ] Local-first architecture exists.
- [ ] Cloud adapters are possible.
- [ ] AI knows CLI commands.
- [ ] AI can chain commands.
- [ ] AI uses actual command results.
- [ ] AI does not invent state.
- [ ] AI diagnoses recoverable failures.
- [ ] AI can perform normal requested operations without unnecessary approval.

## Restore

- [ ] restore all works.
- [ ] restore projects works.
- [ ] Restore scopes are extensible.
- [ ] Missing components can be reconstructed.
- [ ] Existing compatible components are reused.
- [ ] Unknown existing components are preserved and reported.
- [ ] Historical states can be restored.
- [ ] Project Git survives migration.
- [ ] Tools can be re-acquired from recorded source/releases.
- [ ] Final restore reports unresolved items accurately.

## Container

- [ ] Portable Dev Container/environment definition exists.
- [ ] Base image is not bloated.
- [ ] Managed tools can be installed as needed.
- [ ] Host projects remain usable.
- [ ] Clean-machine bootstrap from Endo + DevRepo is possible.
