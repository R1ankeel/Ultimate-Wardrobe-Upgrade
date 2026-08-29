using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using System.Windows;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.Views;

namespace UltimateWardrobe.App;

/// <summary>
/// Application bootstrap (Phase 6 Sprint 6.1). Builds the composition root, wires Serilog to the
/// per-day log file under <c>%LocalAppData%\UltimateWardrobe\logs</c> and to the in-memory
/// <see cref="ILogViewer"/> ring buffer, then runs the startup gate
/// (<see cref="ProjectPickerWindow"/>) BEFORE the shell. A cancelled picker or a picker that ends
/// with no open project shuts the application down with code 0.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UltimateWardrobe",
            "logs");

        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSerilog((services, config) => config
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, "app-{Date}.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .WriteTo.Sink(new LogViewerSink(services.GetRequiredService<ILogViewer>())));

        CompositionRoot.Register(builder.Services);
        _host = builder.Build();
        _host.Start();

        // Eagerly apply persisted theme before any window shows - WpfUiThemeService ctor syncs ApplicationThemeManager with stored Dark/Light.
        _host.Services.GetRequiredService<IThemeService>();

        var session = _host.Services.GetRequiredService<IProjectSession>();
        var picker = _host.Services.GetRequiredService<ProjectPickerWindow>();
        picker.ShowDialog();

        if (session.IsOpen)
        {
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Closed += (_, _) => Shutdown();
            mainWindow.Show();
        }
        else
        {
            Shutdown(0);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}