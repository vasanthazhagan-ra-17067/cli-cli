using CliqCli.Core;
using CliqCli.Core.Auth;
using CliqCli.Tests.Fakes;
using Xunit;

namespace CliqCli.Tests;

public sealed class PatAuthProviderTests
{
    private readonly InMemoryKeychainProvider _keychain = new();
    private readonly PatAuthProvider _provider;

    public PatAuthProviderTests() => _provider = new PatAuthProvider(_keychain);

    // ─── ZohoCorp block ───────────────────────────────────────────────────────

    [Fact]
    public async Task StoreTokenAsync_ZohoCorpEmail_ThrowsAccountDomainBlocked()
    {
        var ex = await Assert.ThrowsAsync<CliqCliException>(() =>
            _provider.StoreTokenAsync("work", "tok123", "user@zohocorp.com"));

        Assert.Equal(ErrorCodes.AccountDomainBlocked, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public async Task StoreTokenAsync_ZohoCorpEuEmail_ThrowsAccountDomainBlocked()
    {
        var ex = await Assert.ThrowsAsync<CliqCliException>(() =>
            _provider.StoreTokenAsync("work", "tok123", "user@zohocorp.eu"));

        Assert.Equal(ErrorCodes.AccountDomainBlocked, ex.Code);
    }

    [Fact]
    public async Task StoreTokenAsync_ZohoCorpComAuEmail_ThrowsAccountDomainBlocked()
    {
        var ex = await Assert.ThrowsAsync<CliqCliException>(() =>
            _provider.StoreTokenAsync("work", "tok123", "user@zohocorp.com.au"));

        Assert.Equal(ErrorCodes.AccountDomainBlocked, ex.Code);
    }

    [Theory]
    [InlineData("ADMIN@ZOHOCORP.COM")]
    [InlineData("user@ZohoCorp.in")]
    public async Task StoreTokenAsync_ZohoCorpEmail_IsCaseInsensitive(string email)
    {
        await Assert.ThrowsAsync<CliqCliException>(() =>
            _provider.StoreTokenAsync("work", "tok", email));
    }

    [Fact]
    public async Task StoreTokenAsync_PersonalEmail_Succeeds()
    {
        // Must not throw
        await _provider.StoreTokenAsync("personal", "tok456", "user@example.com");
    }

    [Fact]
    public async Task StoreTokenAsync_NullEmail_Succeeds()
    {
        await _provider.StoreTokenAsync("anon", "tok789", email: null);
    }

    // ─── Round-trip ───────────────────────────────────────────────────────────

    [Fact]
    public async Task StoreAndGet_RoundTrip_TokenMatches()
    {
        const string accountName = "myaccount";
        const string token = "secret-PAT-value";

        await _provider.StoreTokenAsync(accountName, token, "user@example.com");
        var retrieved = await _provider.GetTokenAsync(accountName);

        Assert.Equal(token, retrieved);
    }

    [Fact]
    public async Task GetTokenAsync_NoToken_ThrowsAuthFailure()
    {
        var ex = await Assert.ThrowsAsync<CliqCliException>(() =>
            _provider.GetTokenAsync("nonexistent"));

        Assert.Equal(ErrorCodes.AuthFailure, ex.Code);
        Assert.Equal(2, ex.ExitCode);
    }

    [Fact]
    public async Task ClearTokenAsync_RemovesToken()
    {
        await _provider.StoreTokenAsync("clearme", "tok", "user@example.com");
        await _provider.ClearTokenAsync("clearme");

        var ex = await Assert.ThrowsAsync<CliqCliException>(() =>
            _provider.GetTokenAsync("clearme"));
        Assert.Equal(ErrorCodes.AuthFailure, ex.Code);
    }

    [Fact]
    public async Task StoreToken_DoesNotWriteTokenToKeyStore_NotPersisted_IfDomainBlocked()
    {
        // Even after a blocked attempt the token must not be in the keychain
        try
        {
            await _provider.StoreTokenAsync("bad", "supersecret", "admin@zohocorp.com");
        }
        catch (CliqCliException) { /* expected */ }

        // Attempting to read must fail (token was never stored)
        await Assert.ThrowsAsync<CliqCliException>(() => _provider.GetTokenAsync("bad"));
    }
}
