using CliqCli.Keychain;

namespace CliqCli.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IKeychainProvider"/> for use in unit tests.
/// No OS keychain or filesystem access is performed.
/// </summary>
public sealed class InMemoryKeychainProvider : IKeychainProvider
{
    private readonly Dictionary<string, string> _store = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(key, out var value);
        return Task.FromResult(value);
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        bool removed = _store.Remove(key);
        return Task.FromResult(removed);
    }

    /// <summary>Returns the number of entries currently in the store.</summary>
    public int Count => _store.Count;

    /// <summary>Returns all stored keys (read-only view for assertions).</summary>
    public IReadOnlyCollection<string> Keys => _store.Keys;
}
