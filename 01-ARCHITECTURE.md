# Endo Architecture

## Mission

Endo is a portable, self-contained development environment manager and orchestration system.

It provides:

- Managed tools
- Managed runtimes
- Project management
- Git-aware workflows
- Private DevRepo state/recovery
- Portable development environment support
- Provider-neutral AI interface

## Core model

User
|
+-- Endo CLI
|
+-- Endo AI
      |
      v
Endo Command Engine
      |
      +-- Projects
      +-- Tools
      +-- Runtimes
      +-- Git
      +-- Environment
      +-- Restore

Architectural rule:

If AI can perform an operation, a deterministic Endo command must exist for it.

AI orchestrates commands.
AI must not become a second hidden implementation of Endo.

## Endo vs Projects

Endo is the environment.

Projects are independent workspaces/repositories managed by Endo.

Conceptually:

Endo/
    executable
    config/
    tools/
    runtimes/
    libraries/
    cache/
    DevRepo/

Projects/
    Applications/
    GameModding/
    ...

The exact physical project root is configurable.

## Endo Installation

Endo installs managed components into its own directory.

It should not depend on arbitrary global installs when Endo can manage the component itself.

First-run setup is interactive because the user must choose important configuration.

## Tools

Tools extend Endo's reach.

General tools are broadly available.

Specialized tools are scoped.

Example:

Tools/
    General/
        Git/
        7zip/
    GameModding/
        Skyrim/
            LOOT/
            xEdit/

A Skyrim project can inherit:

Tools/General/
Tools/GameModding/Skyrim/

It does not receive unrelated game-specific tools.

## Projects

GameModding projects mirror their game hierarchy:

GameModding/
    Skyrim/
        MyMod/

Each project has:

- Its own directory
- Its own Git repository
- Its own project.json
- Its own .agents/ directory when applicable

## Environment

environment.json is the backbone/state description of the Endo environment.

It may become large.

That is acceptable.

Organization, recoverability, and preservation of information are more important than keeping the file artificially small.

The environment file represents known Endo state and configuration.

The physical filesystem remains the physical source of truth.

Endo reconciles the saved environment description against reality.

## AI

Endo AI is separate from project .agents/.

.agents/ is project-specific.

Endo AI operates at the Endo/environment level.

The architecture is local-first.

Cloud AI providers may be used initially.

The provider layer must remain replaceable.

## Restore

Restore is reconciliation, not destructive replacement.

Restore should:

- Restore missing things
- Reuse compatible existing things
- Preserve unknown existing things
- Report differences
- Support historical state
- Never silently delete unrelated data

## Design Philosophy

- Simple user model
- Powerful internals
- Explicit state
- Reproducibility
- No hidden AI-only operations
- No unnecessary abstractions
- Preserve older working versions
- Diagnose before declaring failure
- Never silently destroy data
