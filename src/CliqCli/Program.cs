using CliqCli.Keychain;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace CliqCli;

internal static class Program
{
    public static int Main(string[] args)
    {
        var services = new ServiceCollection();

        // Keychain: select best available provider for this platform
        services.AddSingleton<IKeychainProvider>(_ =>
            KeychainProviderFactory.Create());

        var registrar = new DependencyInjectionRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("cliq-cli");
            config.ValidateExamples();
        });

        return app.Run(args);
    }
}
