# 11. Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Spectre.Console.Cli` | latest stable | CLI command/subcommand/flag framework |
| `System.Text.Json` | (stdlib, .NET 10) | JSON serialization |
| `Microsoft.Extensions.DependencyInjection` | latest stable | DI container |
| `Microsoft.Extensions.Logging` | latest stable | Structured logging |
| `Microsoft.Extensions.Logging.Console` | latest stable | Console log sink |
| `xunit` | latest stable | Unit testing |
| `xunit.runner.visualstudio` | latest stable | Test runner integration |
| `Microsoft.NET.Test.Sdk` | latest stable | Test SDK |

No external keychain NuGet — platform keychain access is implemented via direct P/Invoke to keep the binary self-contained.
