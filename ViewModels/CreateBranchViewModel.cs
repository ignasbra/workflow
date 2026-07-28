using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using PrReviewHelper.Services;

namespace PrReviewHelper.ViewModels;

public class CreateBranchViewModel : INotifyPropertyChanged
{
    private const int MaxBranchLength = 30;
    private const string BaseBranch = "main";

    private readonly GitBranchService _git = new();
    private readonly JiraSettings _jira = JiraSettings.Load();
    private readonly PrReviewSettings _repoPresets = PrReviewSettings.Load();

    public CreateBranchViewModel()
    {
        IssuesView = CollectionViewSource.GetDefaultView(Issues);
        IssuesView.Filter = MatchesAssigneeFilter;
        AssigneeOptions = new() { AllAssignees };

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

        BrowseRepoCommand = new RelayCommand(_ => BrowseRepo());
        UseBackendRepoCommand = new RelayCommand(_ => RepoPath = _repoPresets.BackendRepoPath,
            _ => Directory.Exists(_repoPresets.BackendRepoPath));
        UseFrontendRepoCommand = new RelayCommand(_ => RepoPath = _repoPresets.FrontendRepoPath,
            _ => Directory.Exists(_repoPresets.FrontendRepoPath));
        CreateCommand = new AsyncRelayCommand(_ => CreateAsync(),
            _ => !string.IsNullOrWhiteSpace(RepoPath) && Directory.Exists(RepoPath) && !string.IsNullOrWhiteSpace(Ticket));
        StashCommand = new AsyncRelayCommand(_ => StashAsync(), _ => HasRepo);
        StashPopCommand = new AsyncRelayCommand(_ => StashPopAsync(), _ => HasRepo);
        RefreshIssuesCommand = new AsyncRelayCommand(_ => RefreshIssuesAsync());

        _ = RefreshIssuesAsync();
    }

    private const string AllAssignees = "All";

    public ObservableCollection<SprintIssueViewModel> Issues { get; } = new();
    public ICollectionView IssuesView { get; }
    public ObservableCollection<string> AssigneeOptions { get; }

    private string _assigneeFilter = AllAssignees;
    public string AssigneeFilter
    {
        get => _assigneeFilter;
        set { _assigneeFilter = value; OnChanged(); IssuesView.Refresh(); }
    }

    private SprintIssueViewModel? _selectedIssue;
    public SprintIssueViewModel? SelectedIssue
    {
        get => _selectedIssue;
        set
        {
            _selectedIssue = value;
            OnChanged();
            if (value is not null) Ticket = value.Key;
        }
    }

    public AsyncRelayCommand RefreshIssuesCommand { get; }

    private string _repoPath = "";
    public string RepoPath
    {
        get => _repoPath;
        set { _repoPath = value; OnChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    private string _ticket = "";
    public string Ticket
    {
        get => _ticket;
        set { _ticket = value; OnChanged(); OnChanged(nameof(PreviewBranchName)); CommandManager.InvalidateRequerySuggested(); }
    }

    private string _status = "Idle";
    public string Status { get => _status; set { _status = value; OnChanged(); } }

    /// <summary>Best-effort local preview built only from the ticket key — real summary comes from Jira at Create time.</summary>
    public string PreviewBranchName => string.IsNullOrWhiteSpace(Ticket) ? "" : $"feat/{NormalizeKey(Ticket)}-…";

    private bool HasRepo => !string.IsNullOrWhiteSpace(RepoPath) && Directory.Exists(RepoPath);

    public RelayCommand BrowseRepoCommand { get; }
    public RelayCommand UseBackendRepoCommand { get; }
    public RelayCommand UseFrontendRepoCommand { get; }
    public AsyncRelayCommand CreateCommand { get; }
    public AsyncRelayCommand StashCommand { get; }
    public AsyncRelayCommand StashPopCommand { get; }

    private async Task StashAsync()
    {
        try
        {
            Status = "Stashing changes…";
            var message = await _git.StashAsync(RepoPath);
            Status = string.IsNullOrEmpty(message) ? "Stashed changes." : message;
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task StashPopAsync()
    {
        try
        {
            Status = "Popping stash…";
            await _git.StashPopAsync(RepoPath);
            Status = "Popped stash.";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task CreateAsync()
    {
        try
        {
            var key = NormalizeKey(Ticket);
            Status = $"Fetching {key} from Jira…";
            var jiraClient = new JiraService(_jira);
            var issue = await jiraClient.GetIssueAsync(key);

            var branchName = BuildBranchName(issue.Key, issue.Summary, issue.IssueTypeName);

            var confirm = MessageBox.Show(
                $"Create branch '{branchName}' from '{BaseBranch}' in {RepoPath}?\n\nThis will: fetch + switch to {BaseBranch}, pull --ff-only, then create the new branch.",
                "Confirm Create Branch", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK) { Status = "Cancelled."; return; }

            if (!await _git.IsCleanAsync(RepoPath))
            {
                Status = "Working tree has uncommitted changes. Stash or commit first.";
                return;
            }

            if (await _git.LocalBranchExistsAsync(RepoPath, branchName))
            {
                Status = $"Branch '{branchName}' already exists locally.";
                return;
            }
            if (await _git.RemoteBranchExistsAsync(RepoPath, branchName))
            {
                Status = $"Branch '{branchName}' already exists on origin.";
                return;
            }

            Status = $"Syncing {BaseBranch}…";
            await _git.SyncMainAsync(RepoPath, BaseBranch);

            Status = $"Creating {branchName}…";
            await _git.CreateBranchAsync(RepoPath, branchName);

            Status = $"On {branchName}.";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task RefreshIssuesAsync()
    {
        try
        {
            if (!_jira.HasCredentials)
            {
                Status = "Jira credentials not set — configure them in the Jira tab.";
                return;
            }
            Status = "Loading active-sprint tickets…";
            var jiraClient = new JiraService(_jira);
            var issues = await jiraClient.GetActiveSprintIssuesAsync(_jira.ProjectKey);

            Issues.Clear();
            foreach (var i in issues) Issues.Add(new SprintIssueViewModel(i));

            var assignees = issues.Select(i => i.AssigneeName).Distinct().OrderBy(n => n).ToList();
            AssigneeOptions.Clear();
            AssigneeOptions.Add(AllAssignees);
            foreach (var a in assignees) AssigneeOptions.Add(a);
            if (!AssigneeOptions.Contains(_assigneeFilter)) AssigneeFilter = AllAssignees;

            Status = $"Loaded {Issues.Count} ticket(s).";
        }
        catch (Exception ex)
        {
            Status = $"Error loading tickets: {ex.Message}";
        }
    }

    private bool MatchesAssigneeFilter(object obj)
    {
        if (obj is not SprintIssueViewModel vm) return false;
        if (_assigneeFilter == AllAssignees) return true;
        return string.Equals(vm.AssigneeName, _assigneeFilter, StringComparison.Ordinal);
    }

    private void BrowseRepo()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select repo folder",
            InitialDirectory = Directory.Exists(RepoPath) ? RepoPath : Environment.CurrentDirectory,
        };
        if (dlg.ShowDialog() == true) RepoPath = dlg.FolderName;
    }

    /// <summary>Accepts 'CLOUD-331', 'cloud-331', or just '331' — defaults to JiraSettings.ProjectKey.</summary>
    private string NormalizeKey(string input)
    {
        var trimmed = input.Trim();
        if (Regex.IsMatch(trimmed, @"^\d+$"))
            return $"{_jira.ProjectKey}-{trimmed}";
        return trimmed.ToUpperInvariant();
    }

    internal static string BuildBranchName(string key, string summary, string issueType)
    {
        var prefix = issueType.Equals("Bug", StringComparison.OrdinalIgnoreCase) ? "fix" : "feat";
        var slug = Slugify(summary);
        var full = $"{prefix}/{key}-{slug}".TrimEnd('-');

        if (full.Length <= MaxBranchLength) return full;
        full = full[..MaxBranchLength];
        // don't end on a hyphen — would fail k8s namespace rules and looks awkward
        return full.TrimEnd('-');
    }

    private static string Slugify(string s)
    {
        var lower = s.ToLowerInvariant();
        var replaced = Regex.Replace(lower, "[^a-z0-9]+", "-");
        return replaced.Trim('-');
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
