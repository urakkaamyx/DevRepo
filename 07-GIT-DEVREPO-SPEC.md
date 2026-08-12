# Git and DevRepo Specification

## Project Git

Every project maintains its own Git repository.

Project Git is independent from Endo's DevRepo.

The project's .git directory and history must remain intact.

## DevRepo

DevRepo is Endo's private Git repository.

Its purpose is environment state, configuration, recovery, and history.

It may contain:

- environment.json
- Endo configuration
- Tool manifests
- Custom tool definitions
- Workspace metadata
- Restore metadata
- Environment history
- Checkpoint information

Do not turn DevRepo into a binary dump of the entire computer.

## Checkpoints

Endo should create checkpoints after important progress.

Important checkpoint moments include:

- Creating a project
- Adding a tool
- Installing a runtime
- Changing important environment state
- Completing a development cycle
- Completing an essential workflow step

Checkpoints are progress-dependent.

There does not need to be a push after every trivial command.

## PUSH.md

Before generating a DevRepo checkpoint commit:

1. Locate PUSH.md.
2. Read PUSH.md.
3. Review actual changed state.
4. Determine what was accomplished.
5. Generate the Recommended Push commit message/comment.
6. Commit according to the configured workflow.

The developer/AI must locate PUSH.md.

Do not assume it is always located in a hard-coded path.

PUSH.md provides the contextual guidance for preparing a checkpoint.

Detailed information should remain in the other Markdown files.

## Recommended Push

The AI should update the Recommended Push commit comment/message when preparing a checkpoint.

The generated message must be based on actual changes.

It must not invent work.

## Task Commits

Tasks should be independently committable.

Example:

Task A -> branch -> commit
Task B -> branch -> commit
Task C -> branch -> commit

This makes it possible to cherry-pick individual tasks.

## Denied Tasks

If a task is denied during review:

Task
    ↓
Review
    ↓
Denied
    ↓
Revision

Denied work is not automatically deleted.

## Abandoned Tasks

If the user explicitly decides to stop work on a branch/task, that branch/task may be removed.

Abandonment is different from denial.

Denial means revision.

Abandonment means stop and remove if appropriate.

## Push Between Cycles

A development cycle should end with an appropriate checkpoint.

The next cycle should begin by reading the relevant Markdown state/context.

This provides continuity without forcing every detail into JSON.
