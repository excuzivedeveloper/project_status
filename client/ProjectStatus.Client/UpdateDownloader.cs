using System.Security.Cryptography;

namespace ProjectStatus.Client;

// Downloads release assets to a temporary directory and hashes what actually landed on disk.
// Only HTTPS URLs on github.com are accepted, so a redirect or a tampered feed cannot point the
// updater at another host.
internal static class UpdateDownloader
{
    private const string TrustedHost = "github.com";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public static bool IsTrusted(Uri? uri)
    {
        return uri is not null &&
               uri.Scheme == Uri.UriSchemeHttps &&
               string.Equals(uri.Host, TrustedHost, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTrusted(string? uri)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && IsTrusted(parsed);
    }

    public static async Task<string> DownloadAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (!IsTrusted(uri))
        {
            throw new InvalidOperationException("The update file must be downloaded from the trusted release host.");
        }

        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("The update download has no file name.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "ProjectStatus", "update");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);

        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var target = File.Create(path))
        {
            await response.Content.CopyToAsync(target, cancellationToken);
        }

        return path;
    }

    public static async Task<string> ComputeFileHashAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<string> DownloadTextAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (!IsTrusted(uri))
        {
            throw new InvalidOperationException("The checksum must be downloaded from the trusted release host.");
        }

        using var response = await Http.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
