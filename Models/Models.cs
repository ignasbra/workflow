namespace PrReviewHelper.Models;

public record PrInfo(string Owner, string Repo, int Number, string Title, string Url, string LocalPath);

public record CommentThreadEntry(string Author, string Body, DateTimeOffset CreatedAt);

public class PrComment
{
    public required long Id { get; init; }
    public required string Author { get; init; }
    public required string Body { get; init; }
    public required string Path { get; init; }
    public required int Line { get; init; }
    public required string DiffHunk { get; init; }
    public long? InReplyToId { get; init; }
    public List<CommentThreadEntry> Thread { get; init; } = new();
    public string? CodeContext { get; set; }
}