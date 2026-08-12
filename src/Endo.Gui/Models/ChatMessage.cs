using CommunityToolkit.Mvvm.ComponentModel;

namespace Endo.Gui.Models;

/// <summary>
/// One chat bubble. <see cref="Text"/> is observable so a shell-escape bubble can grow live as
/// PowerShell output streams in, rather than only appearing once the process exits.
/// </summary>
public sealed partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    private string _text;

    public ChatRole Role { get; }

    public DateTime Timestamp { get; }

    public ChatMessage(ChatRole role, string text)
    {
        Role = role;
        _text = text;
        Timestamp = DateTime.Now;
    }

    public void Append(string chunk) => Text += chunk;
}
