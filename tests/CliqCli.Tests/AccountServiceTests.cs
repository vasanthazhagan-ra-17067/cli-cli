using CliqCli.Core;
using CliqCli.Core.Accounts;
using CliqCli.Core.Auth;
using CliqCli.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CliqCli.Tests;

/// <summary>
/// Unit tests for <see cref="AccountService"/>.
/// All I/O is in-memory: InMemoryKeychainProvider, AccountStore with a temp dir,
/// and FakeUserInfoService — no network, no real keychain, no disk side-effects outside the temp dir.
/// </summary>
public sealed class AccountServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AccountStore _store;
    private readonly InMemoryKeychainProvider _keychain;
    private readonly PatAuthProvider _auth;

    public AccountServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cliq-cli-svc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new AccountStore(_tempDir, NullLogger<AccountStore>.Instance);
        _keychain = new InMemoryKeychainProvider();
        _auth = new PatAuthProvider(_keychain);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private AccountService BuildService(IUserInfoService? userInfo = null) =>
        new(_store, _auth, userInfo ?? new FakeUserInfoService());

    // ─── AddAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_NewAccount_ReturnsNameAndDomain()
    {
        var svc = BuildService();

        var result = await svc.AddAsync("work", "tok-abc", "zoho.com");

        Assert.Equal("work", result.Name);
        Assert.Equal("zoho.com", result.Domain);
    }

    [Fact]
    public async Task AddAsync_FirstAccount_IsSetAsDefault()
    {
        var svc = BuildService();
        await svc.AddAsync("work", "tok-abc", "zoho.com");

        var root = await _store.LoadAsync();
        Assert.True(root.Accounts[0].IsDefault);
    }

    [Fact]
    public async Task AddAsync_SecondAccount_IsNotDefault()
    {
        var svc = BuildService();
        await svc.AddAsync("work", "tok-1", "zoho.com");
        await svc.AddAsync("personal", "tok-2", "zoho.com");

        var root = await _store.LoadAsync();
        Assert.True(root.Accounts[0].IsDefault);
        Assert.False(root.Accounts[1].IsDefault);
    }

    [Fact]
    public async Task AddAsync_DuplicateName_ThrowsAccountAlreadyExists()
    {
        var svc = BuildService();
        await svc.AddAsync("work", "tok-abc", "zoho.com");

        var ex = await Assert.ThrowsAsync<CliqCliException>(() =>
            svc.AddAsync("work", "tok-xyz", "zoho.com"));

        Assert.Equal(ErrorCodes.AccountAlreadyExists, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public async Task AddAsync_ZohoCorpEmail_ThrowsAccountDomainBlocked()
    {
        // FakeUserInfoService returns a ZohoCorp email
        var svc = BuildService(new FakeUserInfoService(email: "admin@zohocorp.com"));

        var ex = await Assert.ThrowsAsync<CliqCliException>(() =>
            svc.AddAsync("evil", "tok-xyz", "zoho.com"));

        Assert.Equal(ErrorCodes.AccountDomainBlocked, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public async Task AddAsync_ZohoCorpEmail_NothingWrittenToStoreOrKeychain()
    {
        var svc = BuildService(new FakeUserInfoService(email: "admin@zohocorp.com"));

        await Assert.ThrowsAsync<CliqCliException>(() =>
            svc.AddAsync("evil", "tok-xyz", "zoho.com"));

        var root = await _store.LoadAsync();
        Assert.Empty(root.Accounts);
        Assert.Equal(0, _keychain.Count);
    }

    [Fact]
    public async Task AddAsync_UnknownDomain_ThrowsInvalidArgs()
    {
        var svc = BuildService();

        var ex = await Assert.ThrowsAsync<CliqCliException>(() =>
            svc.AddAsync("work", "tok", "evil.corp"));

        Assert.Equal(ErrorCodes.InvalidArgs, ex.Code);
    }

    [Fact]
    public async Task AddAsync_StoresTokenInKeychain()
    {
        var svc = BuildService();
        await svc.AddAsync("work", "my-secret-token", "zoho.com");

        var retrieved = await _auth.GetTokenAsync("work");
        Assert.Equal("my-secret-token", retrieved);
    }

    [Fact]
    public async Task AddAsync_NoTokenInAccountsJson()
    {
        var svc = BuildService();
        await svc.AddAsync("work", "super-secret", "zoho.com");

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, "accounts.json"));
        Assert.DoesNotContain("super-secret", json, StringComparison.OrdinalIgnoreCase);
        // "token_type" is expected; standalone "token" property key would indicate a leak
        Assert.DoesNotContain("\"token\":", json, StringComparison.OrdinalIgnoreCase);
    }

    // ─── ListAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ReturnsAllAccounts_WithoutRawToken()
    {
        var svc = BuildService();
        await svc.AddAsync("work", "tok-1", "zoho.com");
        await svc.AddAsync("personal", "tok-2", "zoho.com");

        var items = await svc.ListAsync();

        Assert.Equal(2, items.Count);
        // AccountListItem has no Token property — the type itself enforces the mask
        Assert.All(items, item =>
        {
            var props = typeof(AccountListItem).GetProperties();
            Assert.DoesNotContain(props, p =>
                p.Name.Equals("Token", StringComparison.OrdinalIgnoreCase)
                && p.PropertyType == typeof(string)
                && (string?)p.GetValue(item) is not ("***" or null));
        });
    }

    [Fact]
    public async Task ListAsync_ScopeCountIsComputed()
    {
        var svc = BuildService();
        await svc.AddAsync("work", "tok-1", "zoho.com");

        var items = await svc.ListAsync();
        Assert.Equal(0, items[0].ScopeCount);
    }

    [Fact]
    public async Task ListAsync_EmptyStore_ReturnsEmptyList()
    {
        var svc = BuildService();
        var items = await svc.ListAsync();
        Assert.Empty(items);
    }

    // ─── ShowAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShowAsync_KnownAccount_ReturnsDetailWithMaskedToken()
    {
        var svc = BuildService();
        await svc.AddAsync("work", "tok-abc", "zoho.com");

        var detail = await svc.ShowAsync("work");

        Assert.Equal("work", detail.Name);
        Assert.Equal("***", detail.Token);
    }

    [Fact]
    public async Task ShowAsync_UnknownAccount_ThrowsAccountNotFound()
    {
        var svc = BuildService();

        var ex = await Assert.ThrowsAsync<CliqCliException>(() =>
            svc.ShowAsync("nonexistent"));

        Assert.Equal(ErrorCodes.AccountNotFound, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    // ─── RemoveAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_KnownAccount_RemovesFromStoreAndKeychain()
    {
        var svc = BuildService();
        await svc.AddAsync("work", "tok-abc", "zoho.com");

        await svc.RemoveAsync("work");

        var root = await _store.LoadAsync();
        Assert.Empty(root.Accounts);
        Assert.Equal(0, _keychain.Count);
    }

    [Fact]
    public async Task RemoveAsync_UnknownAccount_ThrowsAccountNotFound()
    {
        var svc = BuildService();

        var ex = await Assert.ThrowsAsync<CliqCliException>(() =>
            svc.RemoveAsync("ghost"));

        Assert.Equal(ErrorCodes.AccountNotFound, ex.Code);
    }

    [Fact]
    public async Task RemoveAsync_OnlyRemovesNamedAccount()
    {
        var svc = BuildService();
        await svc.AddAsync("work", "tok-1", "zoho.com");
        await svc.AddAsync("personal", "tok-2", "zoho.com");

        await svc.RemoveAsync("work");

        var root = await _store.LoadAsync();
        Assert.Single(root.Accounts);
        Assert.Equal("personal", root.Accounts[0].Name);
        Assert.Equal(1, _keychain.Count); // personal's token still present
    }

    // ─── SetDefaultAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task SetDefaultAsync_SetsOnlyNamedAccountAsDefault()
    {
        var svc = BuildService();
        await svc.AddAsync("work", "tok-1", "zoho.com");
        await svc.AddAsync("personal", "tok-2", "zoho.com");

        // After add: work is default (first), personal is not
        await svc.SetDefaultAsync("personal");

        var root = await _store.LoadAsync();
        Assert.False(root.Accounts.First(a => a.Name == "work").IsDefault);
        Assert.True(root.Accounts.First(a => a.Name == "personal").IsDefault);
    }

    [Fact]
    public async Task SetDefaultAsync_UnknownAccount_ThrowsAccountNotFound()
    {
        var svc = BuildService();

        var ex = await Assert.ThrowsAsync<CliqCliException>(() =>
            svc.SetDefaultAsync("ghost"));

        Assert.Equal(ErrorCodes.AccountNotFound, ex.Code);
    }

    [Fact]
    public async Task SetDefaultAsync_ExactlyOneDefaultAfterUpdate()
    {
        var svc = BuildService();
        await svc.AddAsync("a", "tok-1", "zoho.com");
        await svc.AddAsync("b", "tok-2", "zoho.com");
        await svc.AddAsync("c", "tok-3", "zoho.com");

        await svc.SetDefaultAsync("c");

        var root = await _store.LoadAsync();
        Assert.Equal(1, root.Accounts.Count(a => a.IsDefault));
        Assert.True(root.Accounts.First(a => a.Name == "c").IsDefault);
    }
}
