using System.Diagnostics;
using System.IO;
using System.Text;

namespace PrReviewHelper.Services;

public record ProcessResult(int ExitCode, string StdOut, string StdErr);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> args,
        string? workingDirectory = null,
        string? stdin = null,
        CancellationToken ct = default)
    {
        var resolvedFileName = ResolveExecutablePath(fileName);
        var psi = new ProcessStartInfo
        {
            FileName = resolvedFileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (stdin is not null) psi.StandardInputEncoding = Encoding.UTF8;
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdoutSb = new StringBuilder();
        var stderrSb = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdoutSb.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderrSb.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (stdin is not null)
        {
            await proc.StandardInput.WriteAsync(stdin);
            proc.StandardInput.Close();
        }

        await proc.WaitForExitAsync(ct);
        return new ProcessResult(proc.ExitCode, stdoutSb.ToString(), stderrSb.ToString());
    }

    private static string ResolveExecutablePath(string command)
    {
        // 1. Try to find it in the combined PATH environment variable (including User and Machine registry targets)
        var processPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
        
        var combinedPath = $"{processPath}{Path.PathSeparator}{userPath}{Path.PathSeparator}{machinePath}";
        
        if (!string.IsNullOrEmpty(combinedPath))
        {
            var paths = combinedPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            var extensions = new[] { "", ".exe", ".cmd", ".bat" };
            foreach (var path in paths)
            {
                foreach (var ext in extensions)
                {
                    try
                    {
                        var fullPath = Path.Combine(path.Trim(' ', '"'), command + ext);
                        if (File.Exists(fullPath))
                        {
                            return fullPath;
                        }
                    }
                    catch { /* ignore invalid paths in PATH env */ }
                }
            }
        }

        // 2. Try typical fallback locations if PATH checks didn't locate it
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (command == "agy")
        {
            var appDataAgy = Path.Combine(localAppData, @"agy\bin\agy.exe");
            if (File.Exists(appDataAgy)) return appDataAgy;

            var userProfileAgy = Path.Combine(userProfile, @"AppData\Local\agy\bin\agy.exe");
            if (File.Exists(userProfileAgy)) return userProfileAgy;
        }
        else if (command == "claude")
        {
            var userProfileClaudeExe = Path.Combine(userProfile, @".local\bin\claude.exe");
            if (File.Exists(userProfileClaudeExe)) return userProfileClaudeExe;

            var userProfileClaudeCmd = Path.Combine(userProfile, @".local\bin\claude.cmd");
            if (File.Exists(userProfileClaudeCmd)) return userProfileClaudeCmd;

            var userProfileClaudeBat = Path.Combine(userProfile, @".local\bin\claude.bat");
            if (File.Exists(userProfileClaudeBat)) return userProfileClaudeBat;
        }

        // Fall back to the raw command name and let Process.Start try its best
        return command;
    }
}
