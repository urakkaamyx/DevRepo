# Environment Specification

environment.json is the backbone of the Endo environment.

It describes the current known configuration and state of Endo.

## Organization

Recommended top-level structure:

{
    "schema": {},
    "identity": {},
    "paths": {},
    "workspace": {},
    "repositories": {},
    "projects": {},
    "tools": {},
    "runtimes": {},
    "libraries": {},
    "ai": {},
    "updates": {},
    "preferences": {},
    "restore": {},
    "history": {},
    "metadata": {}
}

The exact schema can evolve.

## Requirements

environment.json must be:

- Human-readable
- Machine-readable
- Versioned
- Organized
- Restorable
- Reconciliable
- Information-preserving

Do not artificially minimize it.

If state belongs in the environment description, it should be represented.

Detailed narrative belongs in Markdown documents.

## Safe Writes

Environment updates must be crash-safe.

Recommended process:

1. Write a temporary file.
2. Validate the complete JSON.
3. Flush and close.
4. Atomically replace the existing file.
5. Checkpoint important changes into DevRepo.

A process interruption must never leave a partially written environment.json.

## Persistence

Important state changes should be persisted immediately.

Examples:

- Project creation
- Tool registration
- Runtime installation
- Runtime selection
- Dependency changes
- Task changes
- Environment changes
- Restore changes

A force-save mechanism should also exist.

## Drift Detection

Endo should compare:

- environment.json
- Actual filesystem
- Tool manifests
- project.json
- Runtime installation state

and report drift.

## No-Loss Rule

Restore must not silently delete information that is not represented in the saved state.

Unknown existing:

- Projects
- Tools
- Settings
- Files
- Repositories

must be preserved and reported unless the user explicitly requests cleanup.
