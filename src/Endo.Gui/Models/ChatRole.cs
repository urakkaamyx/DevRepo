namespace Endo.Gui.Models;

/// <summary>Who/what produced a chat bubble — drives both display side and styling in MainWindow.xaml.</summary>
public enum ChatRole
{
    User,
    Assistant,
    Shell,
    System,
    Error,
}
