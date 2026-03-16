using System.Text.Json;
using System.Text.Json.Serialization;

namespace CliqCli.Core;

/// <summary>
/// Shared JSON serializer options for all output writers.
/// Uses SnakeCaseLower naming policy per the output contract (ADR-0004).
/// </summary>
internal static class OutputJsonOptions
{
    internal static readonly JsonSerializerOptions Shared = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// Error envelope type. <c>exitCode</c> uses explicit JsonPropertyName to preserve
/// camelCase as required by the output contract (ADR-0004).
/// </summary>
internal sealed record ErrorEnvelope
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("exitCode")]
    public required int ExitCode { get; init; }

    [JsonPropertyName("detail")]
    public object? Detail { get; init; }
}

/// <summary>
/// Production <see cref="IOutputWriter"/> implementation.
/// Writes success envelopes to stdout and error envelopes to stderr using System.Text.Json.
/// </summary>
public sealed class JsonOutputWriter : IOutputWriter
{
    /// <inheritdoc/>
    public void WriteSuccess(object data)
    {
        // Anonymous type: "Status" → "status", "Data" → "data" via SnakeCaseLower
        var envelope = new { Status = "ok", Data = data };
        Console.WriteLine(JsonSerializer.Serialize(envelope, OutputJsonOptions.Shared));
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
        Console.Error.WriteLine(JsonSerializer.Serialize(envelope, OutputJsonOptions.Shared));
    }
}
