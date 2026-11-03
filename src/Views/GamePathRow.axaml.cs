using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using HikariHopper.Core;

namespace HikariHopper.Views;

/// <summary>
/// One installation in Settings: the edition's mark and <see cref="Title"/>, its folder
/// (<see cref="GamePath"/>), Browse… and a mark that clears it, over a line that says whether
/// <see cref="ExpectedExecutable"/> is in that folder. Typing a folder raises <see cref="PathCommitted"/>
/// when the field loses focus or Enter is pressed.
/// </summary>
public partial class GamePathRow : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<GamePathRow, string>(nameof(Title), "");

    public static readonly StyledProperty<string> EditionProperty =
        AvaloniaProperty.Register<GamePathRow, string>(nameof(Edition), "");

    public static readonly StyledProperty<string> GamePathProperty =
        AvaloniaProperty.Register<GamePathRow, string>(nameof(GamePath), "");

    public static readonly StyledProperty<string> ExpectedExecutableProperty =
        AvaloniaProperty.Register<GamePathRow, string>(nameof(ExpectedExecutable), "");

    public static readonly StyledProperty<bool> PathValidProperty =
        AvaloniaProperty.Register<GamePathRow, bool>(nameof(PathValid));

    public GamePathRow()
    {
        InitializeComponent();
        PathField.EditingFinished += (_, _) => PathCommitted?.Invoke(this, PathField.Text ?? "");
        BrowseButton.Click += (_, _) => BrowseRequested?.Invoke(this, EventArgs.Empty);
        ClearButton.Click += (_, _) => PathCommitted?.Invoke(this, "");
        Refresh();
    }

    public event EventHandler<string>? PathCommitted;

    public event EventHandler? BrowseRequested;

    /// <summary>The edition's name, "Enhanced" or "Legacy".</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The edition's settings id, which tints its mark.</summary>
    public string Edition
    {
        get => GetValue(EditionProperty);
        set => SetValue(EditionProperty, value);
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

    public bool PathValid
    {
        get => GetValue(PathValidProperty);
        set => SetValue(PathValidProperty, value);
    }

    /// <summary>The line under the folder.</summary>
    public string StatusMessage => GamePath.Length == 0
        ? "Not set"
        : PathValid ? $"✓ {ExpectedExecutable} found" : $"{ExpectedExecutable} isn't in this folder";

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GamePathProperty && PathField is not null && PathField.Text != GamePath)
        {
            PathField.Text = GamePath;
            PathField.CaretIndex = GamePath.Length;  // the end: a long path shows its last folders, which name the game
        }
        if (change.Property == TitleProperty || change.Property == EditionProperty || change.Property == GamePathProperty
            || change.Property == ExpectedExecutableProperty || change.Property == PathValidProperty)
            Refresh();
    }

    private void Refresh()
    {
        if (StatusText is null)
            return;
        Mark.Enhanced = Edition == GameSettings.EnhancedEdition;
        TitleText.Text = Title;
        ClearButton.IsVisible = GamePath.Length > 0;
        StatusText.Text = StatusMessage;
        StatusText.Foreground = GamePath.Length == 0 ? Ui.Theme.TextFaint : PathValid ? Ui.Theme.Ok : Ui.Theme.Warning;
        AutomationProperties.SetName(ClearButton, $"Clear the {Title} folder");
        AutomationProperties.SetName(PathField, $"{Title} installation folder");
    }
}
