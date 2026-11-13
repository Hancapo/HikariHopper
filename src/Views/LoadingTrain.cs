using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using HikariHopper.Ui;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Views;

/// <summary>
/// The loading overlay's busy sign: a steam engine, a 4-4-0, and its coal tender on a short track that fades out at
/// both ends. While <see cref="IsRunning"/> its wheels turn, the side and main rods drive them from a crosshead
/// sliding into the cylinder, the body rides on its springs, the sleepers run back under it and the stack puffs soft,
/// uneven smoke that drifts back and fades. With motion reduced it stands still, smoke and all.
/// </summary>
/// <remarks>
/// Drawn in a 300 by 110 frame, the rail's top 102 down. The engine faces right; the tender is coupled behind it by a
/// drawbar on the footplates' line.
/// </remarks>
public sealed class LoadingTrain : Control
{
    public static readonly StyledProperty<bool> IsRunningProperty =
        AvaloniaProperty.Register<LoadingTrain, bool>(nameof(IsRunning));

    /// <summary>Whether it moves: set while the overlay shows, so a hidden train asks for no frames.</summary>
    public bool IsRunning
    {
        get => GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    /// <summary>The frame's size.</summary>
    public const double FrameWidth = 300, FrameHeight = 110;

    // Where the tender and the engine stand, and the rail's top.
    private const double TenderX = 61, EngineX = TenderX + 48, TrainY = 42, RailY = 102;

    // A turn of the wheels, the body's rise and fall, a sleeper's pitch and how long one takes to pass, a puff's life.
    private const double WheelTurn = .6, Bob = .3, SleeperPitch = 14, SleeperRun = .42, PuffLife = 1.6;

    // The crank pins' throw, the main rod's length and the crosshead's line.
    private const double Throw = 6, MainRod = 30, CrossheadY = 47.2;

    // The moment a still train shows: every puff part way up.
    private const double StillMoment = 1.0;

    private static readonly IPen Outline = new ImmutablePen(T.Border, 1.4, lineJoin: PenLineJoin.Round);
    private static readonly IPen Ring = new ImmutablePen(T.Border, 1.2);
    private static readonly IPen Slat = new ImmutablePen(T.Border, 1.1);
    private static readonly IPen Lump = new ImmutablePen(T.WindowChrome, 1.6);
    private static readonly IPen Rim = new ImmutablePen(T.TextFaint, 1.8);
    private static readonly IPen Spoke = new ImmutablePen(T.TextFaint, 1.1);
    private static readonly IPen MainRodPen = new ImmutablePen(T.TextFaint, 2.4, lineCap: PenLineCap.Round);
    private static readonly IPen PistonRod = new ImmutablePen(T.TextFaint, 1.6);

    // The engine's shapes, in its own units (its wheels' bottoms at 60).
    private static readonly Geometry Cab = Geometry.Parse("M10 14 H42 V38 H10 Z");
    private static readonly Geometry Roof = Geometry.Parse("M5 15.5 Q26 2.5 47 15.5 L47 17.5 Q26 6.5 5 17.5 Z");
    private static readonly Geometry FrontWindow = Geometry.Parse("M15 28 V20 Q20.5 15 26 20 V28 Z");
    private static readonly Geometry RearWindow = Geometry.Parse("M30 28 V21 Q34 17 38 21 V28 Z");
    private static readonly Geometry Barrel = Geometry.Parse("M42 20 H101 V38 H42 Z");
    private static readonly Geometry Bell = Geometry.Parse("M50 20 L51 16.5 Q53.5 13 56 16.5 L57 20 Z");
    private static readonly Geometry SteamDome = Geometry.Parse("M65 20 V16 Q65 11 70.5 11 Q76 11 76 16 V20 Z");
    private static readonly Geometry SandDome = Geometry.Parse("M85 20 V17 Q85 13.5 89 13.5 Q93 13.5 93 17 V20 Z");
    private static readonly Geometry Smokebox = Geometry.Parse("M100 18 H110 Q117.5 28 110 38 H100 Z");
    private static readonly Geometry DoorRing = Geometry.Parse("M111.5 21.5 Q115.6 28 111.5 34.5");
    private static readonly Geometry Stack = Geometry.Parse("M100.5 18 L102 10.5 L98.8 4 H111.2 L108 10.5 L109.5 18 Z");
    private static readonly Geometry Pilot = Geometry.Parse("M110.5 44 H118.5 L129 59.4 H110.5 Z");

    // The tender's.
    private static readonly Geometry TenderBody = Geometry.Parse("M0 18 H40 V38 H0 Z");
    private static readonly Geometry TenderRim = Geometry.Parse("M-2 15 H42 L40 18.5 H0 Z");
    private static readonly Geometry Coal = Geometry.Parse("M2 15 L5 10 L9 11.5 L13 6.5 L18 9 L23 5 L28 8.5 L33 7.5 L37 11 L38 15 Z");
    private static readonly Geometry CoalLumps = Geometry.Parse("M9 11.5 L12 13 M18 9 L20 11 M28 8.5 L27 11");

    // The track fades in over its first 22% and out over its last.
    private static readonly IImmutableBrush TrackFade = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Colors.Transparent), new ImmutableGradientStop(.22, Colors.White),
         new ImmutableGradientStop(.78, Colors.White), new ImmutableGradientStop(1, Colors.Transparent)],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(1, 0, RelativeUnit.Relative));

    // A puff's circles: solid in the middle, soft to nothing at the rim.
    private static readonly IImmutableBrush Smoke = SoftBrush(((ImmutableSolidColorBrush)T.TextDim).Color);

    /// <summary>One puff: its circles about the stack's mouth, and where it drifts to, how big, how turned and how thick.</summary>
    private sealed record Puff((double X, double Y, double R)[] Circles, Vector Drift, double Scale, double Turn, double Peak);

    // Four puffs, each its own shape and path, a quarter of a life apart.
    private static readonly Puff[] Puffs =
    [
        new([(-2, -1, 4.2), (2.5, -2.5, 3.2), (1, 2, 3.4), (-4.5, 1.5, 2.6)], new Vector(-36, -28), 2.1, -20, .75),
        new([(-1, -2, 3.8), (-4, 1, 3.0), (2, 1, 3.4), (3, -3.5, 2.4), (-1, 3.5, 2.2)], new Vector(-28, -36), 1.8, 15, .7),
        new([(-3, -2, 3.2), (1, -1, 4.4), (-4, 2.5, 2.8), (4, 2, 2.4)], new Vector(-42, -24), 2.3, -35, .8),
        new([(0, -3, 3.4), (-3, 0, 3.8), (2.5, 1.5, 3.2), (-5, 3, 2.2), (4, -2, 2.4)], new Vector(-32, -32), 1.9, 25, .7),
    ];

    private readonly Stopwatch clock = new();
    private readonly DispatcherTimer ticker;

    // While it runs with motion reduced, a slow look at the Windows setting, so turning animations back on mid-load
    // sets it going again.
    private readonly DispatcherTimer watcher;

    public LoadingTrain()
    {
        Width = FrameWidth;
        Height = FrameHeight;
        // Made stopped (the constructor that takes a callback starts its timer), and started only while the train is
        // shown and running: a train never shown asks for nothing, and isn't kept alive by them.
        ticker = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        ticker.Tick += (_, _) => InvalidateVisual();
        watcher = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        watcher.Tick += (_, _) => WatchMotion();
    }

    /// <summary>Set by tests to draw the train at a moment of their choosing, in seconds.</summary>
    internal double? TimeOverride { get; set; }

    /// <summary>Whether it is asking for frames.</summary>
    internal bool Animating => ticker.IsEnabled;

    /// <summary>Whether it is waiting for motion to be turned back on.</summary>
    internal bool Watching => watcher.IsEnabled;

    /// <summary>The watcher's look: once motion is back on, the train moves again.</summary>
    internal void WatchMotion()
    {
        if (!Motion.Reduced)
            Restart();
    }

    /// <summary>The moment it draws: the clock while it moves, else a still moment with smoke in the air.</summary>
    private double Time => TimeOverride ?? (ticker.IsEnabled ? clock.Elapsed.TotalSeconds : StillMoment);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsRunningProperty)
            Restart();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Restart();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ticker.Stop();
        watcher.Stop();
    }

    private void Restart()
    {
        bool shown = IsRunning && VisualRoot is not null;
        bool moving = shown && !Motion.Reduced;
        if (moving && !ticker.IsEnabled)
        {
            clock.Restart();
            ticker.Start();
        }
        else if (!moving)
        {
            ticker.Stop();
        }
        watcher.IsEnabled = shown && !moving;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        // Motion turned off in Windows mid-load stills the train at once, and the watcher waits for it to come back.
        if (ticker.IsEnabled && Motion.Reduced)
        {
            ticker.Stop();
            watcher.Start();
        }
        double t = Time;
        DrawTrack(context, t);
        using (context.PushTransform(Matrix.CreateTranslation(TenderX, TrainY)))
            DrawTender(context, t);
        using (context.PushTransform(Matrix.CreateTranslation(EngineX, TrainY)))
            DrawEngine(context, t);
    }

    private static void DrawTrack(DrawingContext context, double t)
    {
        var track = new Rect(0, RailY, FrameWidth, 6);
        using (context.PushOpacityMask(TrackFade, track))
        using (context.PushClip(track))
        {
            context.FillRectangle(T.Border, new Rect(0, RailY, FrameWidth, 2));
            // The sleepers run back under the train, a pitch each run.
            double offset = -(t % SleeperRun) / SleeperRun * SleeperPitch;
            for (double x = offset; x < FrameWidth; x += SleeperPitch)
                context.FillRectangle(T.ChromeBg, new Rect(x, RailY + 2, 8, 4));
        }
    }

    private static void DrawTender(DrawingContext context, double t)
    {
        context.DrawGeometry(T.ChromeBg, Outline, TenderBody);
        context.DrawGeometry(T.Border, null, TenderRim);
        context.DrawGeometry(T.InsetBg, null, Coal);
        context.DrawGeometry(null, Lump, CoalLumps);
        context.FillRectangle(T.Bevel, new Rect(1, 19.5, 38, 1.4));
        context.FillRectangle(T.Border, new Rect(0, 38, 40, 3));
        context.FillRectangle(T.Accent, new Rect(0, 41, 40, 2));
        context.FillRectangle(T.WindowChrome, new Rect(3, 44, 34, 2.4));
        // The drawbar, out to the engine.
        context.FillRectangle(T.TextFaint, new Rect(40, 38.4, 9, 2.2));
        Wheel(context, 10, 53, 7, 6, 0, t);
        Wheel(context, 30, 53, 7, 6, 0, t);
    }

    private static void DrawEngine(DrawingContext context, double t)
    {
        // The body rides a little on its springs, easing in and out.
        double phase = t % (2 * Bob) / Bob;
        double rise = phase < 1 ? phase : 2 - phase;
        double bob = .6 * (rise < .5 ? 2 * rise * rise : 1 - 2 * (1 - rise) * (1 - rise));
        using (context.PushTransform(Matrix.CreateTranslation(0, bob)))
            DrawBody(context);

        Wheel(context, 31, 49, 11, 8, Throw, t);
        Wheel(context, 58, 49, 11, 8, Throw, t);
        Wheel(context, 85, 54, 6, 6, 0, t);
        Wheel(context, 99, 54, 6, 6, 0, t);

        // The crank pins go round with the drivers; the side rod joins them, and the main rod runs from the front pin
        // to the crosshead, which slides on its guide with the piston rod into the cylinder.
        double crank = Turn(t);
        var pin = new Vector(Throw * Math.Sin(crank), -Throw * Math.Cos(crank));
        context.DrawRectangle(T.Accent, null, new Rect(31 + pin.X, 49 + pin.Y - 1.4, 27, 2.8), 1.4, 1.4);
        var front = new Point(58 + pin.X, 49 + pin.Y);
        double rise2 = front.Y - CrossheadY;
        var crosshead = new Point(front.X + Math.Sqrt(MainRod * MainRod - rise2 * rise2), CrossheadY);
        context.DrawLine(MainRodPen, front, crosshead);
        context.FillRectangle(T.TextFaint, new Rect(crosshead.X - 2.4, crosshead.Y - 2.4, 4.8, 4.8));
        context.DrawLine(PistonRod, crosshead, new Point(96, CrossheadY));

        using (context.PushTransform(Matrix.CreateTranslation(105.2, 2.4)))
            for (int index = 0; index < Puffs.Length; index++)
                DrawPuff(context, Puffs[index], t - index * PuffLife / Puffs.Length);
    }

    private static void DrawBody(DrawingContext context)
    {
        context.FillRectangle(T.Border, new Rect(1, 35, 7, 3));
        // The cab, its arched roof and windows.
        context.DrawGeometry(T.ChromeRaised, Outline, Cab);
        context.DrawGeometry(T.Border, null, Roof);
        context.DrawGeometry(T.InsetBg, Outline, FrontWindow);
        context.DrawGeometry(T.InsetBg, Outline, RearWindow);
        // The barrel, two bands and a highlight along its top.
        context.DrawGeometry(T.ChromeRaised, Outline, Barrel);
        context.FillRectangle(T.Border, new Rect(61, 20, 1.4, 18));
        context.FillRectangle(T.Border, new Rect(81, 20, 1.4, 18));
        context.FillRectangle(T.Bevel, new Rect(43, 21.4, 57, 1.4));
        context.DrawGeometry(T.ChromeBg, Outline, Bell);
        context.DrawGeometry(T.ChromeBg, Outline, SteamDome);
        context.DrawGeometry(T.ChromeBg, Outline, SandDome);
        // The smokebox's rounded face and door ring, and the flared stack.
        context.DrawGeometry(T.ChromeBg, Outline, Smokebox);
        context.DrawGeometry(null, Ring, DoorRing);
        context.DrawGeometry(T.ChromeBg, Outline, Stack);
        context.FillRectangle(T.Border, new Rect(98, 2.4, 14.4, 2.4));
        // The footplate, the accent along its valance, and the buffer beam.
        context.FillRectangle(T.Border, new Rect(2, 38, 111, 3));
        context.FillRectangle(T.Accent, new Rect(2, 41, 111, 2));
        context.DrawRectangle(T.Accent, Ring, new Rect(111, 38, 8, 6), 1, 1);
        // The crosshead's guide, the cylinder, and the slatted pilot down to the rail.
        context.FillRectangle(T.Border, new Rect(78, 46.4, 19, 1.6));
        context.DrawRectangle(T.ChromeBg, Outline, new Rect(95, 43.5, 15, 8), 3.2, 3.2);
        context.DrawGeometry(T.ChromeBg, Outline, Pilot);
        for (int slat = 0; slat < 3; slat++)
            context.DrawLine(Slat, new Point(113 + slat * 3.2, 44.6), new Point(115 + slat * 4.6, 59));
    }

    private static void DrawPuff(DrawingContext context, Puff puff, double age)
    {
        // A puff that hasn't left the stack yet isn't there.
        if (age < 0)
            return;
        double life = age % PuffLife / PuffLife;
        double eased = 1 - (1 - life) * (1 - life);
        double opacity = life < .1 ? puff.Peak * life / .1 : puff.Peak * (1 - (life - .1) / .9);
        double scale = .35 + (puff.Scale - .35) * eased;
        Matrix place = Matrix.CreateScale(scale, scale) * Matrix.CreateRotation(puff.Turn * eased * Math.PI / 180)
            * Matrix.CreateTranslation(puff.Drift * eased);
        using (context.PushOpacity(opacity))
        using (context.PushTransform(place))
            foreach (var (x, y, r) in puff.Circles)
                context.DrawEllipse(Smoke, null, new Point(x, y), r * 1.3, r * 1.3);
    }

    /// <summary>How far the wheels have turned at <paramref name="t"/>, in radians.</summary>
    private static double Turn(double t) => 2 * Math.PI * (t % WheelTurn) / WheelTurn;

    /// <summary>A round wheel with spokes and a hub, turned as far as the train has run, its crank pin <paramref name="pin"/> out.</summary>
    private static void Wheel(DrawingContext context, double x, double y, double r, int spokes, double pin, double t)
    {
        using (context.PushTransform(Matrix.CreateRotation(Turn(t)) * Matrix.CreateTranslation(x, y)))
        {
            context.DrawEllipse(T.InsetBg, Rim, new Point(0, 0), r, r);
            double reach = r - 1.7;
            for (int spoke = 0; spoke < spokes; spoke++)
            {
                double angle = 2 * Math.PI * spoke / spokes + Math.PI / spokes;
                context.DrawLine(Spoke, new Point(0, 0), new Point(reach * Math.Cos(angle), reach * Math.Sin(angle)));
            }
            double hub = Math.Max(1.7, r * .2);
            context.DrawEllipse(T.TextFaint, null, new Point(0, 0), hub, hub);
            if (pin > 0)
                context.DrawEllipse(T.Accent, null, new Point(0, -pin), 1.9, 1.9);
        }
    }

    private static IImmutableBrush SoftBrush(Color colour) => new ImmutableRadialGradientBrush(
        [new ImmutableGradientStop(0, colour), new ImmutableGradientStop(.55, colour),
         new ImmutableGradientStop(1, Color.FromArgb(0, colour.R, colour.G, colour.B))], radius: .5);
}
