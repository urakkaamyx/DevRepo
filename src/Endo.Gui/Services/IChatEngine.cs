using Endo.Gui.Models;

namespace Endo.Gui.Services;

/// <summary>
/// Routes one line of chat input to either a shell escape or AI orchestration, and reports
/// resulting bubbles back through <paramref name="addMessage"/> so the ViewModel decides how
/// they're displayed. See <see cref="ChatEngine"/> for the routing rule.
/// </summary>
public interface IChatEngine
{
    Task SendAsync(string input, Action<ChatMessage> addMessage, CancellationToken cancellationToken = default);
}
