using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Endo.Core.Ai;
using Endo.Core.Commands;
using Endo.Gui.Models;
using Endo.Gui.Services;
using Endo.Gui.ViewModels;
using Endo.Gui.Views;

namespace Endo.Gui;

public partial class MainWindow : Window
{
    private readonly CommandEngine _commandEngine;
    private readonly CommandContext _context;
    private readonly AiOrchestrator _orchestrator;

    public MainWindow(CommandEngine commandEngine, CommandContext context, AiOrchestrator orchestrator)
    {
        InitializeComponent();
        _commandEngine = commandEngine;
        _context = context;
        _orchestrator = orchestrator;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.Messages.CollectionChanged -= OnMessagesChanged;
        }

        if (e.NewValue is MainViewModel newVm)
        {
            newVm.Messages.CollectionChanged += OnMessagesChanged;
        }
    }

    // New bubbles (and shell bubbles growing live) both need the log to keep following the
    // bottom, the same way a terminal does — so both the collection itself and each message's
    // own Text property are watched.
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ChatMessage message in e.NewItems)
            {
                message.PropertyChanged += OnMessageTextChanged;
            }
        }

        ScrollToEnd();
    }

    private void OnMessageTextChanged(object? sender, PropertyChangedEventArgs e) => ScrollToEnd();

    private void ScrollToEnd() => Dispatcher.BeginInvoke(() => ChatScrollViewer.ScrollToEnd());

    // Must be PreviewKeyDown (tunneling), not KeyDown: a multiline TextBox's own AcceptsReturn
    // handling runs on KeyDown, so by the time a KeyDown handler here would fire, the newline has
    // already been inserted regardless of e.Handled. Intercepting on the tunneling pass instead
    // lets plain Enter be suppressed cleanly while Shift+Enter is left alone to insert its newline.
    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers == ModifierKeys.Shift)
        {
            return;
        }

        e.Handled = true;
        if (DataContext is MainViewModel vm && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
        }
    }

    private void CommandsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CommandsList.SelectedItem is not CommandDescriptor descriptor || DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.InputText = CommandTemplateBuilder.Build(descriptor);
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
    }

    private void NewProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new NewProjectWindow(_commandEngine, _context, _orchestrator) { Owner = this };
        window.ShowDialog();
    }
}
