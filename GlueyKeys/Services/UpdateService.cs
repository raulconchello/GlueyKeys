using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace GlueyKeys.Services;

public class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/raulconchello/GlueyKeys/releases/latest";
    private const string DownloadAssetName = "GlueyKeys.exe";
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromDays(1);

    private readonly HttpClient _httpClient = new();

    public UpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GlueyKeys");
    }

    public string CurrentVersion { get; } = GetCurrentVersion();

    public bool ShouldCheckForUpdates(DateTime? lastUpdateCheckUtc)
    {
        return lastUpdateCheckUtc == null ||
               DateTime.UtcNow - lastUpdateCheckUtc.Value.ToUniversalTime() >= UpdateCheckInterval;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var latestVersion = NormalizeVersion(tagName);
            var releasePageUrl = root.TryGetProperty("html_url", out var htmlUrlElement)
                ? htmlUrlElement.GetString()
                : null;

            if (!TryParseVersion(CurrentVersion, out var current) ||
                !TryParseVersion(latestVersion, out var latest))
            {
                return UpdateCheckResult.Failed("Could not compare app versions.");
            }

            if (latest <= current)
            {
                return UpdateCheckResult.NoUpdate(latestVersion);
            }

            var downloadUrl = FindDownloadUrl(root);
            if (string.IsNullOrEmpty(downloadUrl))
            {
                return UpdateCheckResult.Failed($"The latest release does not include {DownloadAssetName}.");
            }

            return UpdateCheckResult.UpdateAvailable(latestVersion, downloadUrl, releasePageUrl);
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed(ex.Message);
        }
    }

    public async Task<string> DownloadUpdateAsync(UpdateCheckResult update, CancellationToken cancellationToken = default)
    {
        if (!update.IsUpdateAvailable || string.IsNullOrEmpty(update.DownloadUrl))
            throw new InvalidOperationException("No update download is available.");

        var updateDirectory = Path.Combine(Path.GetTempPath(), "GlueyKeys", "Update", update.LatestVersion ?? "latest");

        if (Directory.Exists(updateDirectory))
        {
            Directory.Delete(updateDirectory, true);
        }

        Directory.CreateDirectory(updateDirectory);

        var downloadPath = Path.Combine(updateDirectory, DownloadAssetName);

        using var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(downloadPath);
        await source.CopyToAsync(destination, cancellationToken);

        if (new FileInfo(downloadPath).Length < 10_000_000)
            throw new InvalidOperationException("Downloaded update is unexpectedly small.");

        return downloadPath;
    }

    public void LaunchUpdateInstaller(string downloadedExePath, string targetExePath, string version)
    {
        var scriptPath = Path.Combine(
            Path.GetDirectoryName(downloadedExePath) ?? Path.GetTempPath(),
            "apply-update.ps1");

        var processId = Environment.ProcessId;
        var script = $$"""
$ErrorActionPreference = 'Stop'
$processId = {{processId}}
$source = @'
{{downloadedExePath}}
'@
$target = @'
{{targetExePath}}
'@
$version = @'
{{version}}
'@

try {
    Wait-Process -Id $processId -Timeout 60 -ErrorAction SilentlyContinue
} catch {
}

Start-Sleep -Milliseconds 500
Copy-Item -LiteralPath $source -Destination $target -Force
Start-Process -FilePath $target -ArgumentList @('--updated', $version)
Start-Sleep -Seconds 2
Remove-Item -LiteralPath $source -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
""";

        File.WriteAllText(scriptPath, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static string FindDownloadUrl(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return string.Empty;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (!string.Equals(name, DownloadAssetName, StringComparison.OrdinalIgnoreCase))
                continue;

            return asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString() ?? string.Empty
                : string.Empty;
        }

        return string.Empty;
    }

    private static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string NormalizeVersion(string version)
    {
        return version.Trim().TrimStart('v', 'V');
    }

    private static bool TryParseVersion(string version, out Version parsed)
    {
        return Version.TryParse(NormalizeVersion(version), out parsed!);
    }
}

public class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; init; }
    public string? LatestVersion { get; init; }
    public string? DownloadUrl { get; init; }
    public string? ReleasePageUrl { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateCheckResult UpdateAvailable(string latestVersion, string downloadUrl, string? releasePageUrl)
    {
        return new UpdateCheckResult
        {
            IsUpdateAvailable = true,
            LatestVersion = latestVersion,
            DownloadUrl = downloadUrl,
            ReleasePageUrl = releasePageUrl
        };
    }

    public static UpdateCheckResult NoUpdate(string latestVersion)
    {
        return new UpdateCheckResult
        {
            LatestVersion = latestVersion
        };
    }

    public static UpdateCheckResult Failed(string errorMessage)
    {
        return new UpdateCheckResult
        {
            ErrorMessage = errorMessage
        };
    }
}
