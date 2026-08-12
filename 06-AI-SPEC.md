# Endo AI Specification

Endo AI is the natural-language operator for Endo.

## Separation

Endo AI is separate from project .agents/.

Project .agents/ contains project-specific instructions.

Endo AI operates the Endo environment.

## Provider Architecture

Endo should use a provider-neutral interface.

Possible providers:

- Local model
- OpenAI
- Claude
- Future providers

The initial implementation may use cloud providers.

The architecture should remain local-first so a local provider can eventually
become the primary option.

## Natural Language

The user should be able to communicate naturally.

Examples:

"Create me a Skyrim mod project."

"Install the latest version of LOOT."

"Open my project in Visual Studio."

"Find out how this game is modded."

"See if GitHub has any other useful tools."

The AI should translate these requests into Endo CLI operations.

## CLI Knowledge

The AI should know Endo's CLI.

It should not invent commands.

It should use the actual command definitions exposed by the command engine.

## Command Chaining

Natural-language requests may require multiple operations.

Example:

"Set up this new Skyrim modding project."

The AI may:

1. Create project.
2. Determine modding ecosystem.
3. Search web.
4. Search GitHub.
5. Identify tools.
6. Read README files.
7. Test tools.
8. Register successful tools.
9. Update project state.
10. Report results.

## Normal Operation

If the user clearly asks for an operation, Endo AI should perform it.

The user should receive notifications/results.

Do not create unnecessary approval gates simply because an AI performed the operation.

Approval requirements that are part of the development workflow remain valid.

## Error Handling

The AI should not immediately give up.

It should:

1. Read error.
2. Understand error.
3. Inspect context.
4. Read documentation.
5. Search web/GitHub where useful.
6. Attempt a reasonable fix.
7. Retry.
8. Stop after a bounded repair budget.
9. Report honestly.

## No Invented State

The AI must never claim:

"Installed successfully."

unless Endo has actual evidence that installation succeeded.

It must never claim:

"Tests passed."

unless tests actually passed.

## Context Sources

AI context may include:

- environment.json
- project.json
- Markdown workflow files
- Active tasks
- Tool manifests
- Command metadata
- Command results
- Git status
- .agents/ project instructions

Do not automatically send the entire environment to every model request.

## Security

Secrets must not be unnecessarily included in:

- AI prompts
- Logs
- Commit messages
- Notifications
- Web searches
- GitHub searches
