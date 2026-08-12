using Endo.Core.Commands;

namespace Endo.Gui.Services;

/// <summary>
/// Builds a fill-in-the-blanks "name key=&lt;key&gt; ..." template from a command's real
/// parameter names, so clicking a command in the sidebar never has to guess argument syntax —
/// it's the same names <see cref="ICommand.Parameters"/> exposes, not invented ones.
/// </summary>
public static class CommandTemplateBuilder
{
    public static string Build(CommandDescriptor descriptor)
    {
        if (descriptor.Parameters.Count == 0)
        {
            return descriptor.Name;
        }

        var args = string.Join(' ', descriptor.Parameters.Select(p => $"{p}=<{p}>"));
        return $"{descriptor.Name} {args}";
    }
}
