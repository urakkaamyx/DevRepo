# Restore and Migration Specification

## Goal

Move Endo to another machine without manually reconstructing the environment.

## Source of Truth

DevRepo contains versioned environment/configuration/recovery state.

This includes:

- environment.json
- Tool manifests
- Project registration
- Runtime information
- Custom tool definitions
- Restore metadata
- AI configuration metadata
- Checkpoints

## Tools

Tool binaries do not need to be stored in DevRepo.

They can be:

- Rebuilt from source
- Re-downloaded from a recorded release
- Reconstructed from recorded acquisition instructions

Source/ref/version information must be recorded.

## Projects

Projects remain independent Git repositories.

Restore project metadata and clone/reconnect repositories as necessary.

Preserve Git history.

## Restore Commands

endo setup --restore all

endo setup --restore projects

Additional restore scopes may be added.

## Reconciliation

Restore should follow:

Saved State
    ↓
Inspect Current Machine
    ↓
Compare
    ↓
Restore Missing
    ↓
Reuse Compatible Existing
    ↓
Report Differences

Do not wipe the machine by default.

## Unknown Existing State

If the target machine contains something that Endo does not recognize:

- Preserve it.
- Report it.
- Do not silently delete it.

## Historical Restore

DevRepo history/checkpoints allow Endo to restore a previous known environment state.

## Final Restore Report

The final report must distinguish:

- Restored
- Already present
- Repaired
- Changed
- Missing
- Unresolved
- Existing but unmanaged
- Warnings

Endo must not report "restore successful" when required components remain unresolved.
