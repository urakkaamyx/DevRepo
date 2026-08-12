using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Endo.Core.Commands;
using Endo.Gui.Models;
using Endo.Gui.Services;

namespace Endo.Gui.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IChatEngine _chatEngine;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    /// <summary>The sidebar's catalog — same data 'endo help' prints, bound directly so it can never drift.</summary>
    public IReadOnlyList<CommandDescriptor> Commands { get; }

    public string StatusText { get; }

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public MainViewModel(IChatEngine chatEngine, IReadOnlyList<CommandDescriptor> commands, string statusText)
    {
        _chatEngine = chatEngine;
        Commands = commands;
        StatusText = statusText;
        Messages.Add(new ChatMessage(ChatRole.System,
            "Double-click a command in the sidebar to drop a fill-in-the-blanks template here, type a request in plain English, or prefix with '!' to run a PowerShell command directly."));
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        InputText = string.Empty;
        Messages.Add(new ChatMessage(ChatRole.User, text));

        IsBusy = true;
        try
        {
            await _chatEngine.SendAsync(text, Messages.Add);
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage(ChatRole.Error, $"Unexpected error: {ex.Message}"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(InputText);

    partial void OnIsBusyChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    partial void OnInputTextChanged(string value) => SendCommand.NotifyCanExecuteChanged();
}
