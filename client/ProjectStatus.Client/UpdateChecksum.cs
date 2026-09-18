using System.Security.Cryptography;

namespace ProjectStatus.Client;

// Release checksums are published next to the installer as
//     <64 hex chars>  ProjectStatus-Setup-vX.Y.Z.exe
// Parsing and comparison live here, free of UI and network code, so both can be checked directly.
internal static class UpdateChecksum
{
    private const int HashLength = 64;

    public static string? ParseHash(string? content, string? expectedFileName = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var hash = parts[0].ToLowerInvariant();
            if (!IsHexHash(hash))
            {
                continue;
            }

            // The release format names the file the hash belongs to; when the caller knows which file
            // it downloaded, a checksum for a different asset is not accepted.
            if (expectedFileName is not null &&
                parts.Length > 1 &&
                !string.Equals(parts[1], expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return hash;
        }

        return null;
    }

    public static bool IsHexHash(string? value)
    {
        return !string.IsNullOrEmpty(value) &&
               value.Length == HashLength &&
               value.All(Uri.IsHexDigit);
    }

    public static string ComputeHash(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    public static bool Matches(string? expectedHash, string? actualHash)
    {
        return IsHexHash(expectedHash) &&
               string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase);
    }
}
