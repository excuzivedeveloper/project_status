using ProjectStatus.Client;
using Xunit;

namespace ProjectStatus.Client.Tests;

// Pure logic behind the update flow: when to prompt, how a published checksum is read, and which
// download hosts are accepted. No window and no network are involved.
public class UpdatePromptPolicyTests
{
    [Theory]
    [InlineData("0.1.4", "", true)]
    [InlineData("0.1.4", null, true)]
    [InlineData("0.1.4", "0.1.4", false)]
    [InlineData("0.1.5", "0.1.4", true)]
    [InlineData("0.1.4", "0.1.5", false)]
    [InlineData("v0.1.4", "0.1.3", true)]
    [InlineData("1.1", "1.0.0", true)]
    [InlineData("1.0", "1.0.0", false)]
    [InlineData("0.1.4", "not-a-version", true)]
    [InlineData("", "0.1.4", false)]
    [InlineData(null, "0.1.4", false)]
    public void ShouldPrompt_announces_a_version_once(string? offered, string? lastPrompted, bool expected)
    {
        Assert.Equal(expected, UpdatePromptPolicy.ShouldPrompt(offered, lastPrompted));
    }

    [Fact]
    public void ShouldPrompt_keeps_quiet_after_the_user_was_told_once()
    {
        // installed 0.1.3, latest 0.1.4: shown once, then not again while 0.1.4 is still the latest.
        Assert.True(UpdatePromptPolicy.ShouldPrompt("0.1.4", string.Empty));
        Assert.False(UpdatePromptPolicy.ShouldPrompt("0.1.4", "0.1.4"));

        // 0.1.5 appears later, so the user hears about it.
        Assert.True(UpdatePromptPolicy.ShouldPrompt("0.1.5", "0.1.4"));
    }
}

public class UpdateChecksumTests
{
    // SHA-256 of an empty input, used as a known-good value.
    private const string KnownHash =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Fact]
    public void ParseHash_reads_the_release_format()
    {
        var content = $"{KnownHash}  ProjectStatus-Setup-v0.1.4.exe\n";

        Assert.Equal(KnownHash, UpdateChecksum.ParseHash(content, "ProjectStatus-Setup-v0.1.4.exe"));
    }

    [Fact]
    public void ParseHash_accepts_upper_case_hex()
    {
        var content = $"{KnownHash.ToUpperInvariant()}  ProjectStatus-Setup-v0.1.4.exe";

        Assert.Equal(KnownHash, UpdateChecksum.ParseHash(content));
    }

    [Fact]
    public void ParseHash_requires_the_file_name_when_the_caller_knows_which_file_it_downloaded()
    {
        const string installer = "ProjectStatus-Setup-v0.1.4.exe";

        // A bare hash, or one that names a different asset, is not accepted for a known download.
        Assert.Null(UpdateChecksum.ParseHash(KnownHash, installer));
        Assert.Null(UpdateChecksum.ParseHash($"{KnownHash}\n", installer));
        Assert.Null(UpdateChecksum.ParseHash($"{KnownHash}  ProjectStatus-Setup-v0.1.3.exe", installer));

        Assert.Equal(KnownHash, UpdateChecksum.ParseHash($"{KnownHash}  {installer}", installer));
        Assert.Equal(KnownHash, UpdateChecksum.ParseHash($"{KnownHash} {installer}", installer));
    }

    [Fact]
    public void ParseHash_ignores_a_checksum_for_another_file()
    {
        var content = $"{KnownHash}  ProjectStatus-Setup-v0.1.3.exe";

        Assert.Null(UpdateChecksum.ParseHash(content, "ProjectStatus-Setup-v0.1.4.exe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc123  ProjectStatus-Setup-v0.1.4.exe")]
    [InlineData("not a checksum at all")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85z  file.exe")]
    public void ParseHash_rejects_anything_that_is_not_a_64_character_hex_hash(string content)
    {
        Assert.Null(UpdateChecksum.ParseHash(content));
    }

    [Fact]
    public void ParseHash_returns_null_for_missing_content()
    {
        Assert.Null(UpdateChecksum.ParseHash(null));
    }

    [Fact]
    public void ComputeHash_matches_a_known_sha256()
    {
        Assert.Equal(KnownHash, UpdateChecksum.ComputeHash(Array.Empty<byte>()));
    }

    [Fact]
    public void Matches_accepts_only_equal_hashes()
    {
        Assert.True(UpdateChecksum.Matches(KnownHash, KnownHash));
        Assert.True(UpdateChecksum.Matches(KnownHash.ToUpperInvariant(), KnownHash));
        Assert.False(UpdateChecksum.Matches(KnownHash, "0".PadLeft(64, '0')));
        Assert.False(UpdateChecksum.Matches(null, KnownHash));
        Assert.False(UpdateChecksum.Matches("too-short", KnownHash));
    }

    [Fact]
    public void IsHexHash_requires_exactly_64_hex_characters()
    {
        Assert.True(UpdateChecksum.IsHexHash(KnownHash));
        Assert.False(UpdateChecksum.IsHexHash(KnownHash[..63]));
        Assert.False(UpdateChecksum.IsHexHash(KnownHash + "0"));
        Assert.False(UpdateChecksum.IsHexHash(string.Empty));
    }
}

public class UpdateDownloaderTrustTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo/releases/download/v0.1.4/ProjectStatus-Setup-v0.1.4.exe", true)]
    [InlineData("HTTPS://GITHUB.COM/owner/repo/file.exe", true)]
    [InlineData("http://github.com/owner/repo/file.exe", false)]
    [InlineData("https://evil.example.com/ProjectStatus-Setup-v0.1.4.exe", false)]
    [InlineData("https://github.com.evil.example/ProjectStatus-Setup-v0.1.4.exe", false)]
    [InlineData("https://raw.githubusercontent.com/owner/repo/file.exe", false)]
    [InlineData("ftp://github.com/file.exe", false)]
    [InlineData("not a uri", false)]
    [InlineData("", false)]
    public void IsTrusted_accepts_only_https_on_the_release_host(string uri, bool expected)
    {
        Assert.Equal(expected, UpdateDownloader.IsTrusted(uri));
    }

    [Fact]
    public void IsTrusted_rejects_a_missing_uri()
    {
        Assert.False(UpdateDownloader.IsTrusted((Uri?)null));
    }
}

public class UpdateInfoTests
{
    [Fact]
    public void CanUpdateInPlace_needs_both_the_installer_and_its_checksum()
    {
        var installer = new Uri("https://github.com/owner/repo/releases/download/v0.1.4/ProjectStatus-Setup-v0.1.4.exe");
        var checksum = new Uri("https://github.com/owner/repo/releases/download/v0.1.4/ProjectStatus-Setup-v0.1.4.exe.sha256");
        var release = new Uri("https://github.com/owner/repo/releases/tag/v0.1.4");

        Assert.True(new UpdateInfo("0.1.4", release, installer, checksum).CanUpdateInPlace);
        Assert.False(new UpdateInfo("0.1.4", release, installer, null).CanUpdateInPlace);
        Assert.False(new UpdateInfo("0.1.4", release, null, checksum).CanUpdateInPlace);
        Assert.False(new UpdateInfo("0.1.4", release, null, null).CanUpdateInPlace);
    }

    [Fact]
    public void DownloadUri_prefers_the_installer_and_falls_back_to_the_release_page()
    {
        var installer = new Uri("https://github.com/owner/repo/releases/download/v0.1.4/ProjectStatus-Setup-v0.1.4.exe");
        var release = new Uri("https://github.com/owner/repo/releases/tag/v0.1.4");

        Assert.Equal(installer, new UpdateInfo("0.1.4", release, installer, null).DownloadUri);
        Assert.Equal(release, new UpdateInfo("0.1.4", release, null, null).DownloadUri);
    }

    [Fact]
    public void InstallerFileName_matches_the_published_asset_name()
    {
        Assert.Equal(
            "ProjectStatus-Setup-v0.1.4.exe",
            new UpdateInfo("0.1.4", null, null, null).InstallerFileName);
    }
}
