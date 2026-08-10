namespace Endo.Core.Environment;

public sealed class UpdatesInfo
{
    public bool AutoCheck { get; set; } = true;
    public DateTimeOffset? LastChecked { get; set; }
}
