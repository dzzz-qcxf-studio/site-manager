using System.Text.RegularExpressions;

namespace SiteManager.Core.Configuration;

public sealed record ServerProfile(
    string Host,
    int SshPort,
    string Username,
    string PrivateKeyPath,
    string HostKeySha256,
    string PublicBaseUrl,
    int TrashRetentionDays = 30)
{
    private static readonly Regex UsernamePattern = new(
        "^[a-z_][a-z0-9_-]{0,31}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex HostKeyFingerprintPattern = new(
        "^SHA256:[A-Za-z0-9+/]{43}$",
        RegexOptions.CultureInvariant);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Host.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Server host must not be empty or contain whitespace.", nameof(Host));
        }

        if (SshPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(SshPort), "SSH port must be between 1 and 65535.");
        }

        if (!UsernamePattern.IsMatch(Username))
        {
            throw new ArgumentException("Server username is invalid.", nameof(Username));
        }

        if (string.IsNullOrWhiteSpace(PrivateKeyPath))
        {
            throw new ArgumentException("Private key path must not be empty.", nameof(PrivateKeyPath));
        }

        if (!HostKeyFingerprintPattern.IsMatch(HostKeySha256))
        {
            throw new ArgumentException("SSH host key fingerprint must be an OpenSSH SHA256 fingerprint.", nameof(HostKeySha256));
        }

        if (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var publicBaseUrl) ||
            publicBaseUrl.Scheme is not ("http" or "https") ||
            !string.Equals(publicBaseUrl.AbsolutePath, "/s/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(publicBaseUrl.Query) ||
            !string.IsNullOrEmpty(publicBaseUrl.Fragment))
        {
            throw new ArgumentException("Public base URL must be an HTTP(S) URL ending in /s/.", nameof(PublicBaseUrl));
        }

        if (TrashRetentionDays is < 1 or > 365)
        {
            throw new ArgumentOutOfRangeException(nameof(TrashRetentionDays), "Trash retention must be between 1 and 365 days.");
        }
    }
}
