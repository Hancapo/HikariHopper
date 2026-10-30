using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HikariHopper.Core;
using HikariHopper.Infrastructure;
using HikariHopper.Views;

namespace HikariHopper;

/// <summary>Application bootstrap (port of <c>app.py::main</c>).</summary>
public partial class App : Application
{
    /// <summary>The running startup sequence (its <see cref="StartupSequence.MainWindow"/> once loaded).</summary>
    public StartupSequence? Startup { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Qt quit when the last primary window closed; the sequence shuts down explicitly
            // when the main window closes, or when the splash is closed before it exists.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AppServices.Dialogs = new AvaloniaFileDialogs();
            GameSettings.MigrateLegacySettings();

            SplashScreen? splash = null;
            if (StartupSplashRequired())
            {
                splash = new SplashScreen();
                splash.CenterOnScreen();
            }
            StartupSequence.SessionFactory ??= () => new ExplorerSession();
            var startup = new StartupSequence(desktop, splash);
            Startup = startup;
            if (splash is not null)
            {
                splash.Closed += (_, _) =>
                {
                    if (startup.MainWindow is null)
                        desktop.Shutdown();
                };
                splash.Show();
            }
            startup.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Python <c>startup_splash_required()</c>: only when a saved game will be opened.</summary>
    public static bool StartupSplashRequired() => GameSettings.ConfiguredGameRootToOpen().Length > 0;
}
