using System.Text.Json;

namespace ProjectStatus.Client;

internal sealed record UpdateInfo(string Version, Uri DownloadUri);

internal static class UpdateChecker
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/excuzivedeveloper/project_status/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagElement))
            {
                return null;
            }

            var tag = tagElement.GetString();
            var latestVersion = ParseVersion(tag);
            var currentVersion = NormalizeVersion(typeof(UpdateChecker).Assembly.GetName().Version);
            if (latestVersion is null || currentVersion is null || latestVersion <= currentVersion)
            {
                return null;
            }

            var downloadUri = FindInstallerDownload(root) ?? FindReleasePage(root);
            return downloadUri is null
                ? null
                : new UpdateInfo(latestVersion.ToString(3), downloadUri);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(4)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ProjectStatus/0.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var separator = value.IndexOfAny(['-', '+']);
        if (separator >= 0)
        {
            value = value[..separator];
        }

        return Version.TryParse(value, out var parsed) ? NormalizeVersion(parsed) : null;
    }

    private static Version? NormalizeVersion(Version? version)
    {
        if (version is null)
        {
            return null;
        }

        return new Version(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0));
    }

    private static Uri? FindInstallerDownload(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(name) ||
                !name.StartsWith("ProjectStatus-Setup-", StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (asset.TryGetProperty("browser_download_url", out var urlElement) &&
                TryCreateTrustedGitHubUri(urlElement.GetString(), out var uri))
            {
                return uri;
            }
        }

        return null;
    }

    private static Uri? FindReleasePage(JsonElement root)
    {
        return root.TryGetProperty("html_url", out var urlElement) &&
               TryCreateTrustedGitHubUri(urlElement.GetString(), out var uri)
            ? uri
            : null;
    }

    private static bool TryCreateTrustedGitHubUri(string? value, out Uri? uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            candidate.Scheme == Uri.UriSchemeHttps &&
            string.Equals(candidate.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            uri = candidate;
            return true;
        }

        uri = null;
        return false;
    }
}
