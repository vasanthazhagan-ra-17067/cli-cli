using CliqCli.Core;
using CliqCli.Core.Accounts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CliqCli.Tests;

public sealed class AccountStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AccountStore _store;

    public AccountStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cliq-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new AccountStore(_tempDir, NullLogger<AccountStore>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsEmptyRoot()
    {
        var root = await _store.LoadAsync();

        Assert.NotNull(root);
        Assert.Empty(root.Accounts);
    }

    [Fact]
    public async Task RoundTrip_SaveAndLoad_FieldsMatchExactly()
    {
        var entry = new AccountEntry
        {
            Name = "work",
            Domain = "zoho.com",
            Email = "user@example.com",
            Scopes = ["ZohoCliq.Channels.READ"],
            TokenType = "pat",
            IsDefault = true,
            NeedsReauth = false
        };
        var root = new AccountsRoot { Accounts = [entry] };

        await _store.SaveAsync(root);
        var loaded = await _store.LoadAsync();

        Assert.Single(loaded.Accounts);
        var loaded1 = loaded.Accounts[0];
        Assert.Equal("work", loaded1.Name);
        Assert.Equal("zoho.com", loaded1.Domain);
        Assert.Equal("user@example.com", loaded1.Email);
        Assert.Equal("ZohoCliq.Channels.READ", loaded1.Scopes[0]);
        Assert.Equal("pat", loaded1.TokenType);
        Assert.True(loaded1.IsDefault);
        Assert.False(loaded1.NeedsReauth);
    }

    [Fact]
    public async Task AccountsJson_DoesNotContainToken()
    {
        var entry = new AccountEntry
        {
            Name = "notoken",
            Domain = "zoho.com",
            TokenType = "pat",
        };
        await _store.SaveAsync(new AccountsRoot { Accounts = [entry] });

        var accountsPath = Path.Combine(_tempDir, "accounts.json");
        var json = await File.ReadAllTextAsync(accountsPath);

        // "token_type" is expected; a standalone "token" property key would indicate a leak
        Assert.DoesNotContain("\"token\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"secret\":", json, StringComparison.OrdinalIgnoreCase);
        // token_type IS expected to appear
        Assert.Contains("\"token_type\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindAsync_ExistingAccount_ReturnsEntry()
    {
        var root = new AccountsRoot
        {
            Accounts =
            [
                new AccountEntry { Name = "alpha", Domain = "zoho.com", TokenType = "pat" },
                new AccountEntry { Name = "beta",  Domain = "zoho.eu",  TokenType = "pat" }
            ]
        };
        await _store.SaveAsync(root);

        var found = await _store.FindAsync("beta");

        Assert.NotNull(found);
        Assert.Equal("beta", found.Name);
    }

    [Fact]
    public async Task FindAsync_MissingAccount_ReturnsNull()
    {
        await _store.SaveAsync(new AccountsRoot());

        var found = await _store.FindAsync("ghost");

        Assert.Null(found);
    }

    [Fact]
    public async Task GetDefaultAsync_NoAccounts_ThrowsNoDefaultAccount()
    {
        await _store.SaveAsync(new AccountsRoot());

        var ex = await Assert.ThrowsAsync<CliqCliException>(() => _store.GetDefaultAsync());
        Assert.Equal(ErrorCodes.NoDefaultAccount, ex.Code);
    }

    [Fact]
    public async Task GetDefaultAsync_ReturnsIsDefaultAccount()
    {
        var root = new AccountsRoot
        {
            Accounts =
            [
                new AccountEntry { Name = "a", Domain = "zoho.com", TokenType = "pat", IsDefault = false },
                new AccountEntry { Name = "b", Domain = "zoho.com", TokenType = "pat", IsDefault = true  }
            ]
        };
        await _store.SaveAsync(root);

        var def = await _store.GetDefaultAsync();

        Assert.Equal("b", def.Name);
    }

    [Fact]
    public async Task SaveAsync_AccountsJson_WritesSnakeCaseKeys()
    {
        var root = new AccountsRoot
        {
            Accounts =
            [
                new AccountEntry
                {
                    Name = "snake",
                    Domain = "zoho.com",
                    TokenType = "pat",
                    IsDefault = true,
                    NeedsReauth = false
                }
            ]
        };
        await _store.SaveAsync(root);

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, "accounts.json"));

        Assert.Contains("\"is_default\"", json);
        Assert.Contains("\"needs_reauth\"", json);
        Assert.Contains("\"token_type\"", json);
    }
}
