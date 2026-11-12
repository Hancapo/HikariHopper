using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Views;

/// <summary>
/// The common loading overlay: a dim over what is behind, and in the middle the steam train (<see cref="LoadingTrain"/>)
/// over a caption, a small capitalised <see cref="Title"/> and what is loading as <see cref="Detail"/>. The moving train
/// is the busy sign; there is no spinner. Put it over what it covers and show it while the work runs, with
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

    public LoadingOverlay()
    {
        Background = T.LoadingDim;
        Train = new LoadingTrain { HorizontalAlignment = HorizontalAlignment.Center };
        // The train over the caption, the whole a little above the middle.
        Content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 28),
            Children =
            {
                Train,
                new StackPanel { Spacing = 2, Margin = new Thickness(0, 12, 0, 0), Children = { title, detail } },
            },
        };
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

    /// <summary>The train.</summary>
    internal LoadingTrain Train { get; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty)
            title.Text = Title;
        else if (change.Property == DetailProperty)
            detail.Text = Detail;
        else if (change.Property == IsRunningProperty)
            Train.IsRunning = IsRunning;
        if (change.Property == TitleProperty || change.Property == DetailProperty)
            AutomationProperties.SetName(this, string.Join(' ', new[] { Title, Detail }.Where(text => !string.IsNullOrEmpty(text))));
    }
}
