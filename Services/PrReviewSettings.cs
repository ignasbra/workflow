using System.IO;
using System.Text.Json;

namespace PrReviewHelper.Services;

public class PrReviewSettings
{
    public string BackendRepoPath { get; set; } = @"C:\git\github\product-predesign-backend";
    public string FrontendRepoPath { get; set; } = @"C:\git\github\site-feasibility-fe";

    private static string FilePath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pr-review-helper");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "pr-review.json");
        }
    }

    public static PrReviewSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<PrReviewSettings>(json);
                if (loaded is not null) return loaded;
            }
        }
        catch { /* fall through */ }
        return new PrReviewSettings();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}