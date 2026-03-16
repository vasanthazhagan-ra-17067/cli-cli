using System.Reflection;
using CliqCli.Commands;
using CliqCli.Core;
using CliqCli.Core.Accounts;
using CliqCli.Core.Auth;
using CliqCli.Keychain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace CliqCli;

internal static class Program
{
    public static int Main(string[] args)
    {
        // --version is not built into a CommandApp without a root command;
        // handle it explicitly before dispatching to Spectre.
        if (args is ["--version"])
        {
            var ver = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "1.0.0";
            Console.WriteLine(ver);
            return 0;
        }

        var services = new ServiceCollection();

        // Output writer: all command output must flow through this — never Console directly
        services.AddSingleton<IOutputWriter, JsonOutputWriter>();

        // Keychain: select best available provider for this platform
        services.AddSingleton<IKeychainProvider>(_ =>
            KeychainProviderFactory.Create());

        // HTTP client for outbound requests (user-info API during account add)
        services.AddSingleton<HttpClient>();

        // Account store, user-info lookup, and auth provider
        services.AddSingleton<IAccountStore, AccountStore>();
        services.AddSingleton<IUserInfoService, UserInfoService>();
        services.AddSingleton<IAuthProvider, PatAuthProvider>();

        // Account service — orchestrates IAccountStore + IAuthProvider + IUserInfoService
        services.AddSingleton<IAccountService, AccountService>();

        // Logging: Warning+ to stderr only so JSON stdout contract is never broken
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });
        });

        var registrar = new DependencyInjectionRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("cliq-cli");
            config.ValidateExamples();

            // ─── account commands ─────────────────────────────────────────────────
            config.AddBranch("account", account =>
            {
                account.AddCommand<AccountCommands.AddAccountCommand>("add")
                    .WithDescription("Add a Zoho Cliq account using a Personal Access Token.");
                account.AddCommand<AccountCommands.ListAccountsCommand>("list")
                    .WithDescription("List all configured accounts.");
                account.AddCommand<AccountCommands.ShowAccountCommand>("show")
                    .WithDescription("Show details for a named account.");
                account.AddCommand<AccountCommands.RemoveAccountCommand>("remove")
                    .WithDescription("Remove an account from the keychain and accounts.json.");
                account.AddCommand<AccountCommands.SetDefaultAccountCommand>("set-default")
                    .WithDescription("Set the named account as the default.");
                account.AddCommand<AccountCommands.ReAuthAccountCommand>("re-auth")
                    .WithDescription("Re-authenticate an account (v1 stub — not yet supported).");
            });

            config.SetExceptionHandler((ex, resolver) =>
            {
                var writer = resolver?.Resolve(typeof(IOutputWriter)) as IOutputWriter
                             ?? new JsonOutputWriter();

                // Ctrl+C: exit 1, no output
                if (ex is OperationCanceledException)
                    return 1;

                if (ex is CliqCliException cliqEx)
                {
                    writer.WriteError(cliqEx.Message, cliqEx.Code, cliqEx.ExitCode);
                    return cliqEx.ExitCode;
                }

                writer.WriteError(ex.Message, ErrorCodes.InternalError, 1);
                return 1;
            });
        });

        return app.Run(args);
    }
}
