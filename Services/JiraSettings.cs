using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrReviewHelper.Services;

public class JiraSettings
{
    public string Host { get; set; } = "https://pvcase.atlassian.net";
    public string ProjectKey { get; set; } = "CLOUD";
    public string TeamFieldId { get; set; } = "customfield_10001";
    public string TeamId { get; set; } = "c4e1e76b-04bc-488f-a6e5-aa60998b7251";
    public string TeamName { get; set; } = "CloudMount";

    public string Email { get; set; } = "";
    public string ApiToken { get; set; } = "";

    public string DorTemplatePath { get; set; } = "";

    [JsonIgnore]
    public bool HasCredentials => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(ApiToken);

    private static string FilePath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pr-review-helper");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "jira.json");
        }
    }

    public static JiraSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<JiraSettings>(json);
                if (loaded is not null) return loaded;
            }
        }
        catch { /* fall through to defaults */ }
        return new JiraSettings();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
