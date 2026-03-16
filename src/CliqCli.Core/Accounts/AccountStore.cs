using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CliqCli.Core.Accounts;

/// <summary>
/// Reads and writes <c>accounts.json</c> inside the platform config directory.
/// On macOS/Linux the file is written with 0600 permissions.
/// On Windows the DACL is set to restrict access to the current user only.
/// </summary>
public sealed class AccountStore : IAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private readonly string _accountsPath;
    private readonly ILogger<AccountStore> _logger;

    /// <summary>Production constructor — config directory is resolved from the current platform.</summary>
    public AccountStore(ILogger<AccountStore> logger)
        : this(GetPlatformConfigDir(), logger) { }

    /// <summary>Testable constructor — accepts an explicit config directory.</summary>
    internal AccountStore(string configDir, ILogger<AccountStore> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(configDir);
        _accountsPath = Path.Combine(configDir, "accounts.json");
    }

    // ─── IAccountStore ────────────────────────────────────────────────────────

    public async Task<AccountsRoot> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_accountsPath))
            return new AccountsRoot();

        var json = await File.ReadAllTextAsync(_accountsPath, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<AccountsRoot>(json, JsonOptions) ?? new AccountsRoot();
    }

    public async Task SaveAsync(AccountsRoot root, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(root, JsonOptions);
        await File.WriteAllTextAsync(_accountsPath, json, ct).ConfigureAwait(false);
        ApplyPermissions(_accountsPath);
    }

    public async Task<AccountEntry?> FindAsync(string name, CancellationToken ct = default)
    {
        var root = await LoadAsync(ct).ConfigureAwait(false);
        return root.Accounts.FirstOrDefault(a =>
            a.Name.Equals(name, StringComparison.Ordinal));
    }

    public async Task<AccountEntry> GetDefaultAsync(CancellationToken ct = default)
    {
        var root = await LoadAsync(ct).ConfigureAwait(false);

        var account = root.Accounts.FirstOrDefault(a => a.IsDefault)
                   ?? root.Accounts.FirstOrDefault();

        if (account is null)
            throw new CliqCliException(
                "No default account configured. Run 'cliq-cli account add' first.",
                ErrorCodes.NoDefaultAccount);

        return account;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string GetPlatformConfigDir()
    {
        var folder = OperatingSystem.IsWindows()
            ? Environment.SpecialFolder.LocalApplicationData
            : Environment.SpecialFolder.ApplicationData;

        return Path.Combine(Environment.GetFolderPath(folder), "cliq-cli");
    }

    private void ApplyPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            ApplyWindowsAcl(path);
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [SupportedOSPlatform("windows")]
    private void ApplyWindowsAcl(string path)
    {
        try
        {
            var fileSecurity = new FileSecurity(
                path,
                AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group);

            // Remove inherited ACEs and grant FullControl to the current user only
            fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is not null)
            {
                var rule = new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow);
                fileSecurity.SetAccessRule(rule);
            }

            new FileInfo(path).SetAccessControl(fileSecurity);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to restrict NTFS ACL on accounts.json. " +
                "The file may be accessible to other users on this machine.");
        }
    }
}
