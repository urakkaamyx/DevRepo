# JSON Schema Drafts

These are architectural starting points.

Schema versions must be explicit.

Schema migrations should be additive where practical.

Unknown fields should be preserved where feasible.

## environment.json

{
    "schema": {
        "version": 1
    },
    "identity": {},
    "paths": {},
    "workspace": {},
    "repositories": {},
    "projects": {},
    "tools": {
        "general": {},
        "scoped": {}
    },
    "runtimes": {},
    "libraries": {},
    "ai": {},
    "updates": {},
    "preferences": {},
    "restore": {},
    "history": [],
    "metadata": {}
}

## project.json

{
    "schema": {
        "version": 1
    },
    "identity": {
        "name": "MyMod",
        "category": "GameModding",
        "subCategory": "Skyrim"
    },
    "paths": {
        "root": ""
    },
    "repository": {
        "type": "git",
        "remote": null
    },
    "runtime": {},
    "dependencies": {
        "tools": {}
    },
    "ide": null,
    "tasks": {
        "active": []
    },
    "agents": {
        "path": ".agents"
    },
    "metadata": {}
}

## Tool Manifest

{
    "schema": {
        "version": 1
    },
    "identity": {
        "name": "LOOT"
    },
    "scope": {
        "category": "GameModding",
        "subCategory": "Skyrim"
    },
    "source": {
        "type": "git",
        "repository": "https://github.com/example/tool.git",
        "ref": "main",
        "commit": "abc123"
    },
    "acquisition": {
        "method": "source-build"
    },
    "channels": {
        "stable": {},
        "latest": {},
        "develop": {},
        "custom": {}
    },
    "versions": {},
    "validation": {
        "status": "passed",
        "tests": []
    },
    "installation": {},
    "update": {
        "enabled": true
    },
    "metadata": {}
}
