using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Covers the tab while a long operation runs (an import, an extract, a delete, a write into an RPF) with the common
/// loading overlay: what it is, the file in hand, a bar counting the files or the bytes written, and Cancel when it
/// can stop. It appears once the operation has run <see cref="RevealDelay"/>, so a quick one doesn't flash it.
/// </summary>
public sealed class ExplorerWorkView : UserControl
{
    private readonly DispatcherTimer reveal;
    private ExplorerViewModel? bridge;

    public ExplorerWorkView()
    {
        IsVisible = false;
        ZIndex = 9;
        Overlay = new LoadingOverlay();
        Overlay.CancelRequested += (_, _) => bridge?.CancelWork();
        Content = Overlay;
        reveal = new DispatcherTimer { Interval = RevealDelay };
        reveal.Tick += (_, _) =>
        {
            reveal.Stop();
            Refresh(revealNow: true);
        };
    }

    /// <summary>How long an operation runs before the overlay shows it; tests set it.</summary>
    internal static TimeSpan RevealDelay { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>The overlay.</summary>
    internal LoadingOverlay Overlay { get; }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (bridge is not null)
            bridge.PropertyChanged -= OnBridgePropertyChanged;
        bridge = DataContext as ExplorerViewModel;
        if (bridge is not null)
            bridge.PropertyChanged += OnBridgePropertyChanged;
        // Another tab: its own operation, if it has one, shows at once.
        Hide();
        Refresh(revealNow: bridge?.Work is not null);
    }

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerViewModel.Work))
            Refresh(revealNow: false);
    }

    private void Hide()
    {
        reveal.Stop();
        IsVisible = false;
        Overlay.IsRunning = false;
        Overlay.CanCancel = false;
    }

    private void Refresh(bool revealNow)
    {
        if (bridge?.Work is not { } work)
        {
            Hide();
            return;
        }
        Overlay.Title = work.Title;
        Overlay.Detail = work.Current;
        Overlay.Progress = work.Fraction;
        Overlay.ProgressText = work.Count;
        AutomationProperties.SetName(this, $"{work.Title} {work.Current}");
        if (IsVisible)
            return;
        if (revealNow || RevealDelay <= TimeSpan.Zero)
        {
            Overlay.CanCancel = work.Cancellable;
            IsVisible = true;
            Overlay.IsRunning = true;
        }
        else if (!reveal.IsEnabled)
        {
            reveal.Interval = RevealDelay;
            reveal.Start();
        }
    }
}
