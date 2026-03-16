namespace CliqCli.Core;

/// <summary>
/// Enforces the ZohoCorp account hard-block (ADR-0006).
/// This check is sealed into CliqCli.Core and has no runtime override path.
/// </summary>
internal static class ZohoCorpGuard
{
    /// <summary>
    /// Throws <see cref="CliqCliException"/> with code <c>ACCOUNT_DOMAIN_BLOCKED</c>
    /// if the first DNS label of the email host equals <c>zohocorp</c> (case-insensitive).
    /// </summary>
    /// <remarks>
    /// The single-label check (`email.Split('@')[1].Split('.')[0]`) covers
    /// zohocorp.com, zohocorp.eu, zohocorp.in, zohocorp.com.au, and all future
    /// datacenter TLDs automatically — no explicit TLD list required.
    /// </remarks>
    public static void AssertNotZohoCorp(string? email)
    {
        if (email is null)
            return;

        var atIndex = email.IndexOf('@');
        if (atIndex < 0)
            return;

        var host = email[(atIndex + 1)..];
        var firstLabel = host.Split('.')[0];

        if (firstLabel.Equals("zohocorp", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliqCliException(
                "ZohoCorp accounts are not permitted. Use a personal or external Zoho account.",
                ErrorCodes.AccountDomainBlocked);
        }
    }
}
