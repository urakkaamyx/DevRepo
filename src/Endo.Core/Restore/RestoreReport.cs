namespace Endo.Core.Restore;

/// <summary>
/// Final restore report categories, per 10-RESTORE-MIGRATION-SPEC.md "Final Restore Report":
/// must distinguish Restored, Already present, Repaired, Changed, Missing, Unresolved,
/// Existing but unmanaged, and Warnings. Endo must not report "restore successful" when required
/// components remain unresolved.
/// </summary>
public sealed class RestoreReport
{
    public List<string> Restored { get; } = new();
    public List<string> AlreadyPresent { get; } = new();
    public List<string> Repaired { get; } = new();
    public List<string> Changed { get; } = new();
    public List<string> Missing { get; } = new();
    public List<string> Unresolved { get; } = new();
    public List<string> ExistingButUnmanaged { get; } = new();
    public List<string> Warnings { get; } = new();

    public bool FullySuccessful => Missing.Count == 0 && Unresolved.Count == 0;

    public string Summarize()
    {
        return $"Restored: {Restored.Count}, Already present: {AlreadyPresent.Count}, Repaired: {Repaired.Count}, " +
               $"Changed: {Changed.Count}, Missing: {Missing.Count}, Unresolved: {Unresolved.Count}, " +
               $"Existing but unmanaged: {ExistingButUnmanaged.Count}, Warnings: {Warnings.Count}";
    }
}
