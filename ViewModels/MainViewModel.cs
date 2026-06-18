namespace PrReviewHelper.ViewModels;

public class MainViewModel
{
    public CreateBranchViewModel CreateBranch { get; } = new();
    public PrCreateViewModel PrCreate { get; } = new();
    public PrViewModel Pr { get; } = new();
    public JiraViewModel Jira { get; } = new();
}
