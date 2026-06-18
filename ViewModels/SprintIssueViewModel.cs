using System.Windows.Media;
using PrReviewHelper.Services;

namespace PrReviewHelper.ViewModels;

public class SprintIssueViewModel
{
    public SprintIssueViewModel(JiraService.SprintIssue issue)
    {
        Issue = issue;
    }

    public JiraService.SprintIssue Issue { get; }

    public string Key => Issue.Key;
    public string Summary => Issue.Summary;
    public string AssigneeName => Issue.AssigneeName;
    public string StatusName => Issue.StatusName;
    public string IssueTypeName => Issue.IssueTypeName;

    /// <summary>Single uppercase letter for the type pill (B=Bug, S=Story, T=Task, …).</summary>
    public string TypeBadge => Issue.IssueTypeName.Length > 0 ? Issue.IssueTypeName[..1].ToUpperInvariant() : "?";

    public Brush TypeColor => Issue.IssueTypeName switch
    {
        "Bug" => new SolidColorBrush(Color.FromRgb(0xE5, 0x49, 0x33)),         // red
        "Story" => new SolidColorBrush(Color.FromRgb(0x65, 0xBA, 0x43)),       // green
        "Task" => new SolidColorBrush(Color.FromRgb(0x42, 0x88, 0xE7)),        // blue
        "Sub-task" => new SolidColorBrush(Color.FromRgb(0x42, 0x88, 0xE7)),    // blue
        "Spike" => new SolidColorBrush(Color.FromRgb(0xC0, 0x80, 0x36)),       // amber
        "Epic" => new SolidColorBrush(Color.FromRgb(0x76, 0x4A, 0xB5)),        // purple
        _ => new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)),             // muted
    };

    /// <summary>Jira status categories: new (grey), indeterminate (blue), done (green).</summary>
    public Brush StatusColor => Issue.StatusCategoryKey switch
    {
        "done" => new SolidColorBrush(Color.FromRgb(0x23, 0x86, 0x36)),
        "indeterminate" => new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB)),
        _ => new SolidColorBrush(Color.FromRgb(0x57, 0x60, 0x6B)),             // new / unknown
    };

    public string AssigneeInitials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Issue.AssigneeAccountId)) return "·";
            var parts = Issue.AssigneeName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "·",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => $"{parts[0][..1]}{parts[^1][..1]}".ToUpperInvariant(),
            };
        }
    }
}
