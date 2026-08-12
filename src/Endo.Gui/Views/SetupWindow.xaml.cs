using System.Windows;
using System.Windows.Controls;
using Endo.Core.Ai;
using Endo.Core.Environment;
using Endo.Core.Setup;

namespace Endo.Gui.Views;

/// <summary>
/// Graphical equivalent of <see cref="SetupService.RunInteractive"/> — gathers the same answers a
/// console prompt would and calls the exact same deterministic <see cref="SetupService.Apply"/>
/// core, so the GUI is another caller of the real setup flow, not a second implementation of it.
/// Plain code-behind rather than a ViewModel: this is a one-shot form with no ongoing state to
/// keep in sync, so a full MVVM layer would be ceremony without benefit.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly IClaudeCliInstaller _claudeCliInstaller = new ClaudeCliInstaller();

    public SetupWindow()
    {
        InitializeComponent();

        var suggestedRoot = RootLocator.TryLocateRoot() ?? RootLocator.SuggestDefaultRoot();
        RootBox.Text = suggestedRoot;
        WorkspaceBox.Text = RootLocator.SuggestDefaultWorkspace(suggestedRoot);

        // Set after InitializeComponent (not via IsSelected="True" in XAML) — XAML-time selection
        // fires SelectionChanged mid-parse, before ClaudeCliPanel and the other named fields below
        // it in the visual tree have been assigned yet, which crashed with a NullReferenceException.
        ProviderCombo.SelectedIndex = 0;
    }

    private string? SelectedProvider() => (ProviderCombo.SelectedItem as ComboBoxItem)?.Tag as string;

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var isClaudeCli = SelectedProvider() == "claude-cli";
        ClaudeCliPanel.Visibility = isClaudeCli ? Visibility.Visible : Visibility.Collapsed;
        if (isClaudeCli)
        {
            RefreshClaudeCliStatus();
        }
    }

    private void RefreshClaudeCliStatus()
    {
        var status = _claudeCliInstaller.GetStatus();
        ClaudeCliStatusText.Text = !status.Installed
            ? "Not installed."
            : status.LoggedIn == true
                ? $"Installed, logged in as {status.Email}."
                : "Installed, not logged in.";
    }

    private async void InstallClaudeCliButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Installing claude CLI via npm...", _claudeCliInstaller.InstallViaNpm);
        RefreshClaudeCliStatus();
    }

    private async void LoginClaudeCliButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Waiting for 'claude auth login' to complete...", _claudeCliInstaller.Login);
        RefreshClaudeCliStatus();
    }

    private async Task RunBusyAsync(string busyText, Func<ClaudeCliActionResult> action)
    {
        StatusMessage.Foreground = System.Windows.Media.Brushes.SteelBlue;
        StatusMessage.Text = busyText;
        IsEnabled = false;
        try
        {
            var result = await Task.Run(action);
            StatusMessage.Foreground = result.Success
                ? System.Windows.Media.Brushes.MediumSeaGreen
                : System.Windows.Media.Brushes.IndianRed;
            StatusMessage.Text = result.Message;
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var root = RootBox.Text.Trim();
        var workspace = WorkspaceBox.Text.Trim();
        if (root.Length == 0 || workspace.Length == 0)
        {
            StatusMessage.Foreground = System.Windows.Media.Brushes.IndianRed;
            StatusMessage.Text = "Root and workspace paths are required.";
            return;
        }

        var provider = SelectedProvider();
        var model = ModelBox.Text.Trim();

        var setupService = new SetupService(_claudeCliInstaller);
        var result = setupService.Apply(new SetupAnswers(
            root,
            workspace,
            DevRepoCheck.IsChecked == true,
            string.IsNullOrWhiteSpace(provider) ? null : provider,
            AutoUpdateCheck.IsChecked == true,
            string.IsNullOrWhiteSpace(model) ? null : model));

        if (!result.Success)
        {
            StatusMessage.Foreground = System.Windows.Media.Brushes.IndianRed;
            StatusMessage.Text = result.Message;
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
