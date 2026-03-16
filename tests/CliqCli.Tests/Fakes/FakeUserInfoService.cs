using CliqCli.Core.Auth;

namespace CliqCli.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IUserInfoService"/> for unit tests.
/// Returns a configured email or throws a configured exception — no network I/O.
/// </summary>
public sealed class FakeUserInfoService : IUserInfoService
{
    private readonly string? _email;
    private readonly Exception? _exception;

    public FakeUserInfoService(string? email = "user@example.com", Exception? exception = null)
    {
        _email = email;
        _exception = exception;
    }

    public Task<string?> GetUserEmailAsync(string domain, string token, CancellationToken ct = default)
    {
        if (_exception is not null)
            throw _exception;

        return Task.FromResult(_email);
    }
}
