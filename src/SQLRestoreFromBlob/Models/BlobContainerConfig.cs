using System.Web;

namespace SQLRestoreFromBlob.Models;

public class BlobContainerConfig
{
    public string Name { get; set; } = string.Empty;
    public string ContainerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Key used to look up SAS token in Windows Credential Manager.
    /// </summary>
    public string CredentialKey => $"SQLRestoreFromBlob:Blob:{Name}";

    public bool IsExpired => GetSasExpiry() is DateTime expiry && expiry < DateTime.UtcNow;

    public DateTime? SasExpiry => GetSasExpiry();

    public string? StorageAccountName
    {
        get
        {
            if (!Uri.TryCreate(ContainerUrl, UriKind.Absolute, out var uri))
                return null;
            var host = uri.Host;
            var dotIndex = host.IndexOf('.');
            return dotIndex > 0 ? host[..dotIndex] : host;
        }
    }

    public string? ContainerName
    {
        get
        {
            if (!Uri.TryCreate(ContainerUrl, UriKind.Absolute, out var uri))
                return null;
            return uri.AbsolutePath.Trim('/');
        }
    }

    public string DisplayText
    {
        get
        {
            var status = IsExpired ? " [EXPIRED]" : "";
            return $"{Name}{status}";
        }
    }

    private string? _cachedSasTokenValue;

    public DateTime? GetSasExpiry(string? sasToken = null)
    {
        var token = sasToken ?? _cachedSasTokenValue;
        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            var query = token.StartsWith("?") ? token : "?" + token;
            var parsed = HttpUtility.ParseQueryString(query);
            var se = parsed["se"];
            if (se != null && DateTime.TryParse(se, out var expiry))
                return expiry.ToUniversalTime();
        }
        catch
        {
            // Malformed token
        }

        return null;
    }

    public void CacheSasToken(string sasToken)
    {
        _cachedSasTokenValue = sasToken;
    }
}
