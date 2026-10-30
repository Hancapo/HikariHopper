using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// One title in the Settings game-path register (port of <c>GamePathRow.qml</c>).
/// <see cref="PathCommitted"/> fires with the field text when editing finishes;
/// <see cref="BrowseRequested"/> when Browse… is clicked.
/// </summary>
public partial class GamePathRow : UserControl
{
    public static readonly StyledProperty<string> GameNameProperty =
        AvaloniaProperty.Register<GamePathRow, string>(nameof(GameName), "");

    public static readonly StyledProperty<string> GamePathProperty =
        AvaloniaProperty.Register<GamePathRow, string>(nameof(GamePath), "");

    public static readonly StyledProperty<string> ExpectedExecutableProperty =
        AvaloniaProperty.Register<GamePathRow, string>(nameof(ExpectedExecutable), "");

    public static readonly StyledProperty<bool> SupportedProperty =
        AvaloniaProperty.Register<GamePathRow, bool>(nameof(Supported));

    public static readonly StyledProperty<bool> PathValidProperty =
        AvaloniaProperty.Register<GamePathRow, bool>(nameof(PathValid));

    private readonly TextBlock nameText;
    private readonly FlatTextField pathField;
    private readonly ChromeToolButton browseButton;
    private readonly TextBlock statusText;
    private bool loaded;

    public GamePathRow()
    {
        AvaloniaXamlLoader.Load(this);
        nameText = this.FindControl<TextBlock>("NameText")!;
        pathField = this.FindControl<FlatTextField>("PathField")!;
        browseButton = this.FindControl<ChromeToolButton>("BrowseButton")!;
        statusText = this.FindControl<TextBlock>("StatusText")!;
        pathField.EditingFinished += (_, _) => PathCommitted?.Invoke(this, pathField.Text ?? "");
        browseButton.Click += (_, _) => BrowseRequested?.Invoke(this, EventArgs.Empty);
        Refresh();
    }

    public event EventHandler<string>? PathCommitted;

    public event EventHandler? BrowseRequested;

    public string GameName
    {
        get => GetValue(GameNameProperty);
        set => SetValue(GameNameProperty, value);
    }

    public string GamePath
    {
        get => GetValue(GamePathProperty);
        set => SetValue(GamePathProperty, value);
    }

    public string ExpectedExecutable
    {
        get => GetValue(ExpectedExecutableProperty);
        set => SetValue(ExpectedExecutableProperty, value);
    }

    public bool Supported
    {
        get => GetValue(SupportedProperty);
        set => SetValue(SupportedProperty, value);
    }

    public bool PathValid
    {
        get => GetValue(PathValidProperty);
        set => SetValue(PathValidProperty, value);
    }

    /// <summary>The status line under the field.</summary>
    public string StatusMessage => GamePath.Length == 0
        ? "Not configured"
        : PathValid ? "Executable detected" : $"{ExpectedExecutable} not found";

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (loaded)
            return;
        // Component.onCompleted: cursorPosition = 0, so a long path shows its start.
        loaded = true;
        pathField.CaretIndex = 0;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GamePathProperty && pathField is not null)
        {
            // Qt's TextInput moves the cursor to the end when its text is replaced.
            pathField.Text = GamePath;
            pathField.CaretIndex = loaded ? GamePath.Length : 0;
        }
        if (change.Property == GameNameProperty || change.Property == GamePathProperty
            || change.Property == ExpectedExecutableProperty || change.Property == SupportedProperty
            || change.Property == PathValidProperty)
            Refresh();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        return new Size(0, Supported ? 62 : 45);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        pathField.Width = Math.Max(0, e.NewSize.Width - 238 - 96);
        Canvas.SetLeft(browseButton, e.NewSize.Width - 10 - 78);
    }

    private void Refresh()
    {
        if (nameText is null)
            return;
        double fieldTop = Supported ? 7 : 8;
        nameText.Text = GameName;
        nameText.Foreground = Supported ? Ui.Theme.TextRow : Ui.Theme.TextFaint;
        Canvas.SetTop(nameText, Supported ? 10 : 13);
        Canvas.SetTop(pathField, fieldTop);
        Canvas.SetTop(browseButton, fieldTop);
        pathField.IsEnabled = Supported;
        pathField.Opacity = Supported ? 1 : 0.48;
        pathField.PlaceholderText = Supported ? "Select the installation folder" : "Future support";
        browseButton.IsEnabled = Supported;
        statusText.IsVisible = Supported;
        statusText.Text = StatusMessage;
        statusText.Foreground = GamePath.Length > 0 && !PathValid ? Ui.Theme.Error : Ui.Theme.TextFaint;
        InvalidateMeasure();
    }
}
