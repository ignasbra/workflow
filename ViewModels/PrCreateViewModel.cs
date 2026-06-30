using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using PrReviewHelper.Services;

namespace PrReviewHelper.ViewModels;

public class PrCreateViewModel : INotifyPropertyChanged
{
    private const int MaxTitleLength = 150;
    private readonly PrCreateService _svc = new();
    private readonly PrCreateSettings _settings;
    private readonly PrReviewSettings _repoPresets = PrReviewSettings.Load();

    public PrCreateViewModel()
    {
        _settings = PrCreateSettings.Load();
        _templatePath = _settings.TemplatePath;
        _baseBranch = _settings.BaseBranch;

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
        BrowseTemplateCommand = new RelayCommand(_ => BrowseTemplate());
        GenerateCommand = new AsyncRelayCommand(_ => GenerateAsync(),
            _ => !string.IsNullOrWhiteSpace(RepoPath) && !string.IsNullOrWhiteSpace(TemplatePath) && File.Exists(TemplatePath));
        CreateCommand = new AsyncRelayCommand(_ => CreateAsync(),
            _ => !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Body) && !string.IsNullOrWhiteSpace(RepoPath));
        OpenPrCommand = new RelayCommand(_ => OpenPr(), _ => !string.IsNullOrWhiteSpace(CreatedUrl));

        _ = TrySeedTitleFromBranchAsync();
    }

    private string _repoPath = "";
    public string RepoPath
    {
        get => _repoPath;
        set
        {
            if (_repoPath == value) return;
            _repoPath = value;
            OnChanged();
            _ = TrySeedTitleFromBranchAsync();
        }
    }

    private string _templatePath;
    public string TemplatePath
    {
        get => _templatePath;
        set { _templatePath = value; OnChanged(); _settings.TemplatePath = value; TrySaveSettings(); }
    }

    private string _baseBranch;
    public string BaseBranch
    {
        get => _baseBranch;
        set { _baseBranch = value; OnChanged(); _settings.BaseBranch = value; TrySaveSettings(); }
    }

    private string _brief = "";
    public string Brief { get => _brief; set { _brief = value; OnChanged(); } }

    private string _title = "";
    public string Title { get => _title; set { _title = value; OnChanged(); } }

    private string _body = "";
    public string Body { get => _body; set { _body = value; OnChanged(); } }

    private string _status = "Idle";
    public string Status { get => _status; set { _status = value; OnChanged(); } }

    private string _createdUrl = "";
    public string CreatedUrl { get => _createdUrl; set { _createdUrl = value; OnChanged(); } }

    public RelayCommand BrowseRepoCommand { get; }
    public RelayCommand UseBackendRepoCommand { get; }
    public RelayCommand UseFrontendRepoCommand { get; }
    public RelayCommand BrowseTemplateCommand { get; }
    public AsyncRelayCommand GenerateCommand { get; }
    public AsyncRelayCommand CreateCommand { get; }
    public RelayCommand OpenPrCommand { get; }

    private void BrowseRepo()
    {
        var dlg = new OpenFolderDialog
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

    private void BrowseTemplate()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select PR template",
            Filter = "Markdown / text (*.md;*.txt;*.markdown)|*.md;*.txt;*.markdown|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() == true) TemplatePath = dlg.FileName;
    }

    private async Task GenerateAsync()
    {
        try
        {
            Status = "Reading template…";
            var template = await File.ReadAllTextAsync(TemplatePath);

            Status = "Gathering git context…";
            var branch = await _svc.GetCurrentBranchAsync(RepoPath);
            var commits = await _svc.GetCommitLogAsync(RepoPath, BaseBranch);
            var diffStat = await _svc.GetDiffStatAsync(RepoPath, BaseBranch);
            var diff = await _svc.GetDiffAsync(RepoPath, BaseBranch, maxBytes: 30_000);

            var settings = PrReviewSettings.Load();
            Status = $"Generating title + description via {settings.AiName}…";
            var prompt = BuildPrompt(branch, BaseBranch, commits, diffStat, diff, template, Brief);
            // Prompt embeds the diff — pass it on stdin, not as a CLI arg, to avoid the
            // Windows command-line length limit ("filename or extension too long").
            var r = await ProcessRunner.RunAsync(settings.AiExecutable, ["-p"], stdin: prompt);
            if (r.ExitCode != 0)
            {
                Status = $"{settings.AiName} failed: {r.StdErr.Trim()}";
                return;
            }
            var parsed = _svc.ParseGenerated(r.StdOut);
            Title = NormalizeTitle(parsed.Title, branch);
            Body = parsed.Body;
            Status = "Generated. Review/edit, then Create PR.";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task CreateAsync()
    {
        var confirm = MessageBox.Show(
            $"Create PR against base '{BaseBranch}'?\n\nTitle: {Title}",
            "Confirm Create PR", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            Status = "Creating PR via gh…";
            var url = await _svc.CreatePrAsync(RepoPath, BaseBranch, Title, Body);
            CreatedUrl = url;
            Status = $"Created: {url}";
            try { Clipboard.SetText(url); } catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            Status = $"Create failed: {ex.Message}";
        }
    }

    private void OpenPr()
    {
        if (string.IsNullOrWhiteSpace(CreatedUrl)) return;
        Process.Start(new ProcessStartInfo(CreatedUrl) { UseShellExecute = true });
    }

    private void TrySaveSettings() { try { _settings.Save(); } catch { /* ignore */ } }

    private static string BuildPrompt(
        string branch, string baseBranch, string commits, string diffStat, string diff, string template, string brief)
    {
        return $@"You are drafting a GitHub Pull Request title and body.

Output EXACTLY this format and nothing else (no preamble, no code-fence wrapping):

<<<TITLE>>>
{{a single-line PR title in imperative tense, ideally under 72 chars, no trailing period}}
<<<DESCRIPTION>>>
{{the PR body in markdown, following the supplied template structure exactly}}
<<<END>>>

Rules for the title:
- Must start with either 'feat: ' or 'fix: '. Use 'fix:' for bug fixes, 'feat:' otherwise.
- Include the ticket id (e.g. 'CLOUD-331') right after the prefix when one is visible in the branch or commits.
- Imperative, concise, specific to what changed. Must stay under 150 characters.
- Final shape: '<prefix>: <TICKET> <subject>' with no trailing period.

Rules for the description:
- Follow the supplied template's section headings/structure exactly. Fill each section based on the git context and brief.
- For sections that don't apply, write 'N/A' rather than omitting them.
- Be specific. Reference filenames and behavior changes, not generic claims.

# Current branch
{branch}

# Base branch
{baseBranch}

# Commits on this branch (oldest first reversed)
{commits}

# Files changed (diff --stat)
{diffStat}

# Diff (may be truncated)
{diff}

# User brief / extra context (may be empty)
{brief}

# PR template (follow this structure)
{template}";
    }

    private async Task TrySeedTitleFromBranchAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoPath)) return;
        if (!string.IsNullOrWhiteSpace(Title)) return;
        try
        {
            var branch = await _svc.GetCurrentBranchAsync(RepoPath);
            var seed = DeriveTitleSeed(branch);
            if (seed is not null && string.IsNullOrWhiteSpace(Title)) Title = seed;
        }
        catch
        {
            // not a git repo, gh not installed, etc. — leave Title empty.
        }
    }

    internal static string? DeriveTitleSeed(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch)) return null;
        var prefix = DerivePrefix(branch);
        var ticket = DeriveTicket(branch);
        return ticket is not null ? $"{prefix} {ticket} " : $"{prefix} ";
    }

    internal static string NormalizeTitle(string raw, string branch)
    {
        var prefix = DerivePrefix(branch);
        var ticket = DeriveTicket(branch);
        var subject = (raw ?? "").Trim();

        subject = Regex.Replace(subject, @"^(feat|feature|fix|bug|bugfix|hotfix)\s*:\s*", "", RegexOptions.IgnoreCase);
        if (ticket is not null)
        {
            subject = Regex.Replace(subject, $@"^{Regex.Escape(ticket)}\s*:?\s*", "", RegexOptions.IgnoreCase);
        }
        subject = subject.TrimEnd('.').Trim();

        var ticketPart = ticket is not null ? $"{ticket} " : "";
        var result = $"{prefix} {ticketPart}{subject}".Trim();
        if (result.Length > MaxTitleLength) result = result[..MaxTitleLength].TrimEnd();
        return result;
    }

    private static string DerivePrefix(string branch)
    {
        var firstSegment = branch.Split('/', '_')[0].ToLowerInvariant();
        return firstSegment is "fix" or "bug" or "bugfix" or "hotfix" ? "fix:" : "feat:";
    }

    private static string? DeriveTicket(string branch)
    {
        var match = Regex.Match(branch, @"[A-Z]+-\d+", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
