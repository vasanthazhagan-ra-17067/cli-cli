namespace CliqCli.Core;

/// <summary>
/// Represents a domain error in cliq-cli with a machine-readable code and exit code.
/// The global exception handler converts this to the JSON error envelope on stderr.
/// </summary>
public sealed class CliqCliException : Exception
{
    /// <summary>Symbolic error code (e.g. <c>ACCOUNT_NOT_FOUND</c>).</summary>
    public string Code { get; }

    /// <summary>Process exit code: 1 (general error) or 2 (auth failure / needs-reauth).</summary>
    public int ExitCode { get; }

    public CliqCliException(string message, string code, int exitCode = 1)
        : base(message)
    {
        Code = code;
        ExitCode = exitCode;
    }
}
