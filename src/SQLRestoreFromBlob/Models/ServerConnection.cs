namespace SQLRestoreFromBlob.Models;

public enum AuthMode
{
    WindowsAuth,
    SqlAuth
}

public class ServerConnection
{
    public string Name { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public AuthMode AuthMode { get; set; } = AuthMode.WindowsAuth;
    public string? Username { get; set; }
    public int ConnectionTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Key used to look up password in Windows Credential Manager.
    /// Only used when AuthMode is SqlAuth.
    /// </summary>
    public string CredentialKey => $"SQLRestoreFromBlob:SQL:{Name}";

    public string DisplayText => AuthMode == AuthMode.WindowsAuth
        ? $"{ServerName} (Windows Auth)"
        : $"{ServerName} ({Username})";
}
