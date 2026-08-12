# Development Workflow Specification

## Development Cycles

Development is organized into cycles.

A cycle generally contains:

1. Context review
2. Task selection
3. Planning
4. Implementation
5. Testing
6. Documentation/state updates
7. Review
8. Checkpoint

## Active Tasks

Active tasks are plural.

There may be multiple active tasks simultaneously.

Do not use a singular active_task field as the primary model.

## Task Isolation

Where practical, each task should have an independently committable branch/change set.

This allows tasks to be cherry-picked individually.

## Review

A task can be:

- Active
- Ready for review
- Approved
- Denied
- In revision
- Completed
- Abandoned

## Denied Work

Denied work moves to revision.

It remains available.

The AI should not delete denied work.

## Abandoned Work

If the user explicitly stops work on a branch, the branch/task can be removed.

The system should distinguish explicit abandonment from rejection.

## Markdown Context

Detailed workflow information belongs in Markdown.

Possible files include:

- TASK.md
- TASKS.md
- CYCLE.md
- PLAN.md
- PUSH.md
- ARCHITECTURE.md
- NOTES.md

The exact file structure may evolve.

## project.json

project.json stores structured state.

It should not become a giant narrative document.

## environment.json

environment.json stores Endo-wide structured state.

It should not replace detailed Markdown documentation.

## Between Cycles

At the beginning of each cycle, AI should read the relevant Markdown context.

This allows detailed instructions to remain outside the JSON state files.

## PUSH.md Discovery

Before creating a push/checkpoint commit:

1. Search for PUSH.md.
2. Read it.
3. Determine required push behavior.
4. Review changed files.
5. Generate Recommended Push message.
6. Commit.

The AI must not assume a fixed location.

## Self-Referential Instructions

A task may tell the AI to reread a task file.

This must not create an infinite loop.

The workflow engine must track:

- Current cycle
- Current task
- Files already read
- Operations already performed
- Retry count
- Repair budget
- Completion state

Repeated instructions must be interpreted as context refresh, not automatic infinite execution.
