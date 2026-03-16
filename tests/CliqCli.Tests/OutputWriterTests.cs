using System.Text.Json;
using CliqCli.Core;
using Xunit;

namespace CliqCli.Tests;

public sealed class OutputWriterTests
{
    [Fact]
    public void WriteSuccess_ProducesCorrectJsonEnvelope()
    {
        var writer = new InMemoryOutputWriter();

        writer.WriteSuccess(new { AccountName = "work" });

        Assert.Single(writer.SuccessOutputs);
        var doc = JsonDocument.Parse(writer.SuccessOutputs[0]);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("work", doc.RootElement.GetProperty("data").GetProperty("account_name").GetString());
    }

    [Fact]
    public void WriteError_ProducesCorrectJsonEnvelope()
    {
        var writer = new InMemoryOutputWriter();

        writer.WriteError("Account not found", ErrorCodes.AccountNotFound, 1);

        Assert.Single(writer.ErrorOutputs);
        var doc = JsonDocument.Parse(writer.ErrorOutputs[0]);
        Assert.Equal("Account not found", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(ErrorCodes.AccountNotFound, doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("exitCode").GetInt32());
        // detail must be absent when not provided
        Assert.False(doc.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public void WriteError_WithDetail_IncludesDetailField()
    {
        var writer = new InMemoryOutputWriter();

        writer.WriteError("HTTP 404 Not Found", ErrorCodes.ApiError, 1,
            detail: new { StatusCode = 404, Body = "Not Found" });

        Assert.Single(writer.ErrorOutputs);
        var doc = JsonDocument.Parse(writer.ErrorOutputs[0]);
        Assert.True(doc.RootElement.TryGetProperty("detail", out var detail));
        Assert.Equal(404, detail.GetProperty("status_code").GetInt32());
    }

    [Fact]
    public void CliqCliException_SerializedByExceptionHandler_ProducesCorrectEnvelope()
    {
        var writer = new InMemoryOutputWriter();
        var ex = new CliqCliException("Account not found", ErrorCodes.AccountNotFound, exitCode: 1);

        // Simulate what the global exception handler does
        writer.WriteError(ex.Message, ex.Code, ex.ExitCode);

        Assert.Single(writer.ErrorOutputs);
        var doc = JsonDocument.Parse(writer.ErrorOutputs[0]);
        Assert.Equal(ex.Message, doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(ex.Code, doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(ex.ExitCode, doc.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void WriteError_ExitCodeTwoForAuthFailure()
    {
        var writer = new InMemoryOutputWriter();

        writer.WriteError("Authentication failed", ErrorCodes.AuthFailure, 2);

        var doc = JsonDocument.Parse(writer.ErrorOutputs[0]);
        Assert.Equal(2, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(ErrorCodes.AuthFailure, doc.RootElement.GetProperty("code").GetString());
    }
}
