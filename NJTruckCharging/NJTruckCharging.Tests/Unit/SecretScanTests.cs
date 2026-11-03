using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NJTruckCharging.Tests.Unit;

/// <summary>
/// Guards against committing credentials. Scans every file git would commit (tracked + untracked-but-not-ignored)
/// for Google API keys, and checks that committed appsettings files carry no Google Maps key. Real keys belong in
/// <c>dotnet user-secrets</c> (local) or environment variables (deployed) — see docs/config/*.example.
/// </summary>
public partial class SecretScanTests
{
    [GeneratedRegex(@"AIza[0-9A-Za-z_\-]{35}")]
    private static partial Regex GoogleApiKey();

    private static readonly HashSet<string> BinaryExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".ico", ".pdf", ".xlsx", ".bak", ".woff", ".woff2", ".ttf", ".dll", ".exe"];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Not inside a git repository.");
    }

    private static List<string> CommittableFiles(string root)
    {
        var psi = new ProcessStartInfo("git", "ls-files --cached --others --exclude-standard -z")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    [Fact]
    public void No_google_api_keys_in_committable_files()
    {
        var root = RepoRoot();
        var files = CommittableFiles(root);
        Assert.NotEmpty(files);

        var offenders = files
            .Where(f => !BinaryExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Where(f => File.Exists(Path.Combine(root, f)))
            .Where(f => GoogleApiKey().IsMatch(File.ReadAllText(Path.Combine(root, f))))
            .ToList();

        // Only file names are reported — never the matched value.
        Assert.True(offenders.Count == 0, "Google API key found in: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Committed_appsettings_have_no_google_maps_key()
    {
        var root = RepoRoot();
        var settings = CommittableFiles(root).Where(f => Path.GetFileName(f).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                                                         && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(settings);
        foreach (var f in settings)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, f)),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.TryGetProperty("GoogleMaps", out var gm) && gm.TryGetProperty("ApiKey", out var key))
                Assert.True(string.IsNullOrEmpty(key.GetString()), $"{f}: GoogleMaps:ApiKey must be empty in source control.");
        }
    }

    [Fact]
    public void Local_secret_files_are_git_ignored()
    {
        var root = RepoRoot();
        foreach (var path in new[] { "secrets.json", ".env", "NJTruckCharging/NJTruckCharging/appsettings.Local.json", "backups/x.bak" })
        {
            var psi = new ProcessStartInfo("git", $"check-ignore -q \"{path}\"") { WorkingDirectory = root, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, $"{path} should be git-ignored.");
        }
    }
}
