# Implementation Roadmap

Build incrementally.

Every phase should remain buildable and testable.

## Phase 1 — Core

Implement:

- Endo executable
- Cross-platform root detection
- Interactive setup
- Managed directories
- environment.json
- Atomic state writes
- Logging
- Command engine

## Phase 2 — Projects

Implement:

- Workspace
- Project hierarchy
- Interactive creation
- project.json
- Project validation
- Git preservation
- Project opening

## Phase 3 — Runtimes

Implement:

- Runtime manifests
- Multiple versions
- Installation
- Removal
- Version selection
- Latest-installed template behavior

## Phase 4 — Tools

Implement:

- General tools
- Scoped tools
- Tool manifests
- Multiple versions
- Channels
- Source-first acquisition
- Release fallback
- Scratchpad
- README-aware setup
- Build/testing
- Error recovery
- Registration
- Health checks
- Dependency-safe removal

## Phase 5 — GameModding

Implement:

- Game discovery
- Web research
- GitHub search
- README parsing
- Third-party tool discovery
- Scratchpad validation
- Scoped tool registration

## Phase 6 — Git/DevRepo

Implement:

- Private DevRepo
- Checkpoints
- PUSH.md discovery
- Recommended Push message generation
- Task commits
- Cherry-pick support
- Cycle checkpoints
- Restore history

## Phase 7 — Restore

Implement:

- Restore all
- Restore projects
- Restore tools
- Restore runtimes
- Restore configuration
- Historical restore
- Reconciliation
- No-loss behavior

## Phase 8 — AI

Implement:

- Provider-neutral interface
- Local provider architecture
- Cloud adapters
- CLI command metadata
- Natural language
- Command chaining
- Diagnostics
- Retry
- Web research
- GitHub research

## Phase 9 — Container

Implement:

- Dev Container
- Bootstrap
- Restore integration
- Host project access

## Phase 10 — Hardening

Test:

- Interrupted writes
- Interrupted installs
- Failed builds
- Offline operation
- No Git remote
- Multiple versions
- Migration
- Restore
- Unmanaged existing state
- Provider failures
- Path portability
- AI hallucinated success prevention
- Retry-loop prevention
