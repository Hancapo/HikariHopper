using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HikariHopper.Views.Controls;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Views;

/// <summary>
/// The common loading overlay: a dim over what is behind, and across its middle a band holding the steam train (<see cref="LoadingTrain"/>)
/// over a caption, a small capitalised <see cref="Title"/> and what is loading as <see cref="Detail"/>. The moving train
/// is the busy sign; there is no spinner. A long task with a known length also shows <see cref="Progress"/> as a bar
/// with <see cref="ProgressText"/> in it, and one that can stop offers Cancel (<see cref="CanCancel"/>,
/// <see cref="CancelRequested"/>). Put it over what it covers and show it while the work runs, with
/// <see cref="IsRunning"/> set so the train moves only then.
/// </summary>
public sealed class LoadingOverlay : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<LoadingOverlay, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DetailProperty =
        AvaloniaProperty.Register<LoadingOverlay, string?>(nameof(Detail));

    public static readonly StyledProperty<bool> IsRunningProperty =
        AvaloniaProperty.Register<LoadingOverlay, bool>(nameof(IsRunning));

    public static readonly StyledProperty<double?> ProgressProperty =
        AvaloniaProperty.Register<LoadingOverlay, double?>(nameof(Progress));

    public static readonly StyledProperty<string?> ProgressTextProperty =
        AvaloniaProperty.Register<LoadingOverlay, string?>(nameof(ProgressText));

    public static readonly StyledProperty<bool> CanCancelProperty =
        AvaloniaProperty.Register<LoadingOverlay, bool>(nameof(CanCancel));

    /// <summary>The progress bar's size.</summary>
    internal const double BarWidth = 260, BarHeight = 16;

    /// <summary>The band's room above and below what it holds.</summary>
    internal const double BandPadding = 26;

    private readonly TextBlock title = new()
    {
        Foreground = T.TextDim,
        FontFamily = T.MonoFont,
        FontSize = T.SmallFontSize,
        FontWeight = FontWeight.Bold,
        LetterSpacing = 1.2,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private readonly TextBlock detail = new()
    {
        Foreground = T.Text,
        FontFamily = T.UiFont,
        FontSize = T.FontSize,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxWidth = 480,
    };

    // The bar: a sunken well, the accent filling it from the left, and the count over both.
    private readonly Border fill = new()
    {
        Background = T.Accent,
        CornerRadius = new CornerRadius(T.ControlRadius - 1),
        HorizontalAlignment = HorizontalAlignment.Left,
        Width = 0,
    };

    private readonly TextBlock count = new()
    {
        Foreground = T.Text,
        FontFamily = T.MonoFont,
        FontSize = T.SmallFontSize,
        FontWeight = FontWeight.Bold,
        LetterSpacing = .6,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Border bar;

    private readonly ChromeToolButton cancel = new()
    {
        Raised = true,
        Text = "Cancel",
        Width = 88,
        Height = 28,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 14, 0, 0),
        IsVisible = false,
    };

    public LoadingOverlay()
    {
        Background = T.LoadingDim;
        Train = new LoadingTrain { HorizontalAlignment = HorizontalAlignment.Center };
        bar = new Border
        {
            Width = BarWidth,
            Height = BarHeight,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = T.InsetBg,
            BorderBrush = T.BorderHard,
            BorderThickness = new Thickness(1),
            CornerRadius = T.ControlCorner,
            IsVisible = false,
            Child = new Panel { Children = { fill, count } },
        };
        cancel.Click += OnCancel;
        AutomationProperties.SetName(cancel, "Cancel");
        // The train over the caption and, for a long task, its bar and Cancel, on a band of the canvas across the whole
        // overlay with hard seams top and bottom, so they sit on something rather than float over what is dimmed; the
        // whole a little above the middle.
        Band = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 28),
            Background = T.AppBg,
            BorderBrush = T.BorderHard,
            BorderThickness = new Thickness(0, 1),
            Child = new Panel
            {
                Children =
                {
                    new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = T.Bevel },
                    new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, BandPadding),
                        Children =
                        {
                            Train,
                            new StackPanel { Spacing = 2, Margin = new Thickness(0, 12, 0, 0), Children = { title, detail } },
                            bar,
                            cancel,
                        },
                    },
                },
            },
        };
        Content = Band;
    }

    /// <summary>What kind of work it is, as the caption's small capitals show it: "LOADING GAME".</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>What is loading, under the title.</summary>
    public string? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    /// <summary>Whether the train moves: true while the overlay shows.</summary>
    public bool IsRunning
    {
        get => GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    /// <summary>How much of the work is done, 0 to 1; none hides the bar, for work of no known length.</summary>
    public double? Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>What the bar says over its fill: "3/12".</summary>
    public string? ProgressText
    {
        get => GetValue(ProgressTextProperty);
        set => SetValue(ProgressTextProperty, value);
    }

    /// <summary>Whether Cancel shows under the bar. Pressing it greys it until this is set again.</summary>
    public bool CanCancel
    {
        get => GetValue(CanCancelProperty);
        set => SetValue(CanCancelProperty, value);
    }

    /// <summary>Cancel was pressed: the task should stop, and whoever shows the overlay hides it once it has.</summary>
    public event EventHandler? CancelRequested;

    /// <summary>The train.</summary>
    internal LoadingTrain Train { get; }

    /// <summary>The band across the overlay that holds it all.</summary>
    internal Border Band { get; }

    /// <summary>The bar, its fill and Cancel, for tests.</summary>
    internal Border Bar => bar;

    internal Border Fill => fill;

    internal ChromeToolButton CancelButton => cancel;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty)
            title.Text = Title;
        else if (change.Property == DetailProperty)
            detail.Text = Detail;
        else if (change.Property == IsRunningProperty)
            Train.IsRunning = IsRunning;
        else if (change.Property == ProgressProperty)
        {
            bar.IsVisible = Progress is not null;
            // The fill inside the well's border, never narrower than its two rounded ends once anything is done.
            double share = Math.Clamp(Progress ?? 0, 0, 1);
            fill.Width = share <= 0 ? 0 : Math.Max(2 * (T.ControlRadius - 1), (BarWidth - 2) * share);
        }
        else if (change.Property == ProgressTextProperty)
            count.Text = ProgressText;
        else if (change.Property == CanCancelProperty)
        {
            cancel.IsVisible = CanCancel;
            cancel.IsEnabled = CanCancel;
        }
        if (change.Property == TitleProperty || change.Property == DetailProperty)
            AutomationProperties.SetName(this, string.Join(' ', new[] { Title, Detail }.Where(text => !string.IsNullOrEmpty(text))));
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        // Pressed once, it greys while the task winds down.
        cancel.IsEnabled = false;
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
