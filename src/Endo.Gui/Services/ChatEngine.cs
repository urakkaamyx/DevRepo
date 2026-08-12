using Endo.Core.Ai;
using Endo.Core.Commands;
using Endo.Gui.Models;

namespace Endo.Gui.Services;

/// <summary>
/// Routing rule, checked in order:
/// 1. "!&lt;command&gt;" — a shell escape, run through <see cref="IShellRunner"/> with output
///    streamed live into one growing bubble.
/// 2. "&lt;command.name&gt; key=value ..." — a direct, deterministic command invocation (the format
///    the sidebar's click-to-template feature produces, see <see cref="CommandTemplateBuilder"/>).
///    Recognized whenever the first token is a real registered command name, so a filled-in
///    template never has to round-trip through the AI to be understood.
/// 3. Anything else — natural language, sent through the exact same
///    <see cref="AiOrchestrator.AskAsync"/> loop the CLI's "endo ai ask" uses — the GUI is another
///    caller of the same command-dispatch path, never a second implementation of it
///    (01-ARCHITECTURE.md).
/// </summary>
public sealed class ChatEngine : IChatEngine
{
    // IAiProvider is a single-shot completion contract (claude-cli even runs with
    // --no-session-persistence) — without this, every turn looks like the model's first and only
    // message, so a follow-up like "based on what I just said" has nothing to refer to. Kept only
    // for the natural-language branch, and bounded so the prompt doesn't grow without limit over a
    // long session.
    private const int MaxHistoryTurns = 12;

    private readonly AiOrchestrator _orchestrator;
    private readonly CommandEngine _commandEngine;
    private readonly CommandContext _context;
    private readonly IShellRunner _shellRunner;
    private readonly Action<Action> _marshalToUiThread;
    private readonly List<AiConversationTurn> _history = new();

    public ChatEngine(AiOrchestrator orchestrator, CommandEngine commandEngine, CommandContext context, IShellRunner shellRunner, Action<Action> marshalToUiThread)
    {
        _orchestrator = orchestrator;
        _commandEngine = commandEngine;
        _context = context;
        _shellRunner = shellRunner;
        _marshalToUiThread = marshalToUiThread;
    }

    public async Task SendAsync(string input, Action<ChatMessage> addMessage, CancellationToken cancellationToken = default)
    {
        if (input.StartsWith('!'))
        {
            await RunShellAsync(input[1..].Trim(), addMessage, cancellationToken);
            return;
        }

        var trimmed = input.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        var possibleCommand = firstSpace < 0 ? trimmed : trimmed[..firstSpace];
        if (_commandEngine.TryGetCommand(possibleCommand, out _))
        {
            RunDirectCommand(possibleCommand, firstSpace < 0 ? "" : trimmed[(firstSpace + 1)..], addMessage);
            return;
        }

        var result = await _orchestrator.AskAsync(input, _context, _history, cancellationToken);

        // NeedsClarification means the model is legitimately asking for more information, not
        // reporting a failure — it should read like a normal reply, not an error.
        var role = result.Success || result.NeedsClarification ? ChatRole.Assistant : ChatRole.Error;
        var text = result.ChosenCommand is not null ? $"[{result.ChosenCommand}] {result.Message}" : result.Message;
        addMessage(new ChatMessage(role, text));

        _history.Add(new AiConversationTurn(input, text));
        if (_history.Count > MaxHistoryTurns)
        {
            _history.RemoveAt(0);
        }
    }

    private void RunDirectCommand(string commandName, string argsText, Action<ChatMessage> addMessage)
    {
        if (argsText.Contains('<') && argsText.Contains('>'))
        {
            addMessage(new ChatMessage(ChatRole.Error, $"Fill in the <placeholder> values before sending '{commandName}'."));
            return;
        }

        var args = ParseArgs(argsText);
        var result = _commandEngine.Execute(commandName, _context, args);
        var role = result.Success ? ChatRole.Assistant : ChatRole.Error;
        var text = result.Success ? result.Output : (result.Error ?? "Command failed.");
        addMessage(new ChatMessage(role, $"[{commandName}] {text}"));
    }

    /// <summary>Parses "key=value key2=&quot;value with spaces&quot;" into a dictionary — the format <see cref="CommandTemplateBuilder"/> produces once placeholders are filled in.</summary>
    private static Dictionary<string, string> ParseArgs(string argsText)
    {
        var args = new Dictionary<string, string>();
        var i = 0;
        while (i < argsText.Length)
        {
            while (i < argsText.Length && char.IsWhiteSpace(argsText[i])) i++;
            if (i >= argsText.Length) break;

            var keyStart = i;
            while (i < argsText.Length && argsText[i] != '=' && !char.IsWhiteSpace(argsText[i])) i++;
            if (i >= argsText.Length || argsText[i] != '=') break;
            var key = argsText[keyStart..i];
            i++; // skip '='

            string value;
            if (i < argsText.Length && argsText[i] == '"')
            {
                i++;
                var valueStart = i;
                while (i < argsText.Length && argsText[i] != '"') i++;
                value = argsText[valueStart..i];
                if (i < argsText.Length) i++; // skip closing quote
            }
            else
            {
                var valueStart = i;
                while (i < argsText.Length && !char.IsWhiteSpace(argsText[i])) i++;
                value = argsText[valueStart..i];
            }

            args[key] = value;
        }

        return args;
    }

    private async Task RunShellAsync(string commandLine, Action<ChatMessage> addMessage, CancellationToken cancellationToken)
    {
        if (commandLine.Length == 0)
        {
            addMessage(new ChatMessage(ChatRole.Error, "Nothing to run after '!'."));
            return;
        }

        var shellMessage = new ChatMessage(ChatRole.Shell, "");
        addMessage(shellMessage);

        // PowerShellRunner's output events fire on a threadpool thread; WPF bindings need the
        // property-changed notification to happen on the UI thread, so every append is marshaled.
        var exitCode = await _shellRunner.RunAsync(
            commandLine,
            line => _marshalToUiThread(() => shellMessage.Append(line + "\n")),
            cancellationToken);

        _marshalToUiThread(() =>
        {
            if (shellMessage.Text.Length == 0)
            {
                shellMessage.Append("(no output)");
            }
            shellMessage.Append($"\n[exit {exitCode}]");
        });
    }
}
