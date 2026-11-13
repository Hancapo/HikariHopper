using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HikariHopper.Core;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>What the prompt before closing was answered with.</summary>
public enum UnsavedChoice
{
    Cancel,
    Discard,
    Save,
}

/// <summary>
/// Asks, before the app or a tab closes, whether to save every file it has unsaved: the files are listed with where
/// each is open. Save all keeps it up, its buttons off, while they are written, and whoever asked closes it then;
/// Don't save and Cancel (the close mark, Escape) close it at once.
/// </summary>
public partial class UnsavedFilesDialog : UserControl, IModalContent
{
    /// <summary>How tall a file's row is.</summary>
    public const double RowHeight = 34;

    /// <summary>The rows the list shows before it scrolls.</summary>
    public const int VisibleRows = 8;

    private readonly TaskCompletionSource<UnsavedChoice> answer = new();
    private ModalOverlay? overlay;
    private bool saving;

    /// <summary>An empty prompt, for the XAML loader and previews.</summary>
    public UnsavedFilesDialog() : this([], "HikariHopper closes")
    {
    }

    /// <param name="files">The files unsaved, in the order they are listed.</param>
    /// <param name="closing">What closes once they are saved or let go, as the question ends: "the tab closes".</param>
    public UnsavedFilesDialog(IReadOnlyList<UnsavedFile> files, string closing)
    {
        Files = files;
        InitializeComponent();
        HeadingText.Text = files.Count == 1 ? "1 FILE HAS UNSAVED CHANGES" : $"{files.Count} FILES HAVE UNSAVED CHANGES";
        QuestionText.Text = $"Save {(files.Count == 1 ? "it" : "them")} before {closing}?";
        // Rows past the first few scroll, so the prompt never outgrows the window.
        ListScroller.MaxHeight = VisibleRows * RowHeight;
        for (int i = 0; i < files.Count; i++)
            FileRows.Children.Add(Row(files[i], seam: i > 0));
        CancelButton.Click += (_, _) => Answer(UnsavedChoice.Cancel);
        CloseMark.Click += (_, _) => Answer(UnsavedChoice.Cancel);
        DiscardButton.Click += (_, _) => Answer(UnsavedChoice.Discard);
        SaveButton.Click += (_, _) => Answer(UnsavedChoice.Save);
    }

    /// <summary>The files asked about.</summary>
    public IReadOnlyList<UnsavedFile> Files { get; }

    public bool IsOpen => overlay?.IsShown == true;

    /// <summary>Whether the files are being saved: the buttons are off meanwhile, and Escape does nothing.</summary>
    public bool Saving => saving;

    /// <summary>Shows the prompt over the window <paramref name="anchor"/> is in; done once it is answered.</summary>
    public Task<UnsavedChoice> Ask(Control anchor)
    {
        overlay = ModalOverlay.Show(anchor, this);
        if (overlay is null)
            answer.TrySetResult(UnsavedChoice.Cancel);
        else
            overlay.Closed += (_, _) =>
            {
                overlay = null;
                // Closed by anything but its buttons (its window going, say), nothing is to be done.
                answer.TrySetResult(UnsavedChoice.Cancel);
            };
        return answer.Task;
    }

    public void Close() => overlay?.Close();

    /// <summary>Escape: Cancel, unless the files are being saved.</summary>
    void IModalContent.RequestClose() => Answer(UnsavedChoice.Cancel);

    private void Answer(UnsavedChoice choice)
    {
        if (saving || answer.Task.IsCompleted)
            return;
        if (choice == UnsavedChoice.Save)
        {
            saving = true;
            CancelButton.IsEnabled = false;
            CloseMark.IsEnabled = false;
            DiscardButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
        }
        answer.TrySetResult(choice);
        if (choice != UnsavedChoice.Save)
            Close();
    }

    /// <summary>A file's row: its icon in its kind's ink, as the explorer lists it, its name, and where it is open.</summary>
    private static Control Row(UnsavedFile file, bool seam)
    {
        var icon = new LucideIcon
        {
            IconName = "file",
            Width = 16,
            Height = 16,
            Stroke = InkFor(file.Name),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var name = new TextBlock
        {
            Text = file.Name,
            Foreground = Ui.Theme.Text,
            FontFamily = Ui.Theme.UiFont,
            FontSize = Ui.Theme.FontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var where = new TextBlock
        {
            Text = file.Where,
            Margin = new Thickness(12, 0, 0, 0),
            Foreground = Ui.Theme.TextFaint,
            FontFamily = Ui.Theme.MonoFont,
            FontSize = Ui.Theme.SmallFontSize,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 2);
        Grid.SetColumn(where, 3);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("16,10,*,Auto"), Margin = new Thickness(8, 0) };
        row.Children.Add(icon);
        row.Children.Add(name);
        row.Children.Add(where);
        return new Border
        {
            Height = RowHeight,
            BorderBrush = Ui.Theme.BorderHard,
            BorderThickness = new Thickness(0, seam ? 1 : 0, 0, 0),
            Child = row,
        };
    }

    /// <summary>The ink the explorer gives a file of this name: data for text and metadata, asset for models and textures.</summary>
    internal static IBrush InkFor(string name) =>
        FileGlyph.TintFor(FileGlyph.Resolve("file", RpfProvider.FileKind(name)).Family, selected: false);
}
