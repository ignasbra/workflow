using System.IO;
using System.Text.Json;

namespace PrReviewHelper.Services;

public class PrCreateSettings
{
    public string TemplatePath { get; set; } = "";
    public string BaseBranch { get; set; } = "main";

    private static string FilePath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pr-review-helper");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "pr-create.json");
        }
    }

    public static PrCreateSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<PrCreateSettings>(json);
                if (loaded is not null) return loaded;
            }
        }
        catch { /* fall through */ }
        return new PrCreateSettings();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
