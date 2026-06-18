using System.Windows;
using PrReviewHelper.Services;

namespace PrReviewHelper.Views;

public partial class JiraSettingsWindow : Window
{
    private readonly JiraSettings _settings;
    public JiraSettingsWindow(JiraSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        HostBox.Text = settings.Host;
        EmailBox.Text = settings.Email;
        TokenBox.Password = settings.ApiToken;
        ProjectBox.Text = settings.ProjectKey;
        TeamFieldBox.Text = settings.TeamFieldId;
        TeamIdBox.Text = settings.TeamId;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.Host = HostBox.Text.Trim();
        _settings.Email = EmailBox.Text.Trim();
        _settings.ApiToken = TokenBox.Password;
        _settings.ProjectKey = ProjectBox.Text.Trim();
        _settings.TeamFieldId = TeamFieldBox.Text.Trim();
        _settings.TeamId = TeamIdBox.Text.Trim();
        _settings.Save();
        DialogResult = true;
        Close();
    }
}
