using CliqCli.Core;
using CliqCli.Core.Accounts;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CliqCli.Commands;

/// <summary>
/// All six <c>account</c> subcommands.  Command classes are thin: they validate flags,
/// delegate to <see cref="IAccountService"/>, and write output via <see cref="IOutputWriter"/>.
/// No business logic lives here (ADR-0009).
/// </summary>
internal static class AccountCommands
{
    // ─── account add ─────────────────────────────────────────────────────────

    public sealed class AddAccountSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        [CommandOption("--token <TOKEN>")]
        public string? Token { get; init; }

        [CommandOption("--domain <DOMAIN>")]
        public string Domain { get; init; } = "zoho.com";

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return ValidationResult.Error("--name is required.");
            if (string.IsNullOrWhiteSpace(Token))
                return ValidationResult.Error("--token is required.");
            return ValidationResult.Success();
        }
    }

    public sealed class AddAccountCommand : AsyncCommand<AddAccountSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public AddAccountCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, AddAccountSettings settings)
        {
            var result = await _service.AddAsync(settings.Name!, settings.Token!, settings.Domain);
            _output.WriteSuccess(result);
            return 0;
        }
    }

    // ─── account list ─────────────────────────────────────────────────────────

    public sealed class ListAccountsCommand : AsyncCommand<GlobalSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public ListAccountsCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
        {
            var items = await _service.ListAsync();
            _output.WriteSuccess(items);
            return 0;
        }
    }

    // ─── account show ─────────────────────────────────────────────────────────

    public sealed class ShowAccountSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return ValidationResult.Error("--name is required.");
            return ValidationResult.Success();
        }
    }

    public sealed class ShowAccountCommand : AsyncCommand<ShowAccountSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public ShowAccountCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ShowAccountSettings settings)
        {
            var detail = await _service.ShowAsync(settings.Name!);
            _output.WriteSuccess(detail);
            return 0;
        }
    }

    // ─── account remove ───────────────────────────────────────────────────────

    public sealed class RemoveAccountSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return ValidationResult.Error("--name is required.");
            return ValidationResult.Success();
        }
    }

    public sealed class RemoveAccountCommand : AsyncCommand<RemoveAccountSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public RemoveAccountCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, RemoveAccountSettings settings)
        {
            await _service.RemoveAsync(settings.Name!);
            _output.WriteSuccess(new { removed = settings.Name });
            return 0;
        }
    }

    // ─── account set-default ──────────────────────────────────────────────────

    public sealed class SetDefaultSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return ValidationResult.Error("--name is required.");
            return ValidationResult.Success();
        }
    }

    public sealed class SetDefaultAccountCommand : AsyncCommand<SetDefaultSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public SetDefaultAccountCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, SetDefaultSettings settings)
        {
            await _service.SetDefaultAsync(settings.Name!);
            _output.WriteSuccess(new { default_account = settings.Name });
            return 0;
        }
    }

    // ─── account re-auth ──────────────────────────────────────────────────────

    public sealed class ReAuthSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return ValidationResult.Error("--name is required.");
            return ValidationResult.Success();
        }
    }

    public sealed class ReAuthAccountCommand : AsyncCommand<ReAuthSettings>
    {
        private readonly IOutputWriter _output;

        public ReAuthAccountCommand(IOutputWriter output) => _output = output;

        public override Task<int> ExecuteAsync(CommandContext context, ReAuthSettings settings)
        {
            _output.WriteError(
                "re-auth is not supported in v1; use 'account remove' and re-add with a new PAT",
                ErrorCodes.NotImplemented,
                exitCode: 1);
            return Task.FromResult(1);
        }
    }
}
