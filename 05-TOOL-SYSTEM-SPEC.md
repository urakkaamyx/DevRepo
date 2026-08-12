# Tool System Specification

## Core Principle

Tools extend Endo's reach.

Tools are not automatically project dependencies merely because they are installed or available.

## Tool Categories

General tools:

Tools/
    General/

Specialized tools:

Tools/
    GameModding/
        Skyrim/
        Fallout4/

This hierarchy allows specialized tools to remain scoped to the projects that need them.

## General Tools

General tools may include things such as:

- Git
- Archive utilities
- Build utilities
- General development utilities

These are not GameModding tools.

## Game-Specific Tools

Example:

Tools/
    GameModding/
        Skyrim/
            LOOT/

LOOT is available to Skyrim projects because it is scoped to Skyrim.

It should not automatically become available to unrelated projects.

## Automatic Availability

For established categories, Endo should make the common category-specific tools available automatically.

The reasoning is practical:

There would be little value in creating a GameModding environment while withholding the common tools required to actually perform the work.

Availability does not force usage.

The project still determines actual dependencies.

## Unknown Games

If Endo does not already know how a particular game is modded:

1. Create the project environment.
2. Research current modding practices.
3. Search the web.
4. Search GitHub.
5. Identify tools.
6. Read tool repositories' documentation.
7. Notify the user.
8. Test candidates.
9. Register only successful candidates.

## GitHub Discovery

GitHub should be searched for third-party tools even when an official or known tool is already available.

The purpose is to notify the user that alternatives or supplemental tools exist.

Discovery does not automatically install them.

## README Requirement

Before attempting to build or install a repository:

1. Locate README.
2. Read README.
3. Read setup/build documentation.
4. Identify dependencies.
5. Identify required SDK/runtime versions.
6. Identify build commands.
7. Identify output artifacts.
8. Identify platform requirements.

The tool should not blindly attempt a build without reading its documentation.

## Source-First Acquisition

Prefer cloning repositories over downloading releases.

Reason:

Releases are frequently outdated.

Preferred:

clone repository
    ↓
read documentation
    ↓
checkout appropriate ref
    ↓
build
    ↓
test

Release/archive fallback is allowed when source is unavailable or unsuitable.

## Scratchpad

Every new tool candidate should be tested in a disposable Scratchpad.

Suggested structure:

Cache/
    Scratchpad/
        Tools/
            <Category>/
                <SubCategory>/
                    <CandidateTool>/

The Scratchpad is not the final installation location.

## Validation Lifecycle

Discovery
    ↓
Documentation
    ↓
Acquire
    ↓
Scratchpad
    ↓
Setup
    ↓
Build
    ↓
Test
    ↓
Diagnose
    ↓
Repair
    ↓
Retry
    ↓
PASS -> Register
FAIL -> Report

A first error is not an automatic final failure.

## Error Recovery

When something fails:

1. Capture the complete error.
2. Determine the failure category.
3. Inspect project files.
4. Inspect build configuration.
5. Read documentation again if needed.
6. Search GitHub issues when useful.
7. Search web documentation when useful.
8. Attempt a reasonable fix.
9. Retry.
10. Repeat within a bounded repair budget.

Only after reasonable recovery attempts fail should the candidate be marked failed.

## Failure Reports

When a tool cannot be added, Endo should list:

- Project/tool information
- Repository
- Version/ref
- Documentation reviewed
- Dependencies
- What succeeded
- What failed
- Errors
- Recovery attempts
- Final reason for failure

The user must be able to see the error.

Failed candidates must not be registered as available tools.

Scratchpad evidence should be retained long enough to diagnose the failure.

## Tool Registration

A successful candidate becomes an available tool only after validation passes.

The manifest should contain:

- Name
- Scope
- Repository
- Source/ref
- Commit
- Version
- Acquisition method
- Platform
- Build method
- Install method
- Executable/artifact
- Dependencies
- Validation results
- Provenance

## Multiple Versions

Old versions are intentionally preserved.

Not every update is a good update.

Example:

Tools/
    Skyrim/
        LOOT/
            versions/
                0.26/
                0.27/
                0.28/

Projects can remain on older validated versions.

Endo must never automatically force every project onto the newest version.

## Channels

Recommended conceptual channels:

stable
latest
develop
custom

latest means newest validated installed version.

It does NOT necessarily mean newest upstream version.

live may represent an upstream candidate under investigation.

live is not automatically installed or trusted.

## Updates

Endo may check for updates automatically.

The user should receive notifications.

Tools may have update preferences.

The system should retain older versions after updates.

## Removal

Normal removal checks dependencies.

If another project requires a version, Endo should notify the user.

The user may explicitly override protection with:

endo tool remove <tool> --force

## Capability System

Do not create an elaborate generic tool-capability framework.

Explicit tool names, scopes, dependencies, and manifests are sufficient.
