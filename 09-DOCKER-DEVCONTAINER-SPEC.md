# Dev Container Specification

Endo should support a portable development environment.

The environment should be reproducible on another machine without requiring a giant container containing every possible tool.

## Principles

- Minimal base environment
- Endo installed in its managed location
- Tools installed as needed
- Projects remain accessible
- environment.json remains logical state
- DevRepo remains persistent state
- Container is execution infrastructure, not the sole source of truth

## Clean Machine

A clean machine should be capable of:

1. Installing/bootstraping Endo.
2. Connecting to DevRepo.
3. Restoring environment state.
4. Reconstructing runtimes.
5. Reconstructing tools.
6. Reconnecting projects.
7. Continuing development.

The container should make this easier rather than becoming a second competing state system.
