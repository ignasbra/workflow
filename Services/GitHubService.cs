using System.IO;
using System.Text.Json;
using PrReviewHelper.Models;

namespace PrReviewHelper.Services;

public class GitHubService
{
    public async Task<PrInfo> GetCurrentPrAsync(string repoPath, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("gh",
            ["pr", "view", "--json", "number,title,url,headRepository,headRepositoryOwner"],
            repoPath, ct: ct);
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"gh pr view failed: {r.StdErr}");

        using var doc = JsonDocument.Parse(r.StdOut);
        var root = doc.RootElement;
        var number = root.GetProperty("number").GetInt32();
        var title = root.GetProperty("title").GetString() ?? "";
        var url = root.GetProperty("url").GetString() ?? "";

        // owner/repo from URL: https://github.com/OWNER/REPO/pull/N
        var uri = new Uri(url);
        var segs = uri.AbsolutePath.Trim('/').Split('/');
        var owner = segs[0];
        var repo = segs[1];

        return new PrInfo(owner, repo, number, title, url, repoPath);
    }

    public async Task<List<PrComment>> GetCommentsAsync(PrInfo pr, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("gh",
            ["api", $"repos/{pr.Owner}/{pr.Repo}/pulls/{pr.Number}/comments", "--paginate"],
            pr.LocalPath, ct: ct);
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"gh api failed: {r.StdErr}");

        using var doc = JsonDocument.Parse(r.StdOut);
        var raw = doc.RootElement.EnumerateArray()
            .Select(e => new
            {
                Id = e.GetProperty("id").GetInt64(),
                Author = e.GetProperty("user").GetProperty("login").GetString() ?? "",
                Body = e.GetProperty("body").GetString() ?? "",
                Path = e.GetProperty("path").GetString() ?? "",
                Line = e.TryGetProperty("line", out var ln) && ln.ValueKind == JsonValueKind.Number
                    ? ln.GetInt32()
                    : (e.TryGetProperty("original_line", out var oln) && oln.ValueKind == JsonValueKind.Number ? oln.GetInt32() : 0),
                DiffHunk = e.TryGetProperty("diff_hunk", out var dh) ? dh.GetString() ?? "" : "",
                InReplyToId = e.TryGetProperty("in_reply_to_id", out var irt) && irt.ValueKind == JsonValueKind.Number
                    ? irt.GetInt64()
                    : (long?)null,
                CreatedAt = e.GetProperty("created_at").GetDateTimeOffset(),
            })
            .ToList();

        // Group threads: a top-level comment + its replies
        var roots = raw.Where(c => c.InReplyToId is null).ToList();
        var byParent = raw.Where(c => c.InReplyToId is not null)
            .GroupBy(c => c.InReplyToId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreatedAt).ToList());

        var result = new List<PrComment>();
        foreach (var rootC in roots)
        {
            var thread = new List<CommentThreadEntry>
            {
                new(rootC.Author, rootC.Body, rootC.CreatedAt)
            };
            if (byParent.TryGetValue(rootC.Id, out var replies))
                thread.AddRange(replies.Select(x => new CommentThreadEntry(x.Author, x.Body, x.CreatedAt)));

            // The "active" comment to reply to = the most recent one in the thread
            var last = replies?.LastOrDefault();
            result.Add(new PrComment
            {
                Id = last?.Id ?? rootC.Id,
                Author = last?.Author ?? rootC.Author,
                Body = last?.Body ?? rootC.Body,
                Path = rootC.Path,
                Line = rootC.Line,
                DiffHunk = rootC.DiffHunk,
                InReplyToId = last is null ? null : rootC.Id,
                Thread = thread,
            });
        }
        return result;
    }

    public async Task PostReplyAsync(PrInfo pr, long rootCommentId, string body, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("gh",
            ["api", "-X", "POST",
                $"repos/{pr.Owner}/{pr.Repo}/pulls/{pr.Number}/comments/{rootCommentId}/replies",
                "-f", $"body={body}"],
            pr.LocalPath, ct: ct);
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"gh api post failed: {r.StdErr}");
    }

    public string? ReadCodeContext(string repoPath, string relPath, int line, int contextLines = 10)
    {
        var full = Path.Combine(repoPath, relPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return null;
        var lines = File.ReadAllLines(full);
        if (line <= 0 || line > lines.Length) return null;
        var start = Math.Max(0, line - 1 - contextLines);
        var end = Math.Min(lines.Length - 1, line - 1 + contextLines);
        var sb = new System.Text.StringBuilder();
        for (int i = start; i <= end; i++)
        {
            var marker = (i + 1) == line ? ">" : " ";
            sb.AppendLine($"{marker} {i + 1,5}  {lines[i]}");
        }
        return sb.ToString();
    }
}
