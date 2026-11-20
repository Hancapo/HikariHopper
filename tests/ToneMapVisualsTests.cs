using FiveFury;
using HikariHopper.Core;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class ToneMapVisualsTests
{
    // visualsettings.dat's tone map and exposure lines, as both editions' files have them.
    private const string FileText = """
        # Tonemapping
        Tonemapping.bright.filmic.A			0.22
        Tonemapping.bright.filmic.B			0.3
        Tonemapping.bright.filmic.C			0.10
        Tonemapping.bright.filmic.D			0.2
        Tonemapping.bright.filmic.E			0.01
        Tonemapping.bright.filmic.F			0.3
        Tonemapping.bright.filmic.W			4.0
        Tonemapping.bright.filmic.exposure	-0.5

        Tonemapping.dark.filmic.A			0.22
        Tonemapping.dark.filmic.B			0.3
        Tonemapping.dark.filmic.C			0.10
        Tonemapping.dark.filmic.D			0.2
        Tonemapping.dark.filmic.E			0.0
        Tonemapping.dark.filmic.F			0.3
        Tonemapping.dark.filmic.W			4.0
        Tonemapping.dark.filmic.exposure	3.0

        # Exposure
        Exposure.curve.scale	-106.200
        Exposure.curve.power	0.007
        Exposure.curve.offset	104.700
        """;

    /// <summary>The tone map the game's own file gives, in both editions.</summary>
    internal static GtaToneMapVisuals GameFile { get; } = GtaToneMapVisuals.From(GameVisualSettings.Parse(FileText));

    private static readonly GtaToneMapVisuals Expected = new(-106.2f, .007f, 104.7f,
        new Filmic(.22f, .3f, .1f, .2f, .01f, .3f, 4), -.5f, new Filmic(.22f, .3f, .1f, .2f, 0, .3f, 4), 3);

    [Fact]
    public void The_tone_map_and_exposure_curve_are_read_from_visualsettings()
    {
        Assert.Equal(Expected, GameFile);
        // The dark curve's toe goes from 0.01 to nought, past the bright one at an exposure of -0.5, all of it at 3.
        Assert.Equal((1 / 3.5f, .5f / 3.5f), GameFile.Blend);
        Assert.Equal(GameFile.Bright, GameFile.FilmicFor(-.5f));
        Assert.Equal(.005f, GameFile.FilmicFor(1.25f).E, 6);
        Assert.Equal(GameFile.Dark, GameFile.FilmicFor(3));
    }

    [Fact]
    public void Without_a_file_each_setting_is_the_games_code_default()
    {
        // The defaults the game's code reads each setting with.
        var defaults = new GtaToneMapVisuals(-1.0668907449987120E+02f, 4.1897015173052825E-03f, 1.0460364172443734E+02f,
            new Filmic(.22f, .3f, .1f, .2f, .01f, .3f, 4), -2, new Filmic(.22f, .3f, .1f, .2f, .01f, .3f, 4), 1);
        Assert.Equal(defaults, GtaToneMapVisuals.GameDefaults);
        Assert.Equal(defaults, GtaToneMapVisuals.From(null));
        Assert.Equal(defaults, GtaToneMapVisuals.From(GameVisualSettings.Parse("# nothing\nsun.size 1\n")));
        // A file with some of them: those it has, the rest as the game defaults them.
        GtaToneMapVisuals some = GtaToneMapVisuals.From(GameVisualSettings.Parse("Exposure.curve.power 0.007\nTonemapping.dark.filmic.E 0.0\n"));
        Assert.Equal(defaults with { ExposurePower = .007f, Dark = defaults.Dark with { E = 0 } }, some);
    }

    [Fact]
    public void The_same_bright_and_dark_exposure_blends_at_once_as_the_game_does()
    {
        GtaToneMapVisuals same = GameFile with { BrightExposure = 1, DarkExposure = 1 };
        Assert.Equal((1f, -1f), same.Blend);
        Assert.Equal(same.Bright, same.FilmicFor(1));
        Assert.Equal(same.Dark, same.FilmicFor(2));
    }

    [Fact]
    public void Opening_a_game_takes_its_visualsettings_and_closing_it_the_defaults()
    {
        try
        {
            Timecycle.UseVisuals(GameVisualSettings.Parse(FileText));
            Assert.Equal(Expected, Timecycle.ToneMap);
        }
        finally
        {
            Timecycle.UseVisuals(null);
        }
        Assert.Equal(GtaToneMapVisuals.GameDefaults, Timecycle.ToneMap);
    }

    [Fact]
    public void Both_editions_files_have_the_same_tone_map()
    {
        string legacy = TestGames.Legacy();
        Assert.Equal(Expected, Read(legacy, GameTarget.Legacy));
        string enhanced = @"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced";
        if (!File.Exists(Path.Combine(enhanced, "GTA5_Enhanced.exe")))
            Assert.Skip("No GTA V Enhanced installed.");
        Assert.Equal(Expected, Read(enhanced, GameTarget.Enhanced));

        static GtaToneMapVisuals Read(string root, GameTarget target)
        {
            string executable = Path.Combine(root, target == GameTarget.Enhanced ? "GTA5_Enhanced.exe" : "GTA5.exe");
            GameVisualSettings? file = GameVisualSettings.Read(new LoadedGame(root, target, GameKeys.Load(executable)));
            Assert.NotNull(file);
            return GtaToneMapVisuals.From(file);
        }
    }
}
