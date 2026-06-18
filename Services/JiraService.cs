using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PrReviewHelper.Services;

public class JiraService(JiraSettings settings)
{
    private readonly HttpClient _http = CreateClient(settings);

    private static HttpClient CreateClient(JiraSettings s)
    {
        var client = new HttpClient();
        if (s.HasCredentials)
        {
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{s.Email}:{s.ApiToken}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        }
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public record CreateResult(string Key, string Url);

    public record IssueSummary(string Key, string Summary, string IssueTypeName);

    public record SprintIssue(
        string Key,
        string Summary,
        string IssueTypeName,
        string StatusName,
        string StatusCategoryKey,
        string AssigneeName,
        string AssigneeAccountId);

    public async Task<List<SprintIssue>> GetActiveSprintIssuesAsync(string projectKey, CancellationToken ct = default)
    {
        var jql = $"project = {projectKey} AND sprint in openSprints() ORDER BY rank";
        var url = $"{settings.Host.TrimEnd('/')}/rest/api/3/search/jql"
                  + $"?jql={Uri.EscapeDataString(jql)}"
                  + $"&fields=summary,status,issuetype,assignee"
                  + $"&maxResults=200";
        using var resp = await _http.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Jira search failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");

        using var doc = JsonDocument.Parse(body);
        var list = new List<SprintIssue>();
        if (!doc.RootElement.TryGetProperty("issues", out var issuesEl)) return list;

        foreach (var issue in issuesEl.EnumerateArray())
        {
            var key = issue.GetProperty("key").GetString() ?? "";
            var fields = issue.GetProperty("fields");
            var summary = fields.GetProperty("summary").GetString() ?? "";
            var typeName = fields.GetProperty("issuetype").GetProperty("name").GetString() ?? "";
            var status = fields.GetProperty("status");
            var statusName = status.GetProperty("name").GetString() ?? "";
            var statusCategory = status.GetProperty("statusCategory").GetProperty("key").GetString() ?? "";

            var assigneeName = "Unassigned";
            var assigneeId = "";
            if (fields.TryGetProperty("assignee", out var asg) && asg.ValueKind == JsonValueKind.Object)
            {
                assigneeName = asg.GetProperty("displayName").GetString() ?? "Unassigned";
                assigneeId = asg.GetProperty("accountId").GetString() ?? "";
            }
            list.Add(new(key, summary, typeName, statusName, statusCategory, assigneeName, assigneeId));
        }
        return list;
    }

    public async Task<IssueSummary> GetIssueAsync(string key, CancellationToken ct = default)
    {
        var url = $"{settings.Host.TrimEnd('/')}/rest/api/3/issue/{Uri.EscapeDataString(key)}?fields=summary,issuetype";
        using var resp = await _http.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Jira get failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");

        using var doc = JsonDocument.Parse(body);
        var fields = doc.RootElement.GetProperty("fields");
        var summary = fields.GetProperty("summary").GetString() ?? "";
        var issueType = fields.GetProperty("issuetype").GetProperty("name").GetString() ?? "";
        var resolvedKey = doc.RootElement.GetProperty("key").GetString() ?? key;
        return new IssueSummary(resolvedKey, summary, issueType);
    }

    public async Task<CreateResult> CreateIssueAsync(
        string summary, string description, string issueTypeName,
        CancellationToken ct = default)
    {
        var body = new
        {
            fields = new Dictionary<string, object>
            {
                ["project"] = new { key = settings.ProjectKey },
                ["summary"] = summary,
                ["description"] = ToAdf(description),
                ["issuetype"] = new { name = issueTypeName },
                [settings.TeamFieldId] = settings.TeamId,
            }
        };
        var json = JsonSerializer.Serialize(body);
        var req = new HttpRequestMessage(HttpMethod.Post, $"{settings.Host.TrimEnd('/')}/rest/api/3/issue")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var resp = await _http.SendAsync(req, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Jira create failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{respBody}");

        using var doc = JsonDocument.Parse(respBody);
        var key = doc.RootElement.GetProperty("key").GetString()!;
        return new CreateResult(key, $"{settings.Host.TrimEnd('/')}/browse/{key}");
    }

    // ADF (Atlassian Document Format) for /rest/api/3/issue. Treat the markdown-ish
    // input as a single code-block-free body: split on blank lines into paragraphs,
    // and detect bullet lists / headings line-by-line.
    private static object ToAdf(string markdown)
    {
        var content = new List<object>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var paragraphBuffer = new List<string>();
        var bulletBuffer = new List<string>();

        void FlushParagraph()
        {
            if (paragraphBuffer.Count == 0) return;
            var text = string.Join(" ", paragraphBuffer).Trim();
            if (text.Length > 0)
            {
                content.Add(new
                {
                    type = "paragraph",
                    content = new[] { new { type = "text", text } }
                });
            }
            paragraphBuffer.Clear();
        }

        void FlushBullets()
        {
            if (bulletBuffer.Count == 0) return;
            var items = bulletBuffer.Select(b => new
            {
                type = "listItem",
                content = new[]
                {
                    new {
                        type = "paragraph",
                        content = new[] { new { type = "text", text = b } }
                    }
                }
            }).ToArray();
            content.Add(new { type = "bulletList", content = items });
            bulletBuffer.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                FlushBullets();
                continue;
            }

            // Heading: # / ## / ### prefix
            var headingMatch = System.Text.RegularExpressions.Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (headingMatch.Success)
            {
                FlushParagraph();
                FlushBullets();
                var level = headingMatch.Groups[1].Value.Length;
                var text = headingMatch.Groups[2].Value;
                content.Add(new
                {
                    type = "heading",
                    attrs = new { level },
                    content = new[] { new { type = "text", text } }
                });
                continue;
            }

            // Bullet list
            var bulletMatch = System.Text.RegularExpressions.Regex.Match(line, @"^[\*\-]\s+(.*)$");
            if (bulletMatch.Success)
            {
                FlushParagraph();
                bulletBuffer.Add(bulletMatch.Groups[1].Value);
                continue;
            }

            // Horizontal rule
            if (line == "---" || line == "***")
            {
                FlushParagraph();
                FlushBullets();
                content.Add(new { type = "rule" });
                continue;
            }

            FlushBullets();
            paragraphBuffer.Add(line);
        }
        FlushParagraph();
        FlushBullets();

        return new { type = "doc", version = 1, content = content.ToArray() };
    }
}
