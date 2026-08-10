# Endo Dev Container — 09-DOCKER-DEVCONTAINER-SPEC.md
#
# Minimal base: .NET SDK (to build/run endo) + git (needed by project Git, DevRepo, and the
# source-first tool install pipeline). Nothing else is baked in. Tools, runtimes, and projects
# are reconstructed at runtime via `endo setup --restore all` against DevRepo — the container is
# execution infrastructure, not a second competing state system.

FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY . .
RUN dotnet publish src/Endo.Cli/Endo.Cli.csproj -c Release -o /opt/endo \
    && rm -rf /src/src/*/bin /src/src/*/obj /src/tests/*/bin /src/tests/*/obj

ENV PATH="/opt/endo:${PATH}"

# Projects/tools/runtimes/DevRepo all live under whatever ENDO_ROOT and workspace path
# `endo setup` is given — mount a host volume there to persist state across container recreation,
# or clone/restore fresh from DevRepo each time per the Clean Machine flow in
# 09-DOCKER-DEVCONTAINER-SPEC.md and 10-RESTORE-MIGRATION-SPEC.md.
WORKDIR /workspace
