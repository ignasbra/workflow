namespace PrReviewHelper.Services;

public class GitBranchService
{
    public async Task<bool> IsCleanAsync(string repoPath, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["status", "--porcelain"], repoPath, ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git status failed: {r.StdErr.Trim()}");
        return string.IsNullOrWhiteSpace(r.StdOut);
    }

    public async Task<string> GetCurrentBranchAsync(string repoPath, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], repoPath, ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git rev-parse failed: {r.StdErr.Trim()}");
        return r.StdOut.Trim();
    }

    public async Task<bool> LocalBranchExistsAsync(string repoPath, string name, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["rev-parse", "--verify", "--quiet", $"refs/heads/{name}"], repoPath, ct: ct);
        return r.ExitCode == 0;
    }

    public async Task<bool> RemoteBranchExistsAsync(string repoPath, string name, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["ls-remote", "--heads", "origin", name], repoPath, ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git ls-remote failed: {r.StdErr.Trim()}");
        return !string.IsNullOrWhiteSpace(r.StdOut);
    }

    public async Task SyncMainAsync(string repoPath, string baseBranch, CancellationToken ct = default)
    {
        var fetch = await ProcessRunner.RunAsync("git", ["fetch", "origin", baseBranch], repoPath, ct: ct);
        if (fetch.ExitCode != 0) throw new InvalidOperationException($"git fetch failed: {fetch.StdErr.Trim()}");

        var switchTo = await ProcessRunner.RunAsync("git", ["switch", baseBranch], repoPath, ct: ct);
        if (switchTo.ExitCode != 0) throw new InvalidOperationException($"git switch {baseBranch} failed: {switchTo.StdErr.Trim()}");

        var pull = await ProcessRunner.RunAsync("git", ["pull", "--ff-only", "origin", baseBranch], repoPath, ct: ct);
        if (pull.ExitCode != 0) throw new InvalidOperationException($"git pull failed: {pull.StdErr.Trim()}");
    }

    public async Task CreateBranchAsync(string repoPath, string branchName, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["switch", "-c", branchName], repoPath, ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git switch -c failed: {r.StdErr.Trim()}");
    }

    /// <summary>Stashes tracked and untracked changes. Returns git's message (e.g. "No local changes to save").</summary>
    public async Task<string> StashAsync(string repoPath, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["stash", "push", "--include-untracked"], repoPath, ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git stash failed: {r.StdErr.Trim()}");
        return r.StdOut.Trim();
    }

    public async Task StashPopAsync(string repoPath, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["stash", "pop"], repoPath, ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git stash pop failed: {r.StdErr.Trim()}");
    }

    public async Task StageAllAsync(string repoPath, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["add", "-A"], repoPath, ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git add failed: {r.StdErr.Trim()}");
    }

    public async Task CommitAsync(string repoPath, string message, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["commit", "-m", message], repoPath, ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git commit failed: {r.StdErr.Trim()}");
    }

    /// <summary>Pushes the current branch, setting the upstream if it isn't tracking one yet.</summary>
    public async Task PushAsync(string repoPath, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["push", "-u", "origin", "HEAD"], repoPath, ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git push failed: {r.StdErr.Trim()}");
    }
}
