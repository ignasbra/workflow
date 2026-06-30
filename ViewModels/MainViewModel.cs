using System.ComponentModel;
using System.Runtime.CompilerServices;
using PrReviewHelper.Services;

namespace PrReviewHelper.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public CreateBranchViewModel CreateBranch { get; } = new();
    public PrCreateViewModel PrCreate { get; } = new();
    public PrViewModel Pr { get; } = new();
    public JiraViewModel Jira { get; } = new();

    private string _selectedAiTool;
    public string SelectedAiTool
    {
        get => _selectedAiTool;
        set
        {
            if (_selectedAiTool == value) return;
            _selectedAiTool = value;
            OnChanged();

            // Save settings
            var settings = PrReviewSettings.Load();
            settings.AiTool = value;
            settings.Save();
        }
    }

    public List<string> AiTools { get; } = new() { "Claude", "Antigravity CLI" };

    public MainViewModel()
    {
        var settings = PrReviewSettings.Load();
        _selectedAiTool = settings.AiTool;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
