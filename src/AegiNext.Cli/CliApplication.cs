using System.Globalization;
using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AegiNext.Cli;

/// <summary>控制台命令组合与统一错误出口。</summary>
public sealed class CliApplication
{
    private readonly CommandApp application;
    private readonly CliTypeRegistrar registrar = new();
    private readonly CliWizardCatalog catalog = new();

    /// <summary>建立可独立测试的主机并登记字幕转换命令。</summary>
    public CliApplication(CliRuntime runtime, ISubtitleConversionService? conversion = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        application = new(registrar);
        RegisterInstance(runtime);
        RegisterInstance(catalog);
        RegisterInstance(runtime.Console);
        var runner = new ConvertRunner(runtime, conversion ?? new SubtitleConversionService());
        RegisterInstance(runner);
        application.SetDefaultCommand<RootCommand>();
        application.Configure(configuration =>
        {
            configuration.SetApplicationName("aegnc");
            configuration.SetApplicationVersion(typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0");
            configuration.Settings.Console = runtime.Console;
            configuration.Settings.Culture = CultureInfo.InvariantCulture;
            configuration.Settings.StrictParsing = true;
            configuration.Settings.CancellationExitCode = 130;
            configuration.SetExceptionHandler((error, _) =>
            {
                runtime.Console.WriteLine(error is OperationCanceledException ? "Operation canceled." : $"Error: {error.Message}");
                return error switch
                {
                    OperationCanceledException => 130,
                    CliUsageException or CommandParseException => 2,
                    _ => 1
                };
            });
        });
        RegisterCommand<ConvertCommand>("convert", "Convert ASS, SRT, or project subtitles", cancellationToken => runner.RunAsync(new(), cancellationToken));
    }

    /// <summary>登记供追加命令使用的依赖实例。</summary>
    public void RegisterInstance<T>(T instance) where T : class
    {
        registrar.RegisterInstance(typeof(T), instance);
    }

    /// <summary>同时登记子命令和根向导入口。</summary>
    public void RegisterCommand<TCommand>(string name, string description, Func<CancellationToken, Task<int>> wizard) where TCommand : class, ICommand
    {
        application.Configure(configuration => configuration.AddCommand<TCommand>(name).WithDescription(description));
        catalog.Add(new(name, description, wizard));
    }

    /// <summary>使用固定英文语言环境运行命令并传递取消，结束后恢复调用方的语言环境。</summary>
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            return await application.RunAsync(args, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
