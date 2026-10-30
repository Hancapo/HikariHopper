using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HikariHopper.Core;
using HikariHopper.Views;

namespace HikariHopper;

/// <summary>
/// The explorer session the startup sequence creates and restores (Python: the
/// <c>ExplorerTabs</c> object). The explorer-tabs port supplies it through
/// <see cref="StartupSequence.SessionFactory"/>.
/// </summary>
public interface IStartupSession
{
    /// <summary>Python <c>tabs.restore_last_game()</c>.</summary>
    void RestoreLastGame();

    /// <summary>Python <c>tabs.activeBridge.hasWorkspace</c>.</summary>
    bool HasWorkspace { get; }

    /// <summary>Python <c>tabs.activeBridge.gameName</c>.</summary>
    string GameName { get; }

    /// <summary>
    /// Builds the explorer interface into the main window: put <c>ExplorerScreen</c> into
    /// <see cref="MainWindow.ExplorerScreenHost"/> and keep <see cref="Window.Title"/> bound to
    /// <see cref="MainWindow.TitleFor"/> for the active tab (QML: <c>engine.load(main.qml)</c>
    /// with <c>tabs</c> as its initial property).
    /// </summary>
    void Attach(MainWindow window);
}

/// <summary>
/// Advance startup only after the splash has reached the screen (port of
/// <c>app.py::_StartupSequence</c>). Each phase is posted behind rendering so the splash shows
/// its message before the next phase runs, as Qt's <c>QTimer.singleShot(0, …)</c> did.
/// </summary>
public sealed class StartupSequence
{
    private readonly IClassicDesktopStyleApplicationLifetime desktop;
    private readonly SplashScreen? splash;
    private IStartupSession? session;
    private bool started;

    public StartupSequence(IClassicDesktopStyleApplicationLifetime desktop, SplashScreen? splash)
    {
        this.desktop = desktop;
        this.splash = splash;
    }

    /// <summary>
    /// HOOK for the explorer-tabs port: creates the session (Python <c>ExplorerTabs(image_provider=…)</c>,
    /// including whatever the texture viewer needs registered first). Until it is set, startup
    /// runs with <see cref="EmptySession"/>, which has no workspace and leaves the content host empty.
    /// </summary>
    public static Func<IStartupSession>? SessionFactory { get; set; }

    /// <summary>The main window once the interface is loaded.</summary>
    public MainWindow? MainWindow { get; private set; }

    public void Start()
    {
        if (splash is null)
        {
            Dispatcher.UIThread.Post(Begin, DispatcherPriority.Background);
            return;
        }
        // frameSwapped: wait for the splash's first presented frame, with a 150 ms fallback.
        splash.Opened += (_, _) => splash.RequestAnimationFrame(_ => Dispatcher.UIThread.Post(Begin, DispatcherPriority.Background));
        DispatcherTimer.RunOnce(Begin, TimeSpan.FromMilliseconds(150));
    }

    private void SetPhase(string message, string phase)
    {
        if (splash is null)
            return;
        splash.StartupMessage = message;
        splash.StartupPhase = phase;
    }

    private static void Next(Action step) => Dispatcher.UIThread.Post(step, DispatcherPriority.Background);

    private void Begin()
    {
        if (started)
            return;
        started = true;
        SetPhase("Loading explorer components…", "CORE");
        Next(CreateSession);
    }

    private void CreateSession()
    {
        try
        {
            session = SessionFactory?.Invoke() ?? new EmptySession();
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or TypeLoadException
                                          or FileNotFoundException or NotSupportedException)
        {
            Fail(error);
            return;
        }
        string savedGame = GameSettings.ConfiguredGameRootToOpen();
        string message = savedGame.Length > 0 && Directory.Exists(savedGame)
            ? "Loading FiveFury runtime and GTA V keys…"
            : "Checking saved GTA V installation…";
        SetPhase(message, "GAME");
        Next(RestoreWorkspace);
    }

    private void RestoreWorkspace()
    {
        session!.RestoreLastGame();
        string message = session.HasWorkspace ? $"Loaded {session.GameName}" : "No saved GTA V installation";
        SetPhase(message, "GAME");
        Next(PrepareInterface);
    }

    private void PrepareInterface()
    {
        SetPhase("Building explorer interface…", "UI");
        Next(LoadInterface);
    }

    private void LoadInterface()
    {
        MainWindow window;
        try
        {
            window = new MainWindow();
            session!.Attach(window);
            window.Closed += (_, _) => desktop.Shutdown();
            desktop.MainWindow = window;
            window.Show();
        }
        catch (InvalidOperationException error)
        {
            Fail(error);
            return;
        }
        MainWindow = window;
        SetPhase("Opening workspace…", "WINDOW");
        Next(() => Finish(window));
    }

    private void Finish(MainWindow window)
    {
        splash?.Close();
        window.Activate();
    }

    private void Fail(Exception error)
    {
        Console.Error.WriteLine($"HikariHopper startup failed: {error.Message}");
        SetPhase($"Startup failed · {error.Message}", "ERROR");
        TimeSpan delay = TimeSpan.FromMilliseconds(splash is not null ? 1800 : 0);
        DispatcherTimer.RunOnce(() => desktop.Shutdown(1), delay);
    }

    /// <summary>Stand-in until the explorer-tabs view model is ported: no workspace, empty content.</summary>
    public sealed class EmptySession : IStartupSession
    {
        public bool HasWorkspace => false;

        public string GameName => "";

        public void RestoreLastGame()
        {
        }

        public void Attach(MainWindow window)
        {
        }
    }
}
