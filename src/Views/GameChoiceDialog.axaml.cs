using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HikariHopper.Core;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Asks which game a folder opens with, when there's no game to go by: one row per game set up, the first chosen.
/// A click picks a row, a double-click or Enter opens with it, Up and Down move between them. <see cref="Open"/> shows
/// it; it closes itself once answered.
/// </summary>
public partial class GameChoiceDialog : UserControl
{
    private readonly IReadOnlyList<(GameEditionInfo Edition, string Root)> games;
    private readonly List<Border> rows = [];
    private ModalOverlay? overlay;
    private int chosen;

    public GameChoiceDialog() : this("", [])
    {
    }

    /// <param name="folder">What the header names: the folder's name, or how many there are.</param>
    /// <param name="games">The games to choose from, with their folders.</param>
    public GameChoiceDialog(string folder, IReadOnlyList<(GameEditionInfo Edition, string Root)> games)
    {
        this.games = games;
        InitializeComponent();
        FolderText.Text = folder;
        for (int index = 0; index < games.Count; index++)
            rows.Add(Row(index, games[index].Edition));
        Rows.Children.AddRange(rows);
        Focusable = true;
        KeyDown += OnKeyDown;
        Refresh();
    }

    /// <summary>Raised with the game's folder once a game is chosen, before it closes.</summary>
    public event EventHandler<string>? Chosen;

    /// <summary>The row chosen now.</summary>
    public int ChosenIndex => chosen;

    /// <summary>The rows, one per game, for tests.</summary>
    internal IReadOnlyList<Border> GameRows => rows;

    public void Open(Control anchor)
    {
        overlay = ModalOverlay.Show(anchor, this);
        Focus();
    }

    public void Close() => overlay?.Close();

    /// <summary>Picks a row, without opening.</summary>
    public void Choose(int index)
    {
        if (index < 0 || index >= games.Count)
            return;
        chosen = index;
        Refresh();
    }

    /// <summary>Opens with the chosen game, and closes.</summary>
    public void Answer()
    {
        if (chosen >= 0 && chosen < games.Count)
            Chosen?.Invoke(this, games[chosen].Root);
        Close();
    }

    private Border Row(int index, GameEditionInfo edition)
    {
        var row = new Border
        {
            Height = 36,
            CornerRadius = Ui.Theme.InnerCorner,
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new EditionMark { Edition = edition.Id, Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock
                    {
                        Text = edition.FullName,
                        FontFamily = Ui.Theme.UiFont,
                        FontSize = Ui.Theme.FontSize,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        AutomationProperties.SetName(row, edition.FullName);
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                return;
            Choose(index);
            e.Handled = true;
            if (e.ClickCount % 2 == 0)
                Answer();
        };
        row.PointerEntered += (_, _) => Refresh();
        row.PointerExited += (_, _) => Refresh();
        return row;
    }

    private void Refresh()
    {
        for (int index = 0; index < rows.Count; index++)
        {
            Border row = rows[index];
            bool on = index == chosen;
            // The chosen row raised out of the well; another under the pointer a shade above it.
            row.Background = on ? Ui.Theme.ChromeRaised : row.IsPointerOver ? Ui.Theme.HoverBg : Brushes.Transparent;
            if (row.Child is StackPanel { Children: [_, TextBlock text] })
            {
                text.Foreground = on ? Ui.Theme.Text : Ui.Theme.TextDim;
                text.FontWeight = on ? FontWeight.Bold : FontWeight.Normal;
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                Choose(Math.Max(0, chosen - 1));
                e.Handled = true;
                break;
            case Key.Down:
                Choose(Math.Min(games.Count - 1, chosen + 1));
                e.Handled = true;
                break;
            case Key.Enter:
                Answer();
                e.Handled = true;
                break;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnOpen(object? sender, RoutedEventArgs e) => Answer();
}
