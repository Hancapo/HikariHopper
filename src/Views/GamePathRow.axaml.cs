using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using HikariHopper.Core;

namespace HikariHopper.Views;

/// <summary>
/// One installation in Settings: the edition's mark and <see cref="Title"/>, over its folder
/// (<see cref="GamePath"/>) and whether <see cref="ExpectedExecutable"/> is in it. Choose folder… (Change… once one
/// is set) raises <see cref="BrowseRequested"/>; the clear mark commits an empty folder through <see cref="PathCommitted"/>.
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

    private readonly Run pathRun = new() { Foreground = Ui.Theme.TextRow };
    private readonly Run gap = new("   ");

    public GamePathRow()
    {
        InitializeComponent();
        DetailText.Inlines!.Add(pathRun);
        DetailText.Inlines.Add(gap);
        DetailText.Inlines.Add(StatusText);
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

    /// <summary>Whether the executable is there, after the folder.</summary>
    internal Run StatusText { get; } = new();

    /// <summary>What the line under the name says about the folder.</summary>
    public string StatusMessage => GamePath.Length == 0
        ? "Not set"
        : PathValid ? $"✓ {ExpectedExecutable} found" : $"{ExpectedExecutable} isn't in this folder";

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty || change.Property == EditionProperty || change.Property == GamePathProperty
            || change.Property == ExpectedExecutableProperty || change.Property == PathValidProperty)
            Refresh();
    }

    private void Refresh()
    {
        if (DetailText is null)
            return;
        bool set = GamePath.Length > 0;
        Mark.Enhanced = Edition == GameSettings.EnhancedEdition;
        TitleText.Text = Title;
        pathRun.Text = GamePath;
        gap.Text = set ? "   " : "";
        StatusText.Text = StatusMessage;
        StatusText.Foreground = !set ? Ui.Theme.TextFaint : PathValid ? Ui.Theme.Ok : Ui.Theme.Warning;
        BrowseButton.Text = set ? "Change…" : "Choose folder…";
        BrowseButton.Width = set ? 84 : 118;
        ClearButton.IsVisible = set;
        ToolTip.SetTip(DetailText, set ? GamePath : null);
        AutomationProperties.SetName(BrowseButton, set ? $"Change the {Title} folder" : $"Choose the {Title} folder");
        AutomationProperties.SetName(ClearButton, $"Clear the {Title} folder");
    }
}
