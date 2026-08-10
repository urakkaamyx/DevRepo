namespace Endo.Core.Setup;

/// <summary>
/// Prompt/confirm functions are injected rather than hard-wired to Console so SetupService stays
/// unit-testable and so the same interactive flow can, in principle, be driven by something other
/// than a terminal later.
/// </summary>
public sealed class SetupPrompts
{
    public required Func<string, string, string> Prompt { get; init; } // (question, defaultValue) -> answer
    public required Func<string, bool, bool> Confirm { get; init; } // (question, defaultValue) -> yes/no

    public static SetupPrompts Console() => new()
    {
        Prompt = (question, defaultValue) =>
        {
            System.Console.Write(string.IsNullOrEmpty(defaultValue) ? $"{question}: " : $"{question} [{defaultValue}]: ");
            var line = System.Console.ReadLine();
            return string.IsNullOrWhiteSpace(line) ? defaultValue : line.Trim();
        },
        Confirm = (question, defaultValue) =>
        {
            var suffix = defaultValue ? "[Y/n]" : "[y/N]";
            System.Console.Write($"{question} {suffix}: ");
            var line = System.Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                return defaultValue;
            }
            return line.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ||
                   line.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
        }
    };
}
