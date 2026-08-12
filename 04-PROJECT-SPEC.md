# Project Specification

Projects are independent repositories managed by Endo.

## Hierarchy

Projects/
    GameModding/
        Skyrim/
            MyMod/

For GameModding projects, the GameName layer is required.

## project.json

project.json describes:

- Project identity
- Category
- Subcategory
- Path
- Runtime selections
- Dependencies
- IDE preference
- Active tasks
- Repository metadata
- Project configuration

Rich narrative should remain in Markdown.

Do not put large amounts of workflow prose into project.json.

## Dependencies

Dependencies are explicit named tools.

Example:

{
    "dependencies": {
        "tools": {
            "LOOT": "latest",
            "xEdit": "0.5.6"
        }
    }
}

A tool being available does not make it a dependency.

A project must explicitly declare tools it requires.

## Runtime Versions

Coding languages and runtimes have their own versions.

Example:

Python 3.12
Python 3.13

Both may be installed.

The project can select one.

The default template should select the latest installed compatible version.

The user can change the selected version at any time.

Availability and selection are separate concepts.

## IDE

project.json may contain:

{
    "ide": "visual-studio"
}

If no IDE is configured:

endo project open

opens the project directory.

If an IDE is configured, it can be used as the default.

The user can temporarily override it:

endo project open --ide visual-studio

Natural language can also request an override.

## .agents

.agents/ is project-specific.

It contains project-specific AI instructions/context.

It must not be merged with Endo's system-level AI interface.

## GameModding Discovery

When a new GameModding game is created:

1. Endo should determine how that game is modded.
2. AI should research the web.
3. AI should search GitHub.
4. AI should identify relevant third-party tools.
5. AI should read relevant repository README/setup documentation.
6. Endo should notify the user of available tools.
7. Candidate tools should be validated before registration.

The user does not need to manually know every tool required to begin modding.

## Multiple Active Tasks

project.json must support multiple active tasks.

Example:

{
    "tasks": {
        "active": [
            "task-a",
            "task-b",
            "task-c"
        ]
    }
}

Never design active task state as a singular task field.
