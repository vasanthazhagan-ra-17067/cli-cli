namespace CliqCli.Core;

/// <summary>
/// Routes all CLI output. Commands must never write directly to <see cref="System.Console"/>.
/// </summary>
public interface IOutputWriter
{
    /// <summary>Writes a success envelope to stdout: <c>{"status":"ok","data":...}</c>.</summary>
    void WriteSuccess(object data);

    /// <summary>
    /// Writes an error envelope to stderr: <c>{"error":"...","code":"...","exitCode":N}</c>.
    /// The optional <paramref name="detail"/> is included only for <c>API_ERROR</c> (see OQ-007).
    /// </summary>
    void WriteError(string message, string code, int exitCode, object? detail = null);
}
