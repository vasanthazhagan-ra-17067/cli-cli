namespace CliqCli.Core;

/// <summary>
/// Symbolic error code vocabulary for the JSON error envelope (ADR-0004).
/// Callers branch on these codes; the human-readable message may change independently.
/// </summary>
public static class ErrorCodes
{
    public const string AccountNotFound = "ACCOUNT_NOT_FOUND";
    public const string AccountAlreadyExists = "ACCOUNT_ALREADY_EXISTS";
    public const string NoDefaultAccount = "NO_DEFAULT_ACCOUNT";
    public const string AuthFailure = "AUTH_FAILURE";
    public const string NeedsReauth = "NEEDS_REAUTH";
    public const string ApiError = "API_ERROR";
    public const string InvalidArgs = "INVALID_ARGS";
    public const string IoError = "IO_ERROR";
    public const string KeychainError = "KEYCHAIN_ERROR";
    public const string AccountDomainBlocked = "ACCOUNT_DOMAIN_BLOCKED";
    public const string HostNotAllowed = "HOST_NOT_ALLOWED";
    public const string NotImplemented = "NOT_IMPLEMENTED";

    /// <summary>Used by the global exception handler for unhandled non-CliqCliException errors.</summary>
    public const string InternalError = "INTERNAL_ERROR";
}
