using Endo.Core.Diagnostics;

namespace Endo.Core.Git;

public sealed record PushMdResult(string? Path, string? Content);

public sealed record CheckpointResult(bool Success, string Message, List<string> ChangedFiles, string? CommitHash);

/// <summary>
/// Endo's private DevRepo: environment state, configuration, and recovery history, independent
/// from any project's own Git repository (07-GIT-DEVREPO-SPEC.md). Checkpoints are
/// progress-dependent, not tied to every trivial command.
/// </summary>
public sealed class DevRepoService
{
    private readonly string _devRepoPath;
    private readonly string _searchRoot;
    private readonly string? _configPath;
    private readonly Logger _logger;

    /// <param name="devRepoPath">The DevRepo Git working tree.</param>
    /// <param name="searchRoot">The Endo managed root, used for PUSH.md discovery.</param>
    /// <param name="configPath">The live config/ directory (environment.json etc.) to snapshot into DevRepo before each checkpoint. Null skips snapshotting (e.g. in tests against a bare DevRepo).</param>
    public DevRepoService(string devRepoPath, string searchRoot, Logger logger, string? configPath = null)
    {
        _devRepoPath = devRepoPath;
        _searchRoot = searchRoot;
        _configPath = configPath;
        _logger = logger;
    }

    /// <summary>
    /// Locates PUSH.md by searching rather than assuming a fixed path (07-GIT-DEVREPO-SPEC.md
    /// "PUSH.md": "Do not assume it is always located in a hard-coded path"). Searches the DevRepo
    /// and the Endo root, bounded depth, so an interrupted or unusual layout cannot cause an
    /// unbounded scan.
    /// </summary>
    public PushMdResult FindPushMd()
    {
        foreach (var candidateRoot in new[] { _devRepoPath, _searchRoot })
        {
            if (!Directory.Exists(candidateRoot))
            {
                continue;
            }

            var found = SearchBounded(candidateRoot, "PUSH.md", maxDepth: 4);
            if (found is not null)
            {
                return new PushMdResult(found, File.ReadAllText(found));
            }
        }

        return new PushMdResult(null, null);
    }

    private static string? SearchBounded(string root, string fileName, int maxDepth)
    {
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();

            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir).Where(d => !Path.GetFileName(d).Equals(".git", StringComparison.OrdinalIgnoreCase));
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var sub in subDirs)
            {
                queue.Enqueue((sub, depth + 1));
            }
        }

        return null;
    }

    /// <summary>
    /// Reviews actual changed state in the DevRepo working tree and generates a Recommended Push
    /// commit message from it. Must not invent work (07-GIT-DEVREPO-SPEC.md "Recommended Push").
    /// </summary>
    public (string Message, List<string> ChangedFiles) GenerateRecommendedPush()
    {
        var status = GitProcess.Run(_devRepoPath, "status", "--porcelain");
        var changedFiles = status.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Length > 3 ? line[3..].Trim() : line.Trim())
            .Where(f => f.Length > 0)
            .ToList();

        if (changedFiles.Count == 0)
        {
            return ("No changes to checkpoint.", changedFiles);
        }

        var message = $"Checkpoint: {changedFiles.Count} file(s) changed - {string.Join(", ", changedFiles.Take(5))}" +
                      (changedFiles.Count > 5 ? ", ..." : "");
        return (message, changedFiles);
    }

    /// <summary>
    /// Copies the live config/ directory (environment.json, etc.) into the DevRepo working tree.
    /// DevRepo's purpose is versioning environment state/configuration/recovery data
    /// (07-GIT-DEVREPO-SPEC.md) — it must actually contain a copy for `git add`/`commit` to have
    /// anything to version. Only state/config is mirrored, never project or tool binaries/sources,
    /// per the explicit constraint against turning DevRepo into a machine backup.
    /// </summary>
    private void SnapshotConfig()
    {
        if (_configPath is null || !Directory.Exists(_configPath))
        {
            return;
        }

        var destination = Path.Combine(_devRepoPath, "config");
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(_configPath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_configPath, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>Commits currently changed DevRepo state. Returns Success=true with no commit hash when there was nothing to commit.</summary>
    public CheckpointResult Checkpoint(string? overrideMessage = null)
    {
        if (!GitProcess.IsGitRepository(_devRepoPath))
        {
            return new CheckpointResult(false, $"'{_devRepoPath}' is not a Git repository. Run 'endo setup' to initialize DevRepo.", new List<string>(), null);
        }

        SnapshotConfig();

        var (generatedMessage, changedFiles) = GenerateRecommendedPush();
        if (changedFiles.Count == 0)
        {
            return new CheckpointResult(true, "Nothing to checkpoint; DevRepo working tree is clean.", changedFiles, null);
        }

        var message = overrideMessage ?? generatedMessage;

        var add = GitProcess.Run(_devRepoPath, "add", "-A");
        if (!add.Success)
        {
            return new CheckpointResult(false, $"git add failed: {add.StdErr.Trim()}", changedFiles, null);
        }

        var commit = GitProcess.Run(_devRepoPath, "commit", "-m", message);
        if (!commit.Success)
        {
            return new CheckpointResult(false, $"git commit failed: {commit.StdErr.Trim()}", changedFiles, null);
        }

        var revParse = GitProcess.Run(_devRepoPath, "rev-parse", "HEAD");
        var commitHash = revParse.Success ? revParse.StdOut.Trim() : null;

        _logger.Info("DevRepo checkpoint created.", new { message, changedFiles.Count, commitHash });

        return new CheckpointResult(true, message, changedFiles, commitHash);
    }
}
