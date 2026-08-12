# Endo CLI Specification

## Command Name

The command is:

endo

Windows:

endo.exe

Unix-like systems:

endo

Do not use dev as the command name.

## Setup

endo setup

First-run setup must be interactive.

The user must be allowed to specify important environment choices rather than having Endo silently assume them.

Setup establishes:

- Endo managed root
- Project/workspace location
- DevRepo
- AI configuration
- Update preferences
- Bootstrap requirements

Managed dependencies should be installed into Endo's own directory.

## Restore

endo setup --restore all

endo setup --restore projects

Restore is additive and reconciliatory.

It is not wipe-and-replace.

## Projects

endo project new

endo project new <Category> <SubCategory> <ProjectName>

endo project check

endo project open

endo project open --ide <ide>

Default project opening behavior:

Open the project directory.

If project.json contains an IDE preference, that IDE may be used as the default.

An explicit --ide argument overrides the default for that operation.

Natural language may also request an IDE.

Example:

Open my project in Visual Studio.

Unless the user explicitly asks to save the change, that is an operation-level override.

## Tools

endo tool list

endo tool info <tool>

endo tool install <tool>

endo tool update <tool>

endo tool versions <tool>

endo tool remove <tool>

endo tool check <tool>

## Runtimes

endo runtime list

endo runtime install <runtime> <version>

endo runtime set <runtime> <version>

Multiple runtime versions can coexist.

Availability does not mean selection.

## Updates

endo update check

Endo may check for updates automatically and manually.

Update notifications should be visible to the user.

## Removal

Normal removal protects active dependencies.

The user can explicitly override protection with:

endo tool remove <tool> --force

Normal removal should provide a notification when something prevents removal.

## Command Results

Every command should produce structured internal results containing:

- Success/failure
- Exit status
- Output
- Error information
- Affected state
- Changed files
- Diagnostics
- Recovery information where applicable

The AI consumes these results rather than guessing.
