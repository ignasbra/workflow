using System.ComponentModel;
using System.Runtime.CompilerServices;
using PrReviewHelper.Models;

namespace PrReviewHelper.ViewModels;

public enum CommentState { Pending, Generating, Suggested, Staged, Posted, Denied, Implementing, Implemented, Error }

public class CommentViewModel(PrComment comment) : INotifyPropertyChanged
{
    public PrComment Comment { get; } = comment;

    public string Header => $"{Comment.Path[(Comment.Path.LastIndexOf('/') + 1)..]}:{Comment.Line}  —  {Comment.Author}";
    public string ThreadText => string.Join("\n\n", Comment.Thread.Select(t => $"[{t.Author}]  ({t.CreatedAt.LocalDateTime:g})\n{t.Body}"));
    public string CodeContext => Comment.CodeContext ?? Comment.DiffHunk;
    public string FileLine => $"{Comment.Path}  (line {Comment.Line})";

    private string _reply = "";
    public string Reply { get => _reply; set { _reply = value; OnChanged(); } }

    private CommentState _state = CommentState.Pending;
    public CommentState State { get => _state; set { _state = value; OnChanged(); OnChanged(nameof(StateLabel)); } }
    public string StateLabel => State switch
    {
        CommentState.Pending => "·",
        CommentState.Generating => "…",
        CommentState.Suggested => "✎",
        CommentState.Staged => "📋",
        CommentState.Posted => "✓",
        CommentState.Denied => "✗",
        CommentState.Implementing => "⚙",
        CommentState.Implemented => "✅",
        CommentState.Error => "!",
        _ => ""
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}