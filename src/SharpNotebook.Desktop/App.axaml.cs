using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using SharpNotebook.Services;

namespace SharpNotebook.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Same UserSecretsId as SharpNotebook.Web, deliberately — `dotnet user-secrets set OpenAI:ApiKey
            // ... --project src/SharpNotebook.Web` is enough for both frontends to see the key.
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
                .AddEnvironmentVariables()
                .Build();
            IAiCodeGenerator aiGenerator = new OpenAiCodeGenerator(configuration);

            desktop.MainWindow = new MainWindow(aiGenerator);
        }

        base.OnFrameworkInitializationCompleted();
    }
}