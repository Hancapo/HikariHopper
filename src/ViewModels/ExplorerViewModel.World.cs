using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using HikariHopper.Core;

namespace HikariHopper.ViewModels;

/// <summary>Which side of its game a tab shows: its files, or its world.</summary>
public enum TabFace
{
    Files,
    World,
}

/// <summary>Where a world's camera is: its eye, in the world's coordinates, and where it looks.</summary>
/// <param name="Yaw">Its azimuth, in radians.</param>
/// <param name="Pitch">Its elevation, in radians: above zero looks down.</param>
public readonly record struct WorldSpot(Vector3 Eye, float Yaw, float Pitch)
{
    /// <summary>The eye's coordinates as the address row shows them: "-300.00, 740.00, 132.00".</summary>
    public string Coordinates =>
        string.Create(CultureInfo.InvariantCulture, $"{Eye.X:F2}, {Eye.Y:F2}, {Eye.Z:F2}");

    /// <summary>How a spot is kept in the settings: its five numbers.</summary>
    internal string Stored =>
        string.Create(CultureInfo.InvariantCulture, $"{Eye.X:R},{Eye.Y:R},{Eye.Z:R},{Yaw:R},{Pitch:R}");

    /// <summary>A spot as <see cref="Stored"/> keeps it; null for anything else.</summary>
    internal static WorldSpot? Parse(string stored)
    {
        string[] parts = stored.Split(',');
        if (parts.Length != 5)
            return null;
        var numbers = new float[5];
        for (int i = 0; i < 5; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) || !float.IsFinite(numbers[i]))
                return null;
        return new WorldSpot(new Vector3(numbers[0], numbers[1], numbers[2]), numbers[3], numbers[4]);
    }
}

/// <summary>
/// A placement picked in the world, as its menu shows it: its archetype's name, its map file and entity, the files of
/// its model and its map (null where they aren't found), and where it stands.
/// </summary>
internal sealed record WorldPlacement(string Name, string Detail, WorldFile? Model, WorldFile? Map, Vector3 Position)
{
    /// <summary>Where it stands, as the address row writes coordinates.</summary>
    public string Coordinates => string.Create(CultureInfo.InvariantCulture, $"{Position.X:F2}, {Position.Y:F2}, {Position.Z:F2}");
}

/// <summary>
/// A stop in a tab's Back and Forward history: a folder of its files (<paramref name="Path"/>, as the tab names it), or
/// its world at a camera spot, with the folder the files are left at.
/// </summary>
internal readonly record struct HistoryStop(string Path, WorldSpot? World = null);

/// <summary>
/// A game's tab has two sides: its files, and, for a game whose world HikariHopper shows, its world. The tab strip, the
/// address row and the status bar stay; what's under the address row is one or the other, and Back and Forward go
/// through both. The world's camera starts at the game's landmark, then where it was left in that installation.
/// </summary>
public sealed partial class ExplorerViewModel
{
    private TabFace face;
    // Where this tab's world camera is; null until it's been shown (then the installation's, or the landmark).
    private WorldSpot? worldSpot;
    private string worldStatus = "";

    private static readonly string[] FaceProperties =
        [nameof(Face), nameof(InWorld), nameof(TabTitle), nameof(CanGoBack), nameof(CanGoForward), nameof(CanGoUp), nameof(WorldCoordinates)];

    /// <summary>The side the tab shows.</summary>
    public TabFace Face => face;

    /// <summary>Whether the tab shows its game's world.</summary>
    public bool InWorld => face == TabFace.World;

    /// <summary>Where the game's world camera starts the first time; null for a game whose world isn't shown, or no game.</summary>
    public WorldLandmark? WorldLandmark => Provider.HasGame ? GameCatalog.Edition(Provider.GameEditionId)?.Game.Landmark : null;

    /// <summary>Whether the tab's game has a world to show.</summary>
    public bool HasWorld => WorldLandmark is not null;

    /// <summary>Where the world's camera is, or goes when it's shown: where it was left, or the game's landmark.</summary>
    public WorldSpot WorldSpot => worldSpot ?? StoredSpot() ?? (WorldLandmark is { } landmark
        ? new WorldSpot(landmark.Eye, landmark.Yaw, landmark.Pitch) : default);

    /// <summary>The camera's coordinates, as the address row shows them on the World side.</summary>
    public string WorldCoordinates => InWorld ? WorldSpot.Coordinates : "";

    // The tab's game's world, while it has one: its index, made or being made.
    private GameWorld? world;

    private static readonly string[] WorldIndexProperties =
        [nameof(WorldReady), nameof(WorldPreparing), nameof(WorldStage), nameof(WorldProgress), nameof(WorldStatus), nameof(WorldFailure)];

    /// <summary>Whether the world's index is ready, so the world can be shown.</summary>
    public bool WorldReady => world?.Index is not null;

    /// <summary>
    /// Whether the world's index is still being made (or read from the cache), or couldn't be: its loading screen shows
    /// meanwhile, saying why when it couldn't.
    /// </summary>
    public bool WorldPreparing => world is not null && world.Index is null;

    /// <summary>Why the world's index couldn't be made; null while it's being made, or once it is.</summary>
    public string? WorldFailure => world?.Failure;

    /// <summary>What making the world's index is doing, as its loading screen says it.</summary>
    public string WorldStage => world?.Progress.Stage ?? "";

    /// <summary>How far making the world's index has got, 0 to 1; null while that isn't known.</summary>
    public double? WorldProgress => world?.Progress.Fraction;

    /// <summary>The world's index, once it's ready.</summary>
    internal WorldIndex? WorldIndex => world?.Index;

    /// <summary>
    /// Starts the tab's game's world on its way, in the background, when the game has one: its index is made, or read
    /// from the cache, as the game opens, so it's ready (or nearly) by the time the world is shown.
    /// </summary>
    private void PrepareWorld()
    {
        DetachWorld();
        if (!HasWorld || Provider.Keys is not { } keys)
            return;
        world = GameWorld.ForGame(Provider.GamePath, keys);
        world.ProgressChanged += OnWorldProgress;
        GameWorld started = world;
        _ = started.Load().ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (world == started)
                PublishWorldIndex();
        }), TaskScheduler.Default);
        PublishWorldIndex();
    }

    private void DetachWorld()
    {
        if (world is not null)
            world.ProgressChanged -= OnWorldProgress;
        world = null;
    }

    // Raised on the thread making the index: told on the UI thread.
    private void OnWorldProgress(WorldProgress progress) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(PublishWorldIndex);

    private void PublishWorldIndex()
    {
        if (world?.Index is { } index)
            worldStatus = string.Create(CultureInfo.CurrentCulture, $"{index.MapCount:N0} maps · {index.Placements:N0} placements");
        else if (world?.Failure is not null)
            worldStatus = "The world couldn't be prepared";
        else if (world is not null)
            worldStatus = "Preparing the world";
        RaiseAll(WorldIndexProperties);
    }

    /// <summary>
    /// A picked placement as its menu shows it: its archetype's name (its model's file's, or its dictionary member's),
    /// "&lt;map file&gt; · entity N" (and which instance, for a batch's), and the files of its model and map.
    /// </summary>
    internal WorldPlacement? DescribePlacement(Rendering.WorldPick pick)
    {
        if (WorldIndex is not { } index)
            return null;
        WorldFile? model = index.Locate(pick.Asset.Hash, pick.Asset.Extension);
        WorldFile? map = index.Locate(pick.Node.Id.Map, 73);
        string name = pick.Asset.Member != 0 ? index.NameOf(pick.Asset.Member) : model?.Title ?? index.NameOf(pick.Asset.Hash);
        string where = map?.Name ?? $"{index.NameOf(pick.Node.Id.Map)}.ymap";
        string entity = index.PlacementDetail(pick.Node) ?? $"entity {pick.Node.Id.Index}";
        string detail = pick.Instance >= 0 ? $"{where} · {entity} · instance {pick.Instance}" : $"{where} · {entity}";
        return new WorldPlacement(name, detail, model, map, pick.Position);
    }

    /// <summary>Opens a placement's model in the 3D viewer, its textures found as from its folder.</summary>
    internal void OpenWorldModel(WorldFile model) => OpenHit(new Core.Search.SearchHit(model.Path, model.Size, []), open: true);

    /// <summary>
    /// Shows one of the world's files in its folder, selected, on the Files side: one more stop in Back and Forward,
    /// which returns to the world where its camera was.
    /// </summary>
    internal void ShowWorldFile(WorldFile file)
    {
        HistoryStop? from = InWorld && historyIndex >= 0 && historyIndex < history.Count ? history[historyIndex] : null;
        if (!GoToAddress(file.Path))
            return;
        if (InWorld)
            SetFace(TabFace.Files);
        // Entering an RPF starts the history again: the world stays the stop before it, where Back returns.
        if (from is { World: not null } world && !history.Any(stop => stop.World is not null))
        {
            history = [world with { Path = currentPath }, history[historyIndex]];
            historyIndex = 1;
            RaiseAll(FaceProperties);
        }
    }

    /// <summary>Copies a placement's name or coordinates, saying so.</summary>
    internal void CopyWorldText(string text, string what)
    {
        _ = Infrastructure.AppServices.Clipboard.SetTextAsync(text);
        SetStatus($"Copied {what}");
    }

    private float worldHours = Rendering.DayCycle.DefaultHours;

    /// <summary>The time of day the world is lit at, in hours.</summary>
    public float WorldHours => worldHours;

    /// <summary>Lights the world as the game does at <paramref name="hours"/>, 0 to 24: the address row's time.</summary>
    public void SetWorldHours(float hours)
    {
        if (!float.IsFinite(hours) || worldHours == hours)
            return;
        worldHours = hours;
        OnPropertyChanged(nameof(WorldHours));
    }

    /// <summary>What the world shows, as the status bar says it on the World side.</summary>
    public string WorldStatus => worldStatus;

    /// <summary>Asks the world view to put its camera at <see cref="WorldSpot"/>: Back or Forward to a World stop.</summary>
    public event EventHandler? WorldSpotRequested;

    // Whether the game being loaded opens on its World side.
    private bool worldAsked;

    /// <summary>
    /// Loads a game in the background, as the start page's row does, and shows its world once it's open: the row's
    /// globe. False while another operation runs.
    /// </summary>
    public bool OpenGameOnWorld(string path)
    {
        if (!OpenGameAsync(path))
            return false;
        worldAsked = true;
        return true;
    }

    /// <summary>Shows the game's world, where its camera was left: one more stop in Back and Forward.</summary>
    public void ShowWorld()
    {
        if (!HasWorld || InWorld || EntryOperationBusy)
            return;
        // The search is the files'; the world has its own address.
        if (searching)
            EndSearch();
        Push(new HistoryStop(currentPath, WorldSpot));
        SetFace(TabFace.World);
    }

    /// <summary>Shows the game's files, where they were left: one more stop in Back and Forward.</summary>
    public void ShowFiles()
    {
        if (!InWorld || EntryOperationBusy)
            return;
        Push(new HistoryStop(currentPath));
        SetFace(TabFace.Files);
    }

    /// <summary>Flips to the other side, as F6 does.</summary>
    public void ToggleFace()
    {
        if (InWorld)
            ShowFiles();
        else
            ShowWorld();
    }

    /// <summary>
    /// Keeps where the world's camera is, as it moves: the stop Back and Forward return to, and, for the installation,
    /// where the world opens next time.
    /// </summary>
    public void RememberWorldSpot(WorldSpot spot)
    {
        if (worldSpot == spot)
            return;
        worldSpot = spot;
        if (historyIndex >= 0 && historyIndex < history.Count && history[historyIndex].World is not null)
            history[historyIndex] = history[historyIndex] with { World = spot };
        OnPropertyChanged(nameof(WorldCoordinates));
    }

    /// <summary>What the status bar says on the World side; set by the world view.</summary>
    public void SetWorldStatus(string value)
    {
        if (worldStatus == value)
            return;
        worldStatus = value;
        OnPropertyChanged(nameof(WorldStatus));
    }

    private void Push(HistoryStop stop)
    {
        history = history.Take(historyIndex + 1).ToList();
        history.Add(stop);
        historyIndex++;
    }

    private void SetFace(TabFace value)
    {
        if (face == value)
            return;
        // Leaving the world: where its camera is, for the installation.
        if (face == TabFace.World)
            StoreSpot();
        face = value;
        RaiseAll(FaceProperties);
    }

    /// <summary>Goes to a stop of Back and Forward: a folder of the files, or the world at its camera spot.</summary>
    private void GoTo(HistoryStop stop)
    {
        currentPath = stop.Path;
        if (stop.World is { } spot)
        {
            worldSpot = spot;
            SetFace(TabFace.World);
            WorldSpotRequested?.Invoke(this, EventArgs.Empty);
            RaiseAll(FaceProperties);
            return;
        }
        SetFace(TabFace.Files);
        ExpandCurrentBranch();
        PublishNavigation();
    }

    // Whether the tab still shows the game its world is: entering one of its RPFs keeps the world.
    private bool KeepsWorld() => world is not null && Provider.HasGame
        && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Provider.GamePath)), world.Root, StringComparison.OrdinalIgnoreCase);

    // On the Files side, without a stop of its own: the caller's navigation makes it.
    private void FacesFiles()
    {
        if (face == TabFace.World)
            SetFace(TabFace.Files);
    }

    /// <summary>Back to the files, the world forgotten: another game, an archive on its own, or none.</summary>
    private void LeaveWorld()
    {
        if (face == TabFace.World)
            StoreSpot();
        face = TabFace.Files;
        worldSpot = null;
        DetachWorld();
        RaiseAll(FaceProperties);
        RaiseAll(WorldIndexProperties);
        OnPropertyChanged(nameof(HasWorld));
    }

    // Where the installation's world camera was left, kept by its folder.
    private string SpotKey => $"worldSpots/{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Provider.GamePath.ToUpperInvariant())))[..16]}";

    private WorldSpot? StoredSpot() => HasWorld ? WorldSpot.Parse(AppSettings.GetString(SpotKey)) : null;

    private void StoreSpot()
    {
        if (HasWorld && worldSpot is { } spot)
            AppSettings.SetString(SpotKey, spot.Stored);
    }
}
