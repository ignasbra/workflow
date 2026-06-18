using System.Text;
using PrReviewHelper.Models;

namespace PrReviewHelper.Services;

public class ClaudeService
{
    public async Task<string> SuggestReplyAsync(PrComment c, CancellationToken ct = default)
    {
        var prompt = BuildPrompt(c);
        // Pass the prompt via stdin, not as a CLI arg — large prompts exceed the Windows
        // command-line length limit and fail CreateProcess with "filename or extension too long".
        var r = await ProcessRunner.RunAsync("claude", ["-p"], stdin: prompt, ct: ct);
        if (r.ExitCode != 0)
            return $"[claude error: {r.StdErr.Trim()}]";
        return r.StdOut.Trim();
    }

    public async Task<ProcessResult> ImplementCommentAsync(PrComment c, string repoPath, CancellationToken ct = default)
    {
        var prompt = BuildImplementPrompt(c);
        // Prompt goes on stdin (see SuggestReplyAsync) to avoid the command-line length limit.
        return await ProcessRunner.RunAsync(
            "claude",
            ["-p", "--dangerously-skip-permissions"],
            workingDirectory: repoPath,
            stdin: prompt,
            ct: ct);
    }

    private static string BuildImplementPrompt(PrComment c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are implementing a code change requested in a GitHub pull request review comment.");
        sb.AppendLine("Make the change directly in the working tree. Do NOT run git, do NOT commit. Only edit files.");
        sb.AppendLine("Keep the change minimal and focused on what the reviewer asked. Do not refactor unrelated code.");
        sb.AppendLine("If the request is ambiguous or you would need additional context to do it safely, output a single line starting with 'BLOCKED:' and explain — do not guess.");
        sb.AppendLine("When done, output a short one-paragraph summary of what you changed and which files you touched.");
        sb.AppendLine();
        sb.AppendLine($"File: {c.Path}  (line {c.Line})");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(c.CodeContext))
        {
            sb.AppendLine("Code context (around the reviewed line):");
            sb.AppendLine("```");
            sb.AppendLine(c.CodeContext);
            sb.AppendLine("```");
            sb.AppendLine();
        }
        else if (!string.IsNullOrWhiteSpace(c.DiffHunk))
        {
            sb.AppendLine("Diff hunk:");
            sb.AppendLine("```");
            sb.AppendLine(c.DiffHunk);
            sb.AppendLine("```");
            sb.AppendLine();
        }
        sb.AppendLine("Comment thread (oldest first):");
        foreach (var t in c.Thread)
            sb.AppendLine($"  [{t.Author}] {t.Body}");
        sb.AppendLine();
        sb.AppendLine($"Implement the change requested by {c.Thread[^1].Author} in their most recent message.");
        return sb.ToString();
    }

    private static string BuildPrompt(PrComment c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are helping respond to a GitHub pull request review comment. Be concise (1-3 sentences), professional, and direct. Output ONLY the reply text — no preamble, no quotes, no markdown wrapping.");
        sb.AppendLine();
        sb.AppendLine($"File: {c.Path}  (line {c.Line})");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(c.CodeContext))
        {
            sb.AppendLine("Code context:");
            sb.AppendLine("```");
            sb.AppendLine(c.CodeContext);
            sb.AppendLine("```");
            sb.AppendLine();
        }
        else if (!string.IsNullOrWhiteSpace(c.DiffHunk))
        {
            sb.AppendLine("Diff hunk:");
            sb.AppendLine("```");
            sb.AppendLine(c.DiffHunk);
            sb.AppendLine("```");
            sb.AppendLine();
        }
        sb.AppendLine("Comment thread (oldest first):");
        foreach (var t in c.Thread)
            sb.AppendLine($"  [{t.Author}] {t.Body}");
        sb.AppendLine();
        sb.AppendLine($"Write a reply to the most recent message from {c.Thread[^1].Author}.");
        return sb.ToString();
    }
}
