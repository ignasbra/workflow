using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using PrReviewHelper.Models;
using PrReviewHelper.Services;

namespace PrReviewHelper.ViewModels;

public class PrViewModel : INotifyPropertyChanged
{
    private readonly GitHubService _gh = new();
    private readonly AiService _ai = new();
    private readonly PrReviewSettings _settings = PrReviewSettings.Load();

    public ObservableCollection<CommentViewModel> Comments { get; } = new();

    private string _repoPath = "";
    public string RepoPath { get => _repoPath; set { _repoPath = value; OnChanged(); } }

    private string _status = "Idle";
    public string Status { get => _status; set { _status = value; OnChanged(); } }

    private PrInfo? _pr;
    public string PrTitle => _pr is null ? "No PR loaded" : $"#{_pr.Number} — {_pr.Title}";

    private CommentViewModel? _selected;
    public CommentViewModel? Selected
    {
        get => _selected;
        set { _selected = value; OnChanged(); }
    }

    public AsyncRelayCommand LoadCommand { get; }
    public AsyncRelayCommand LoadBackendCommand { get; }
    public AsyncRelayCommand LoadFrontendCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public AsyncRelayCommand RegenerateCommand { get; }
    public RelayCommand StageCommand { get; }
    public AsyncRelayCommand PostCommand { get; }
    public AsyncRelayCommand ImplementCommand { get; }
    public RelayCommand DenyCommand { get; }

    public PrViewModel()
    {
        var args = Environment.GetCommandLineArgs();
        var cliPath = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-') && Directory.Exists(a));
        if (cliPath is not null)
        {
            RepoPath = Path.GetFullPath(cliPath);
        }
        else
        {
            var cwd = Environment.CurrentDirectory;
            if (Directory.Exists(Path.Combine(cwd, ".git"))) RepoPath = cwd;
        }

        LoadCommand = new AsyncRelayCommand(_ => LoadAsync());
        LoadBackendCommand = new AsyncRelayCommand(_ => LoadFromPathAsync(_settings.BackendRepoPath),
            _ => Directory.Exists(_settings.BackendRepoPath));
        LoadFrontendCommand = new AsyncRelayCommand(_ => LoadFromPathAsync(_settings.FrontendRepoPath),
            _ => Directory.Exists(_settings.FrontendRepoPath));
        BrowseCommand = new RelayCommand(_ => Browse());
        RegenerateCommand = new AsyncRelayCommand(_ => RegenerateAsync(Selected), _ => Selected is not null);
        StageCommand = new RelayCommand(_ => Stage(Selected), _ => Selected is not null && !string.IsNullOrWhiteSpace(Selected.Reply));
        PostCommand = new AsyncRelayCommand(_ => PostAsync(Selected), _ => Selected is not null && !string.IsNullOrWhiteSpace(Selected!.Reply));
        ImplementCommand = new AsyncRelayCommand(_ => ImplementAsync(Selected),
            _ => Selected is not null && !string.IsNullOrWhiteSpace(RepoPath) && Directory.Exists(RepoPath));
        DenyCommand = new RelayCommand(_ => Deny(Selected), _ => Selected is not null);

        if (cliPath is not null)
            _ = LoadAsync();
    }

    private async Task LoadFromPathAsync(string path)
    {
        RepoPath = path;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            Status = "Detecting PR…";
            _pr = await _gh.GetCurrentPrAsync(RepoPath);
            OnChanged(nameof(PrTitle));

            Status = "Fetching comments…";
            var comments = await _gh.GetCommentsAsync(_pr);

            Comments.Clear();
            foreach (var c in comments)
            {
                c.CodeContext = _gh.ReadCodeContext(_pr.LocalPath, c.Path, c.Line);
                Comments.Add(new CommentViewModel(c));
            }
            Status = $"Loaded {Comments.Count} comments. Generating suggestions…";

            _ = Task.Run(GenerateAllAsync);
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task GenerateAllAsync()
    {
        foreach (var vm in Comments.ToList())
            await RegenerateAsync(vm);
        Application.Current.Dispatcher.Invoke(() => Status = "Done.");
    }

    private async Task RegenerateAsync(CommentViewModel? vm)
    {
        if (vm is null) return;
        var settings = PrReviewSettings.Load();
        Application.Current.Dispatcher.Invoke(() =>
        {
            vm.State = CommentState.Generating;
            Status = $"Generating reply for {vm.Header} using {settings.AiName}…";
        });
        try
        {
            var reply = await _ai.SuggestReplyAsync(vm.Comment);
            Application.Current.Dispatcher.Invoke(() =>
            {
                vm.Reply = reply;
                vm.State = CommentState.Suggested;
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                vm.Reply = $"[error: {ex.Message}]";
                vm.State = CommentState.Error;
            });
        }
    }

    private void Stage(CommentViewModel? vm)
    {
        if (vm is null) return;
        Clipboard.SetText(vm.Reply);
        vm.State = CommentState.Staged;
        Status = "Copied to clipboard.";
    }

    private async Task PostAsync(CommentViewModel? vm)
    {
        if (vm is null || _pr is null) return;
        var confirm = MessageBox.Show(
            $"Post this reply to GitHub?\n\nThread: {vm.Header}\n\n{vm.Reply}",
            "Confirm Post", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            Status = "Posting…";
            var rootId = vm.Comment.InReplyToId ?? vm.Comment.Id;
            await _gh.PostReplyAsync(_pr, rootId, vm.Reply);
            vm.State = CommentState.Posted;
            Status = "Posted.";
        }
        catch (Exception ex)
        {
            vm.State = CommentState.Error;
            Status = $"Post failed: {ex.Message}";
        }
    }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select repo folder",
            InitialDirectory = Directory.Exists(RepoPath) ? RepoPath : Environment.CurrentDirectory,
        };
        if (dlg.ShowDialog() == true)
        {
            RepoPath = dlg.FolderName;
            Status = $"Selected: {RepoPath}";
        }
    }

    private async Task ImplementAsync(CommentViewModel? vm)
    {
        if (vm is null) return;
        var settings = PrReviewSettings.Load();
        var confirm = MessageBox.Show(
            $"Let {settings.AiName} implement this comment's request in {RepoPath}?\n\nThis modifies files on disk (no git commit, no staging).\n\nThread: {vm.Header}",
            "Confirm Implement", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        var prior = vm.State;
        vm.State = CommentState.Implementing;
        Status = $"Implementing change for {vm.Header} using {settings.AiName}…";
        try
        {
            var result = await _ai.ImplementCommentAsync(vm.Comment, RepoPath);
            if (result.ExitCode != 0)
            {
                vm.State = CommentState.Error;
                Status = $"Implement failed: {result.StdErr.Trim()}";
                return;
            }
            var summary = result.StdOut.Trim();
            if (summary.StartsWith("BLOCKED:", StringComparison.Ordinal))
            {
                vm.State = prior;
                MessageBox.Show(summary, $"{settings.AiName} needs more context", MessageBoxButton.OK, MessageBoxImage.Information);
                Status = "Implementation blocked — see message.";
                return;
            }
            vm.State = CommentState.Implemented;
            Status = "Implemented. Review the diff in your editor before posting.";
            if (!string.IsNullOrWhiteSpace(summary))
                MessageBox.Show(summary, "Implementation summary", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            vm.State = CommentState.Error;
            Status = $"Implement failed: {ex.Message}";
        }
    }

    private void Deny(CommentViewModel? vm)
    {
        if (vm is null) return;
        vm.State = CommentState.Denied;
        Status = "Denied.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
