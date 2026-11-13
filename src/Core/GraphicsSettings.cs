namespace HikariHopper.Core;

/// <summary>
/// How the 3D views draw, one set for every viewer: anti-aliasing (CMAA 2), bloom, the size the drawables are drawn at,
/// the sun's shadows and their resolution, the reflections' resolution, and how sharply textures are filtered at a
/// slant. Saved as they change; <see cref="Changed"/> tells
/// the open viewers, which draw with them at once.
/// </summary>
public static class GraphicsSettings
{
    public const string AntialiasingKey = "graphics/antialiasing";
    public const string BloomKey = "graphics/bloom";
    public const string ShadowsKey = "graphics/shadows";
    public const string ShadowResolutionKey = "graphics/shadowResolution";
    public const string AnisotropyKey = "graphics/anisotropy";
    public const string RenderScaleKey = "graphics/renderScale";
    public const string ReflectionWidthKey = "graphics/reflectionWidth";

    /// <summary>The render scales to choose from, in percent of the window's size.</summary>
    public static IReadOnlyList<int> RenderScales { get; } = [75, 100, 150, 200];

    /// <summary>The reflection map's widths to choose from, in texels: Low, Medium and High.</summary>
    public static IReadOnlyList<int> ReflectionWidths { get; } = [256, 512, 1024];

    /// <summary>The shadow map's sides to choose from, in texels: Medium, High and Ultra.</summary>
    public static IReadOnlyList<int> ShadowResolutions { get; } = [1024, 2048, 4096];

    /// <summary>The anisotropic filtering to choose from; 1 is trilinear alone.</summary>
    public static IReadOnlyList<int> Anisotropies { get; } = [1, 4, 8, 16];

    public const int DefaultShadowResolution = 2048, DefaultAnisotropy = 8, DefaultRenderScale = 100;
    public const uint DefaultReflectionWidth = 512;

    /// <summary>A setting changed; the viewers draw with it from their next frame.</summary>
    public static event Action? Changed;

    /// <summary>Whether CMAA 2 smooths the frame's edges; on by default.</summary>
    public static bool Antialiasing
    {
        get => AppSettings.GetInt(AntialiasingKey, 1) != 0;
        set => SetFlag(AntialiasingKey, Antialiasing, value);
    }

    /// <summary>Whether bright light blooms, as the game's post-process makes it; on by default.</summary>
    public static bool Bloom
    {
        get => AppSettings.GetInt(BloomKey, 1) != 0;
        set => SetFlag(BloomKey, Bloom, value);
    }

    /// <summary>Whether the sun casts shadows, on the model and the ground; on by default.</summary>
    public static bool Shadows
    {
        get => AppSettings.GetInt(ShadowsKey, 1) != 0;
        set => SetFlag(ShadowsKey, Shadows, value);
    }

    /// <summary>The side of each of the shadow's cascades in texels: one of <see cref="ShadowResolutions"/>, 2048 by default.</summary>
    public static int ShadowResolution
    {
        get => Nearest(ShadowResolutions, AppSettings.GetInt(ShadowResolutionKey, DefaultShadowResolution));
        set => SetNumber(ShadowResolutionKey, ShadowResolution, Nearest(ShadowResolutions, value));
    }

    /// <summary>The textures' anisotropic filtering: one of <see cref="Anisotropies"/>, 8 by default.</summary>
    public static int Anisotropy
    {
        get => Nearest(Anisotropies, AppSettings.GetInt(AnisotropyKey, DefaultAnisotropy));
        set => SetNumber(AnisotropyKey, Anisotropy, Nearest(Anisotropies, value));
    }

    /// <summary>The size the drawables are drawn at, in percent of the window's: one of <see cref="RenderScales"/>, 100 by default.</summary>
    public static int RenderScale
    {
        get => Nearest(RenderScales, AppSettings.GetInt(RenderScaleKey, DefaultRenderScale));
        set => SetNumber(RenderScaleKey, RenderScale, Nearest(RenderScales, value));
    }

    /// <summary>The reflection map's width in texels: one of <see cref="ReflectionWidths"/>, 512 by default.</summary>
    public static int ReflectionWidth
    {
        get => Nearest(ReflectionWidths, AppSettings.GetInt(ReflectionWidthKey, (int)DefaultReflectionWidth));
        set => SetNumber(ReflectionWidthKey, ReflectionWidth, Nearest(ReflectionWidths, value));
    }

    // A value saved by hand or by an older version is read as the nearest choice.
    private static int Nearest(IReadOnlyList<int> choices, int value) => choices.MinBy(choice => Math.Abs(choice - value));

    // Saved and told only when it changes: showing the settings writes nothing.
    private static void SetFlag(string key, bool was, bool value)
    {
        if (was == value)
            return;
        AppSettings.SetInt(key, value ? 1 : 0);
        Changed?.Invoke();
    }

    private static void SetNumber(string key, int was, int value)
    {
        if (was == value)
            return;
        AppSettings.SetInt(key, value);
        Changed?.Invoke();
    }
}
