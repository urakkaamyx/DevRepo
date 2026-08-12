# Positive Build Prompt

You are building Endo.

Endo is a complete managed development environment and natural-language development orchestrator.

Use the Endo architecture package as the authoritative stable design baseline.

## Mission

Build a robust, maintainable implementation.

Endo is the whole system.

Projects are independent Git repositories.

Tools extend Endo's reach.

AI is a provider-neutral natural-language orchestration layer over the Endo command engine.

## Positive Requirements

Build the system so that:

1. The user model remains simple.
2. State is explicit.
3. Data is preserved.
4. The environment is reproducible.
5. Tools and runtimes are versioned.
6. Source repositories are preferred over stale releases.
7. Candidates are validated before registration.
8. Errors are diagnosed before final failure.
9. Older validated versions remain available.
10. AI never invents state.
11. Every AI operation corresponds to a real Endo command.
12. Project Git remains independent from DevRepo.
13. environment.json remains comprehensive and organized.
14. Restore is reconciliatory and no-loss.
15. Unnecessary abstractions are avoided.

## Required Behavior

Implement:

endo setup

Interactive setup must allow the user to specify important choices.

Implement environment.json as the Endo environment state/configuration backbone.

Use crash-safe atomic writes.

Implement:

GameModding/<GameName>/<ProjectName>

for GameModding projects.

Implement project.json.

Implement multiple runtime versions.

Availability and selected version must remain separate.

Default templates use the latest installed compatible version.

Implement general and scoped tool management.

Implement:

- Multiple versions
- Channels
- Source-first acquisition
- Release fallback
- Scratchpad validation
- README-aware setup
- Dependency detection
- Testing
- Error diagnosis
- Recovery attempts
- Registration only after success
- Provenance
- Health checks
- Protected removal

When a new game has no established toolset:

1. Research web sources.
2. Search GitHub.
3. Read README/setup documentation.
4. Identify third-party tools.
5. Notify the user.
6. Validate candidates.
7. Register successful candidates.

Prefer cloning repositories.

If a release is used, validate it the same way.

If a build fails, inspect the error and attempt to resolve it before marking the tool a complete failure.

Preserve the error evidence.

If a candidate ultimately fails, report:

- Project information
- Tool information
- Source
- Version/ref
- Documentation
- Successful steps
- Failed steps
- Errors
- Recovery attempts
- Final reason

Do not register it.

Implement private DevRepo.

Keep project Git repositories independent.

Before checkpoint commits:

1. Find PUSH.md.
2. Read PUSH.md.
3. Review actual changes.
4. Generate/update Recommended Push commit message/comment.
5. Commit the appropriate state.

Use independently committable task branches where appropriate.

Denied tasks enter revision.

Explicitly abandoned branches/tasks can be removed.

Implement Endo AI as a separate interface.

Use a provider-neutral architecture.

Prefer local-first architecture.

Cloud providers may be used initially.

AI must know Endo CLI commands.

AI should use natural language to invoke those commands.

Implement restore with reconciliation.

Implement:

endo setup --restore all

and:

endo setup --restore projects

Implement project opening.

Default behavior opens the directory.

Support project.json IDE preference.

Support:

--ide

as a temporary override.

## Implementation Order

1. Core
2. Environment
3. Projects
4. Runtimes
5. Tools
6. Discovery
7. Git/DevRepo
8. Restore
9. AI
10. Container
11. Hardening

Keep every stage buildable and testable.
