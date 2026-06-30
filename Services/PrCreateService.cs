using System.Text;
using System.Text.RegularExpressions;

namespace PrReviewHelper.Services;

public class PrCreateService
{
    public async Task<string> GetCurrentBranchAsync(string repoPath, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["branch", "--show-current"], repoPath, ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git failed: {r.StdErr}");
        return r.StdOut.Trim();
    }

    public async Task<string> GetCommitLogAsync(string repoPath, string baseBranch, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git",
            ["log", $"{baseBranch}..HEAD", "--pretty=format:%h %s%n%b%n---"],
            repoPath, ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git log failed: {r.StdErr}");
        return r.StdOut.Trim();
    }

    public async Task<string> GetDiffStatAsync(string repoPath, string baseBranch, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["diff", "--stat", $"{baseBranch}...HEAD"], repoPath, ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git diff --stat failed: {r.StdErr}");
        return r.StdOut.Trim();
    }

    public async Task<string> GetDiffAsync(string repoPath, string baseBranch, int maxBytes, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["diff", $"{baseBranch}...HEAD"], repoPath, ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git diff failed: {r.StdErr}");
        var diff = r.StdOut;
        if (diff.Length > maxBytes) diff = diff[..maxBytes] + "\n\n[…diff truncated…]";
        return diff;
    }

    public async Task<string> CreatePrAsync(string repoPath, string baseBranch, string title, string body, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("gh",
            ["pr", "create", "--title", title, "--body-file", "-", "--base", baseBranch],
            repoPath, stdin: body, ct: ct);
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"gh pr create failed: {r.StdErr.Trim()}");
        // gh prints the PR URL on stdout
        return r.StdOut.Trim();
    }

    public record GeneratedPr(string Title, string Body);

    public GeneratedPr ParseGenerated(string raw)
    {
        // Expect:
        //   <<<TITLE>>>
        //   the title
        //   <<<DESCRIPTION>>>
        //   the body
        //   <<<END>>>
        var titleMatch = Regex.Match(raw, @"<<<TITLE>>>\s*\n(.*?)\n\s*<<<DESCRIPTION>>>", RegexOptions.Singleline);
        var bodyMatch = Regex.Match(raw, @"<<<DESCRIPTION>>>\s*\n(.*?)(?:\n\s*<<<END>>>|$)", RegexOptions.Singleline);
        if (!titleMatch.Success || !bodyMatch.Success)
            throw new InvalidOperationException("Could not parse AI output. Raw:\n" + raw);
        return new GeneratedPr(titleMatch.Groups[1].Value.Trim(), bodyMatch.Groups[1].Value.Trim());
    }
}
