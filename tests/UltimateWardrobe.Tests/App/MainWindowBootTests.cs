using Microsoft.Extensions.DependencyInjection;
using UltimateWardrobe.App;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.Views;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Tests.Persistence;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// Regression: the shell must construct right after the startup gate opens a project (Sprint 6.6
/// crash - <c>Icon="{StaticResource AppIcon}"</c> on the MainWindow root could never resolve its own
/// window resources and threw a XamlParseException, so the app died immediately after picking/
/// creating a project). Runs on a dedicated STA thread; the icon is now resolved from code with a
/// null-safe lookup, so the window builds even with no Application instance in scope.
/// </summary>
public class MainWindowBootTests
{
    [Fact]
    public void MainWindow_builds_with_an_open_session()
    {
        var root = TestHelpers.NewTempDir("UW_Boot_");
        try
        {
            Exception? captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var services = new ServiceCollection();
                    CompositionRoot.Register(services, registerUi: true);
                    using var provider = services.BuildServiceProvider();

                    var project = new Project(Guid.NewGuid(), "Boot", root);
                    var db = Path.Combine(root, "project.db");
                    provider.GetRequiredService<IProjectSession>().Open(project, db);

                    var window = provider.GetRequiredService<MainWindow>();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            var finished = thread.Join(30000);

            if (!finished)
            {
                throw new TimeoutException("MainWindow construction did not finish within 30s.");
            }

            if (captured is not null)
            {
                throw new InvalidOperationException(
                    "MainWindow must build without crashing with an open session.",
                    captured);
            }
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }
}