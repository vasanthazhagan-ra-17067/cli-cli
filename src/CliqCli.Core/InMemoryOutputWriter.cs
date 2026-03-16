using System.Text.Json;

namespace CliqCli.Core;

/// <summary>
/// In-memory <see cref="IOutputWriter"/> for use in unit tests.
/// Captures all output as serialized JSON strings for assertion.
/// </summary>
public sealed class InMemoryOutputWriter : IOutputWriter
{
    /// <summary>JSON-serialized success envelopes emitted via <see cref="WriteSuccess"/>.</summary>
    public List<string> SuccessOutputs { get; } = new();

    /// <summary>JSON-serialized error envelopes emitted via <see cref="WriteError"/>.</summary>
    public List<string> ErrorOutputs { get; } = new();

    /// <inheritdoc/>
    public void WriteSuccess(object data)
    {
        var envelope = new { Status = "ok", Data = data };
        SuccessOutputs.Add(JsonSerializer.Serialize(envelope, OutputJsonOptions.Shared));
    }

    /// <inheritdoc/>
    public void WriteError(string message, string code, int exitCode, object? detail = null)
    {
        var envelope = new ErrorEnvelope
        {
            Error = message,
            Code = code,
            ExitCode = exitCode,
            Detail = detail
        };
        ErrorOutputs.Add(JsonSerializer.Serialize(envelope, OutputJsonOptions.Shared));
    }
}
