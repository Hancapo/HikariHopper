using FiveFury.Rpf;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using HikariHopper.ViewModels;
using HikariHopper.Views;
using HikariHopper.Views.Controls;

namespace HikariHopper.Tests;

/// <summary>The address bar as text: copying where you are, and going to a path typed or pasted into it.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class AddressBarTests : ExplorerTestBase
{
    private static IEnumerable<string> Names(ExplorerViewModel bridge) => bridge.EntriesModel.Select(item => item.Name);

    private static IEnumerable<string> Selected(ExplorerViewModel bridge) =>
        bridge.EntriesModel.Where(item => item.Selected).Select(item => item.Name);

    private ExplorerViewModel Game()
    {
        MakeGame();
        Write(Path.Combine(Work, "x64", "levels", "notes.txt"), "n");
        RpfArchive inner = Archive("inner.rpf", ("deep/b.txt", "b"), ("top.txt", "t"));
        SaveArchive(Path.Combine(Work, "mods", "mods.rpf"), ("data/carcols.meta", "<Item />"), ("inner.rpf", inner));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        return bridge;
    }

    [AvaloniaFact]
    public void TheAddressIsRelativeToTheGameAndRunsThroughArchives()
    {
        ExplorerViewModel bridge = Game();
        Assert.Equal("", bridge.AddressText);
        bridge.Navigate("x64/levels");
        Assert.Equal(@"x64\levels", bridge.AddressText);

        Assert.True(bridge.GoToAddress(Path.Combine(Work, "mods", "mods.rpf", "inner.rpf", "deep")));
        Assert.Equal(@"mods\mods.rpf\inner.rpf\deep", bridge.AddressText);
        Assert.Equal(["b.txt"], Names(bridge));

        bridge.CopyAddress();
        Assert.Equal(@"mods\mods.rpf\inner.rpf\deep", Clipboard.Text);
        Assert.Equal("Copied address", bridge.Status);

        // What it copies goes back to the same place.
        bridge.Navigate(".");
        Assert.True(bridge.GoToAddress(Clipboard.Text!));
        Assert.Equal(["b.txt"], Names(bridge));
    }

    [AvaloniaFact]
    public void AnRpfFromOutsideTheGameStartsItsOwnAddress()
    {
        MakeGame();
        string outside = Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Work, "..", "downloads"))).FullName;
        string addon = SaveArchive(Path.Combine(outside, "addon.rpf"), ("stream/readme.txt", "addon"));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(addon);
        Assert.True(bridge.GoToAddress(@"addon.rpf\stream"));
        Assert.Equal(@"addon.rpf\stream", bridge.AddressText);
        Assert.Equal(["readme.txt"], Names(bridge));
        Assert.True(bridge.GoToAddress("addon.rpf"));
        Assert.Equal("addon.rpf", bridge.AddressText);
        Assert.False(bridge.GoToAddress(@"other.rpf\stream"));
    }

    [AvaloniaFact]
    public void FoldersAndArchivesCanBeGoneToByAnyForm()
    {
        ExplorerViewModel bridge = Game();
        // Relative to the game, with either slash.
        Assert.True(bridge.GoToAddress("x64/levels"));
        Assert.Equal("x64/levels", bridge.CurrentPath);
        Assert.True(bridge.GoToAddress(@"mods\mods.rpf\data"));
        Assert.True(bridge.InArchive);
        Assert.Equal(["carcols.meta"], Names(bridge));
        // The RPF itself, quoted as Explorer copies it.
        Assert.True(bridge.GoToAddress($"\"{Path.Combine(Work, "mods", "mods.rpf")}\""));
        Assert.Equal(["data", "inner.rpf"], Names(bridge).Order());
        // Back out to a folder of the game.
        Assert.True(bridge.GoToAddress(Path.Combine(Work, "x64")));
        Assert.False(bridge.InArchive);
        Assert.Equal("x64", bridge.CurrentPath);
    }

    [AvaloniaFact]
    public void AFileIsShownSelectedInItsFolderAndCopyPathsFormWorks()
    {
        ExplorerViewModel bridge = Game();
        Assert.True(bridge.GoToAddress(Path.Combine(Work, "x64", "levels", "notes.txt")));
        Assert.Equal("x64/levels", bridge.CurrentPath);
        Assert.Equal(["notes.txt"], Selected(bridge));
        // Copy path's form for a file in an RPF.
        Assert.True(bridge.GoToAddress($"{Path.Combine(Work, "mods", "mods.rpf")}::data/carcols.meta"));
        Assert.True(bridge.InArchive);
        Assert.Equal(["carcols.meta"], Selected(bridge));

        // A file at a nested RPF's root: that RPF opens, the file selected in it.
        Assert.True(bridge.GoToAddress(Path.Combine(Work, "mods", "mods.rpf", "inner.rpf", "top.txt")));
        Assert.Equal(@"mods\mods.rpf\inner.rpf", bridge.AddressText);
        Assert.Equal(["top.txt"], Selected(bridge));

        // What Copy path gives inside a nested RPF goes back to the same file.
        Assert.True(bridge.GoToAddress(Path.Combine(Work, "mods", "mods.rpf", "inner.rpf", "deep")));
        bridge.SelectEntry(0);
        bridge.CopySelectedPath();
        Assert.StartsWith(Path.Combine(Work, "mods", "mods.rpf") + "::", Clipboard.Text);
        bridge.Navigate(".");
        Assert.True(bridge.GoToAddress(Clipboard.Text!));
        Assert.Equal(["b.txt"], Selected(bridge));
    }

    [AvaloniaFact]
    public void APathThatCantBeShownSaysWhy()
    {
        ExplorerViewModel bridge = Game();
        Assert.False(bridge.GoToAddress("x64/nowhere"));
        Assert.StartsWith("Not found", bridge.Status);
        Assert.False(bridge.GoToAddress(@"mods\mods.rpf\missing"));
        Assert.StartsWith("Not found in mods.rpf", bridge.Status);
        string outside = Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Work, "..", "outside"))).FullName;
        Assert.False(bridge.GoToAddress(outside));
        Assert.StartsWith("Only folders inside the game", bridge.Status);
        Assert.False(bridge.GoToAddress("   "));
    }

    private (MainWindow Window, NavigationBar Navigation, ExplorerViewModel Bridge) Open()
    {
        MakeGame();
        Directory.CreateDirectory(Path.Combine(Work, "x64", "levels"));
        var session = new ExplorerSession();
        var window = new MainWindow { Width = 1100, Height = 600 };
        session.Attach(window);
        window.Show();
        ExplorerViewModel bridge = session.Tabs.ActiveBridge;
        bridge.OpenGame(Work);
        Settle(bridge);
        TestWait.Settle();
        return (window, window.GetVisualDescendants().OfType<NavigationBar>().Single(), bridge);
    }

    private static void Key(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None, PhysicalKey physical = PhysicalKey.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        TestWait.Settle();
    }

    [AvaloniaFact]
    public void CtrlLEditsTheAddressAndEnterGoesThere()
    {
        var (window, navigation, bridge) = Open();
        bridge.Navigate("x64");
        TestWait.Settle();
        Key(window, Avalonia.Input.Key.L, RawInputModifiers.Control, PhysicalKey.L);
        Assert.True(navigation.EditingAddress);
        Assert.Equal("x64", navigation.AddressField.Text);
        Assert.Equal((0, navigation.AddressField.Text!.Length), (navigation.AddressField.SelectionStart, navigation.AddressField.SelectionEnd));

        // A path that isn't there stays to be fixed, marked.
        navigation.AddressField.Text = "x64/nowhere";
        Key(window, Avalonia.Input.Key.Enter, physical: PhysicalKey.Enter);
        Assert.True(navigation.EditingAddress);
        Assert.True(navigation.AddressField.Invalid);

        navigation.AddressField.Text = "x64/levels";
        Key(window, Avalonia.Input.Key.Enter, physical: PhysicalKey.Enter);
        Assert.False(navigation.EditingAddress);
        Assert.Equal("x64/levels", bridge.CurrentPath);

        // Esc puts the path back without going anywhere.
        Key(window, Avalonia.Input.Key.L, RawInputModifiers.Control, PhysicalKey.L);
        navigation.AddressField.Text = "x64";
        Key(window, Avalonia.Input.Key.Escape, physical: PhysicalKey.Escape);
        Assert.False(navigation.EditingAddress);
        Assert.True(navigation.AddressBar.IsVisible);
        Assert.Equal("x64/levels", bridge.CurrentPath);
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingTheBarsEmptyPartEditsAndARightClickOffersCopyAndPaste()
    {
        var (window, navigation, bridge) = Open();
        bridge.Navigate("x64");
        TestWait.Settle();
        Border bar = navigation.AddressBar;
        Point empty = bar.TranslatePoint(new Point(bar.Bounds.Width - 60, bar.Bounds.Height / 2), window)!.Value;

        window.MouseDown(empty, MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(empty, MouseButton.Right, RawInputModifiers.None);
        TestWait.Settle();
        RetroMenu menu = Assert.IsType<RetroMenu>(navigation.AddressMenu);
        RetroMenuItem[] items = [.. menu.Items.OfType<RetroMenuItem>()];
        Assert.Equal(["Copy address", "Edit address", "Paste and go"], items.Select(item => item.Text));
        items[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
        Assert.Equal("x64", Clipboard.Text);
        menu.Close();

        // Paste and go: a path from the clipboard.
        Clipboard.SetTextAsync(Path.Combine(Work, "x64", "levels"));
        window.MouseDown(empty, MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(empty, MouseButton.Right, RawInputModifiers.None);
        TestWait.Settle();
        navigation.AddressMenu!.Items.OfType<RetroMenuItem>().Last()
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
        TestWait.Settle();
        Assert.Equal("x64/levels", bridge.CurrentPath);
        navigation.AddressMenu?.Close();

        // A click on the empty part edits.
        TestWait.Settle();
        window.MouseDown(empty, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(empty, MouseButton.Left, RawInputModifiers.None);
        TestWait.Settle();
        Assert.True(navigation.EditingAddress);
        Assert.Equal(@"x64\levels", navigation.AddressField.Text);
        window.Close();
    }
}
