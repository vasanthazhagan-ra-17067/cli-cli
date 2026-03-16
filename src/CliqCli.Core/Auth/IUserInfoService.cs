namespace CliqCli.Core.Auth;

/// <summary>
/// Fetches authenticated user metadata from the Zoho Accounts API (ADR-0005, OQ-001).
/// Used during <c>account add</c> to retrieve the user's email for the ZohoCorp domain block.
/// </summary>
public interface IUserInfoService
{
    /// <summary>
    /// Returns the email address associated with the supplied PAT, or <see langword="null"/>
    /// if the API response does not contain an email field.
    /// </summary>
    /// <param name="domain">Zoho datacenter domain, e.g. <c>zoho.com</c>.</param>
    /// <param name="token">PAT to authenticate with.</param>
    /// <exception cref="CliqCliException">
    ///   <c>AUTH_FAILURE</c> (exit 2) on HTTP 401; <c>IO_ERROR</c> (exit 1) on network failure or non-success status.
    /// </exception>
    Task<string?> GetUserEmailAsync(string domain, string token, CancellationToken ct = default);
}
