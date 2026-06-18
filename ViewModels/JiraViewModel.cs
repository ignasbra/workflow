using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using PrReviewHelper.Services;

namespace PrReviewHelper.ViewModels;

public class JiraViewModel : INotifyPropertyChanged
{
    private readonly JiraSettings _settings;

    public JiraViewModel()
    {
        _settings = JiraSettings.Load();
        _dorTemplatePath = _settings.DorTemplatePath;

        BrowseDorCommand = new RelayCommand(_ => BrowseDor());
        GenerateCommand = new AsyncRelayCommand(_ => GenerateAsync(),
            _ => !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(DorTemplatePath) && File.Exists(DorTemplatePath));
        CreateCommand = new AsyncRelayCommand(_ => CreateAsync(),
            _ => !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Description));
        SettingsCommand = new RelayCommand(_ => OpenSettings());
        OpenTicketCommand = new RelayCommand(_ => OpenTicket(), _ => !string.IsNullOrWhiteSpace(CreatedUrl));
    }

    public List<string> IssueTypes { get; } = new() { "Story", "Bug", "Task", "Spike" };

    private string _title = "";
    public string Title { get => _title; set { _title = value; OnChanged(); } }

    private string _brief = "";
    public string Brief { get => _brief; set { _brief = value; OnChanged(); } }

    private string _dorTemplatePath;
    public string DorTemplatePath
    {
        get => _dorTemplatePath;
        set { _dorTemplatePath = value; OnChanged(); _settings.DorTemplatePath = value; TrySaveSettings(); }
    }

    private string _selectedIssueType = "Story";
    public string SelectedIssueType { get => _selectedIssueType; set { _selectedIssueType = value; OnChanged(); } }

    private string _description = "";
    public string Description { get => _description; set { _description = value; OnChanged(); } }

    private string _status = "Idle";
    public string Status { get => _status; set { _status = value; OnChanged(); } }

    private string _createdUrl = "";
    public string CreatedUrl { get => _createdUrl; set { _createdUrl = value; OnChanged(); } }

    public string TeamName => _settings.TeamName;
    public string ProjectKey => _settings.ProjectKey;

    public RelayCommand BrowseDorCommand { get; }
    public AsyncRelayCommand GenerateCommand { get; }
    public AsyncRelayCommand CreateCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public RelayCommand OpenTicketCommand { get; }

    private void BrowseDor()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select DoR template",
            Filter = "Markdown / text (*.md;*.txt;*.markdown)|*.md;*.txt;*.markdown|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() == true)
        {
            DorTemplatePath = dlg.FileName;
            Status = $"DoR template: {DorTemplatePath}";
        }
    }

    private async Task GenerateAsync()
    {
        try
        {
            Status = "Reading DoR template…";
            var dor = await File.ReadAllTextAsync(DorTemplatePath);

            Status = "Generating description via claude…";
            var prompt = BuildPrompt(Title, Brief, dor, SelectedIssueType);
            // Pass the prompt on stdin, not as a CLI arg, to avoid the Windows command-line
            // length limit ("filename or extension too long").
            var r = await ProcessRunner.RunAsync("claude", ["-p"], stdin: prompt);
            if (r.ExitCode != 0)
            {
                Status = $"claude failed: {r.StdErr.Trim()}";
                return;
            }
            Description = r.StdOut.Trim();
            Status = "Generated. Review/edit, then Create.";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task CreateAsync()
    {
        if (!_settings.HasCredentials)
        {
            MessageBox.Show("Jira credentials are not configured. Open Settings to add email + API token.",
                "Missing credentials", MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenSettings();
            if (!_settings.HasCredentials) return;
        }

        var confirm = MessageBox.Show(
            $"Create {SelectedIssueType} in {_settings.ProjectKey} (Team: {_settings.TeamName})?\n\nTitle: {Title}",
            "Confirm Create", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            Status = "Creating ticket…";
            var svc = new JiraService(_settings);
            var result = await svc.CreateIssueAsync(Title, Description, SelectedIssueType);
            CreatedUrl = result.Url;
            Status = $"Created {result.Key}: {result.Url}";
            Clipboard.SetText(result.Url);
        }
        catch (Exception ex)
        {
            Status = $"Create failed: {ex.Message}";
        }
    }

    private void OpenSettings()
    {
        var win = new Views.JiraSettingsWindow(_settings) { Owner = Application.Current.MainWindow };
        win.ShowDialog();
        OnChanged(nameof(TeamName));
        OnChanged(nameof(ProjectKey));
    }

    private void OpenTicket()
    {
        if (string.IsNullOrWhiteSpace(CreatedUrl)) return;
        Process.Start(new ProcessStartInfo(CreatedUrl) { UseShellExecute = true });
    }

    private void TrySaveSettings() { try { _settings.Save(); } catch { /* ignore */ } }

    private static string BuildPrompt(string title, string brief, string dor, string issueType)
    {
        return $@"You are drafting a Jira ticket description for a {issueType} ticket. Follow the supplied Definition of Ready (DoR) template — fill in each section that applies, and explicitly note 'N/A' for sections that don't.

The output MUST:
- Be valid markdown.
- Start with a one-paragraph summary (no leading heading).
- Include all DoR sections as in the template, in the same order.
- Include an 'Acceptance Criteria' bulleted list (concrete, testable).
- Omit the title (it is set separately as the issue summary).
- Output ONLY the markdown content — no preamble, no code-fence wrapping, no commentary.

# Ticket title
{title}

# User brief / context
{brief}

# Definition of Ready template (follow this structure)
{dor}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
