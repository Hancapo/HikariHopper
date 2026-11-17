using System.Globalization;
using System.Numerics;
using System.Xml;
using FiveFury.Textures;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

/// <summary>
/// One element of GTA V's lens flare, as its lensflare_*.xml has it: where it sits along the line from the
/// sun through the screen's centre, its size, its turn or ring width, its intensity, its stretch, its colour and which
/// of the flare's textures (and which cell of the artefact sheet) it's drawn with.
/// </summary>
internal sealed record GtaFlareElement(
    float DistFromLight,
    float Size,
    float WidthRotate,
    float ScrollSpeed,
    float DistanceInnerOffset,
    float IntensityScale,
    float IntensityFade,
    float AnamorphicScaleU,
    float AnamorphicScaleV,
    bool AnamorphicBehavesLikeArtefact,
    bool AlignRotationToSun,
    Vector4 Colour,
    float GradientMultiplier,
    int Texture,
    int SubGroup,
    float PositionOffsetU,
    float TextureColourDesaturate);

/// <summary>
/// One of GTA V's lens flare settings (CLensFlareSettings): how much of the sun's disc its visibility is
/// measured over and how the fog hides it, how the exposure scales, turns and brightens the elements, the rings' and
/// artefacts' own factors, and the sun's elements. Fields a file lacks keep the parser's (or the class's) default.
/// </summary>
internal sealed record GtaLensFlareSettings(
    float SunVisibilityFactor,
    float SunFogFactor,
    float ChromaticTexUSize,
    float ExposureScaleFactor,
    float ExposureIntensityFactor,
    float ExposureRotationFactor,
    float ChromaticFadeFactor,
    float ArtefactDistanceFadeFactor,
    float ArtefactRotationFactor,
    float ArtefactScaleFactor,
    float CoronaDistanceAdditionalSize,
    float MinExposureScale,
    float MaxExposureScale,
    float MinExposureIntensity,
    float MaxExposureIntensity,
    GtaFlareElement[] Elements)
{
    /// <summary>
    /// A lensflare_*.xml's settings and its sun's elements (its first flare type, the sun's); throws
    /// <see cref="XmlException"/> where it isn't XML.
    /// </summary>
    public static GtaLensFlareSettings Parse(string xml)
    {
        var document = new XmlDocument { XmlResolver = null };
        using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            document.Load(reader);
        XmlElement root = document.DocumentElement ?? throw new XmlException("The lens flare has no settings.");
        var elements = new List<GtaFlareElement>();
        if (root["arrFlareTypeFX"]?["Item"]?["arrFlareFX"] is { } list)
            foreach (XmlElement item in list.ChildNodes.OfType<XmlElement>().Where(node => node.Name == "Item"))
                elements.Add(new GtaFlareElement(
                    Float(item, "fDistFromLight", 0),
                    Float(item, "fSize", .1f),
                    Float(item, "fWidthRotate", .1f),
                    Float(item, "fScrollSpeed", .15f),
                    Float(item, "fDistanceInnerOffset", 0),
                    Float(item, "fIntensityScale", 100),
                    Float(item, "fIntensityFade", 0),
                    Float(item, "fAnimorphicScaleFactorU", 0),
                    Float(item, "fAnimorphicScaleFactorV", 0),
                    Bool(item, "bAnimorphicBehavesLikeArtefact"),
                    Bool(item, "bAlignRotationToSun"),
                    Colour(item, "color"),
                    Float(item, "fGradientMultiplier", 1),
                    (int)Float(item, "nTexture", 0),
                    (int)Float(item, "nSubGroup", 1),
                    Float(item, "fPositionOffsetU", 0),
                    Float(item, "fTextureColorDesaturate", 0)));
        return new GtaLensFlareSettings(
            Float(root, "fSunVisibilityFactor", 1),
            Float(root, "fSunFogFactor", 1),
            Float(root, "fChromaticTexUSize", .25f),
            Float(root, "fExposureScaleFactor", 1),
            Float(root, "fExposureIntensityFactor", 1),
            Float(root, "fExposureRotationFactor", 1),
            Float(root, "fChromaticFadeFactor", 1),
            Float(root, "fArtefactDistanceFadeFactor", 1),
            Float(root, "fArtefactRotationFactor", 1),
            Float(root, "fArtefactScaleFactor", 1),
            Float(root, "fCoronaDistanceAdditionalSize", 1),
            Float(root, "fMinExposureScale", 0),
            Float(root, "fMaxExposureScale", 20),
            Float(root, "fMinExposureIntensity", 0),
            Float(root, "fMaxExposureIntensity", 20),
            [.. elements]);
    }

    private static string? Value(XmlElement parent, string name) => parent[name]?.GetAttribute("value");

    private static float Float(XmlElement parent, string name, float fallback) =>
        float.TryParse(Value(parent, name), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) ? value : fallback;

    private static bool Bool(XmlElement parent, string name) => string.Equals(Value(parent, name), "true", StringComparison.OrdinalIgnoreCase);

    // A Color32 as the parser writes it, 0xAARRGGBB; white where it's missing.
    private static Vector4 Colour(XmlElement parent, string name)
    {
        string? text = Value(parent, name);
        if (text is null || !uint.TryParse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out uint argb))
            return Vector4.One;
        return new Vector4((argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF, argb >> 24) / 255f;
    }
}

/// <summary>
/// The flare's settings in visualsettings.dat: how long it takes to fade out when the sun leaves the
/// screen, and the boost its rings' desaturation is given.
/// </summary>
internal sealed record GtaLensFlareVisuals(float HideMilliseconds, float DesaturationBoost)
{
    /// <summary>The game's file's settings, each with the default the game's code reads it with where the file hasn't it.</summary>
    public static GtaLensFlareVisuals From(Core.GameVisualSettings? file) =>
        new(file?.Get("lensflare.hide.length", 1000) ?? 1000, file?.Get("lensflare.desaturation.boost", 1) ?? 1);
}

/// <summary>
/// GTA V's lens flare as the game has it: Michael's settings (lensflare_m.xml, the first the game loads and the one it
/// starts with) and the four textures the game finds in <c>platform:/textures/graphics.ytd</c>, in the order
/// its elements number them (animorphic, artefact, chromatic and corona).
/// </summary>
internal sealed record GtaLensFlare(GtaLensFlareSettings Settings, GtaLensFlareVisuals Visuals, GameTexture?[] Textures)
{
    public const int Anamorphic = 0, Artefact = 1, Chromatic = 2, Corona = 3, TextureCount = 4;

    /// <summary>The textures by the names the game looks them up by; null where the dictionary hasn't one.</summary>
    public static GameTexture?[] TexturesFrom(TextureDictionary? textures)
    {
        GameTexture? Named(string name) =>
            textures?.Textures.FirstOrDefault(texture => string.Equals(texture.Name, name, StringComparison.OrdinalIgnoreCase));
        return [Named("flare_animorphic"), Named("flare_artefactx2"), Named("flare_chromatic"), Named("flare_corona")];
    }
}

/// <summary>
/// What the flare needs of the frame: the view (origin-relative), the camera, the tangent of the half horizontal field
/// of view, the width over the height, and the width in pixels of the target the sun's visibility is counted on.
/// </summary>
internal readonly record struct GtaLensFlareView(Matrix4x4 ViewProjection, Vector3 Origin, Vector3 Eye, Vector3 Forward, float TanHalfWidth,
    float Aspect, int PixelWidth);

/// <summary>
/// GTA V's lens flare from frame to frame, as the game runs it for the sun: its visibility from the count of the
/// sun's pixels the sky shows, the exposure's scale, intensity and turn (Render), the
/// fade out when the sun leaves the screen and the snap back when it returns, and
/// the elements laid out as it draws them: each a sprite the shader draws as is
/// (gta_lens_flare.wgsl's FlareSprite, <see cref="SpriteVectors"/> vectors each).
/// </summary>
internal sealed class GtaLensFlareState(GtaLensFlare flare)
{
    /// <summary>A sprite's vectors.</summary>
    public const int SpriteVectors = 7;

    /// <summary>The kinds of sprite: a quad, a quad with the artefacts' gradient, a ring.</summary>
    public const int Quad = 0, GradientQuad = 1, Ring = 2;

    // The ring's entries round its circle (half its vertices, less one), and its strip's vertices.
    public const int RingEntries = 88, RingVertices = (RingEntries + 1) * 2;

    // A vector's length the game takes for nought.
    private const float Small = 1e-3f;

    private enum Transition { None, In, Out }

    private readonly GtaLensFlareSettings settings = flare.Settings;
    private readonly float[] scroll = new float[flare.Settings.Elements.Length];
    private Transition transition;
    private double transitionStart, transitionLength;

    /// <summary>How much of the sun shows, nought to one, from the last count.</summary>
    public float SunVisibility { get; private set; }

    /// <summary>The last visibility above nought, which a fade out goes on with.</summary>
    public float CachedVisibility { get; private set; }

    /// <summary>How far the flare has faded out: nought shows it whole, one not at all.</summary>
    public float CurrentFade { get; private set; }

    /// <summary>The radius of the disc the sun's visibility is counted over: the visibility factor times the sun's.</summary>
    public float VisibilityRadius(float sunRadius) => settings.SunVisibilityFactor * sunRadius;

    /// <summary>
    /// The visibility from a count of the sun disc's pixels the sky shows: over the pixels the whole disc would cover
    /// (its radius in pixels at its distance, the area of that circle, over the cosine of the camera's forward with the
    /// sun's position, made unit from the world's origin, as the game has it), times the timecycle's visibility, at most
    /// one. The last value above nought is kept for a fade out.
    /// </summary>
    public void UpdateVisibility(float pixels, Vector3 sunPosition, float radius, in GtaLensFlareView view, float timecycleVisibility)
    {
        float distance = Vector3.Dot(sunPosition - view.Eye, view.Forward);
        float pixelRadius = radius / (view.TanHalfWidth * distance) * (view.PixelWidth >> 1);
        float estimate = MathF.PI * pixelRadius * pixelRadius / MathF.Abs(Vector3.Dot(view.Forward, Vector3.Normalize(sunPosition)));
        float visibility = pixels / estimate * timecycleVisibility;
        SunVisibility = float.IsFinite(visibility) ? MathF.Min(1, visibility) : 0;
        if (SunVisibility > 0)
            CachedVisibility = SunVisibility;
    }

    /// <summary>
    /// The exposure's scale, intensity and turn for an average exposure nought to one (the game's average linear
    /// exposure), each kept in the settings' range; the turn stops where the scale is held, as the game's does (it
    /// compares the scale to the intensity's maximum, as the game does).
    /// </summary>
    public (float Scale, float Intensity, float Rotation) ExposureFactors(float exposure)
    {
        float scale = Math.Clamp(settings.ExposureScaleFactor * exposure, settings.MinExposureScale, settings.MaxExposureScale);
        float intensity = Math.Clamp(settings.ExposureIntensityFactor * exposure, settings.MinExposureIntensity, settings.MaxExposureIntensity);
        float rotation = scale <= settings.MinExposureScale || scale >= settings.MaxExposureIntensity
            ? settings.ExposureRotationFactor * (scale / settings.ExposureScaleFactor)
            : settings.ExposureRotationFactor * exposure;
        return (scale, intensity, rotation);
    }

    /// <summary>The game's linear exposure: where the exposure in stops is in the timecycle's range, nought to one.</summary>
    public static float LinearExposure(float stops, float minimum, float maximum)
    {
        if (minimum == maximum)
            maximum += .0001f;
        return Math.Clamp((stops - minimum) / (maximum - minimum), 0, 1);
    }

    /// <summary>
    /// The sun's place on the screen, nought to one across and down, as the game projects it; null where it can't
    /// be (at the camera's plane).
    /// </summary>
    public static Vector2? ScreenPosition(Vector3 sunPosition, in GtaLensFlareView view)
    {
        Vector4 clip = Vector4.Transform(new Vector4(sunPosition - view.Origin, 1), view.ViewProjection);
        if (clip.W == 0)
            return null;
        return new Vector2(clip.X / clip.W * .5f + .5f, .5f - clip.Y / clip.W * .5f);
    }

    /// <summary>
    /// The fade for this frame, at <paramref name="milliseconds"/> on the game's clock, with the sun at
    /// <paramref name="sun"/> on the screen: the transition under way goes on; on the screen the flare snaps back in
    /// while the sun shows and goes at once when it doesn't; off it, it fades out over visualsettings.dat's hide length.
    /// True where the flare is drawn.
    /// </summary>
    public bool Advance(Vector2 sun, double milliseconds)
    {
        if (transition != Transition.None)
        {
            double progress = (milliseconds - transitionStart) / transitionLength;
            if (progress >= 1)
            {
                CurrentFade = transition == Transition.Out ? 1 : 0;
                transition = Transition.None;
            }
            else
                CurrentFade = (float)(transition == Transition.Out ? progress : 1 - progress);
        }
        bool onScreen = sun.X > 0 && sun.X < 1 && sun.Y > 0 && sun.Y < 1;
        if (onScreen)
        {
            if (SunVisibility < .001f)
            {
                transition = Transition.None;
                CurrentFade = 1;
                CachedVisibility = 0;
            }
            else if (CurrentFade != 0 && transition != Transition.In)
            {
                transition = Transition.None;
                CurrentFade = 0;
            }
        }
        else if (CurrentFade < 1 && transition == Transition.None)
        {
            transition = Transition.Out;
            transitionStart = milliseconds;
            transitionLength = flare.Visuals.HideMilliseconds;
        }
        return CurrentFade < 1;
    }

    /// <summary>
    /// Room for each texture's sprites in the buffer Build writes, in texture order: the rings (the chromatic
    /// texture's) twice over, as each is drawn twice.
    /// </summary>
    public (int First, int Count)[] Layout()
    {
        var layout = new (int First, int Count)[GtaLensFlare.TextureCount];
        int first = 0;
        for (int texture = 0; texture < layout.Length; texture++)
        {
            int count = settings.Elements.Count(element => element.Texture == texture) * (texture == GtaLensFlare.Chromatic ? 2 : 1);
            layout[texture] = (first, count);
            first += count;
        }
        return layout;
    }

    /// <summary>
    /// The sun's elements as the game lays them out for the sun at <paramref name="sun"/> on the screen in its
    /// <paramref name="colour"/> (the sky's sun colour, as a Color32), at the cached visibility: each in its texture's
    /// room in <paramref name="sprites"/> (<see cref="Layout"/>), and how many each texture has. An element whose
    /// intensity comes to nought adds nothing and is left out. <paramref name="seconds"/> is the frame's time step,
    /// <paramref name="milliseconds"/> the game's clock.
    /// </summary>
    public int[] Build(Span<Vector4> sprites, Vector2 sun, Vector3 colour, float aspect, float exposure, float seconds, double milliseconds)
    {
        (int First, int Count)[] layout = Layout();
        int[] written = new int[GtaLensFlare.TextureCount];
        var (exposureScale, exposureIntensity, exposureRotation) = ExposureFactors(exposure);
        float intensity = CachedVisibility;
        colour = Vector3.Clamp(colour, Vector3.Zero, Vector3.One);
        var aspectRatio = new Vector2(1, aspect);

        // The line from the sun through the screen's centre to its edge, and how far towards the sun the elements are
        // drawn in as it nears the centre.
        Vector2 dirNorm = new Vector2(.5f) - sun;
        float length = dirNorm.Length(), distScale = 0;
        Vector2 flareDir;
        if (length < Small)
        {
            dirNorm = flareDir = Vector2.Zero;
        }
        else
        {
            Vector2 toEdge = Vector2.Abs(dirNorm) + new Vector2(.5f);
            dirNorm /= length;
            flareDir = dirNorm * toEdge.Length();
            distScale = MathF.Min(1, length * 2);
            distScale *= distScale;
        }

        for (int i = 0; i < settings.Elements.Length; i++)
        {
            GtaFlareElement fx = settings.Elements[i];
            if ((uint)fx.Texture >= GtaLensFlare.TextureCount)
                continue;
            bool chromatic = fx.Texture == GtaLensFlare.Chromatic, artefact = fx.Texture == GtaLensFlare.Artefact;
            if (chromatic)
            {
                // The rings scroll round, wrapped to one.
                scroll[i] += seconds * fx.ScrollSpeed * .5f;
                if (scroll[i] >= 1)
                    scroll[i] -= 1;
            }
            float gamma = intensity * fx.IntensityScale;
            if (chromatic)
                gamma -= intensity * fx.IntensityFade * MathF.Max(0, 1 - distScale);
            else
                gamma *= exposureIntensity;
            float emissive = MathF.Max(gamma, 0);
            if (emissive <= 0)
                continue;

            float along = artefact || (fx.Texture == GtaLensFlare.Anamorphic && fx.AnamorphicBehavesLikeArtefact)
                ? 2 * (2 * length) + fx.DistFromLight * (2 * length)
                : fx.DistFromLight * distScale;
            Vector2 position = sun + flareDir * along;
            if (artefact)
                position.X += fx.PositionOffsetU;

            float halfSize;
            if (chromatic)
                halfSize = fx.Size;
            else
            {
                halfSize = exposureScale * fx.Size;
                if (artefact)
                    halfSize *= settings.ArtefactScaleFactor;
                else if (fx.Texture == GtaLensFlare.Corona)
                    halfSize += length * settings.CoronaDistanceAdditionalSize;
            }

            Vector2 fromLight = sun - position;
            fromLight = fromLight.LengthSquared() > 0 ? Vector2.Normalize(fromLight) : Vector2.Zero;
            // The im shader's general parameters: reset to their defaults after every sprite, set for the artefacts'
            // gradient and the rings' desaturation.
            Vector4 params0 = new(1, 1, 1, 0), params1 = Vector4.Zero;
            if (artefact)
            {
                Vector2 half = new(halfSize, halfSize * aspect);
                Vector2 pixelDir = position - fromLight * half * .5f * fx.GradientMultiplier;
                params0 = new Vector4(fromLight, pixelDir.X, pixelDir.Y);
                params1 = new Vector4(half, 0, 0);
            }
            else if (chromatic)
                params1 = new Vector4(0, 0, fx.TextureColourDesaturate, flare.Visuals.DesaturationBoost);

            // To clip space.
            Vector2 clip = new(position.X * 2 - 1, 1 - position.Y * 2);
            int slot = layout[fx.Texture].First + written[fx.Texture];

            if (chromatic)
            {
                float scaleFactor = MathF.Min(1, length);
                Vector2 scaleFactors = new(fx.AnamorphicScaleU, fx.AnamorphicScaleV);
                Vector2 inner = new Vector2(halfSize) * aspectRatio + length * scaleFactors;
                Vector2 outer = new Vector2(halfSize + fx.WidthRotate) * aspectRatio + length * scaleFactors;
                float offset = fx.DistanceInnerOffset * scaleFactor;
                Vector2 innerOffset = dirNorm * new Vector2(-offset, offset);
                float step = 2 * MathF.PI * fx.Size / (settings.ChromaticTexUSize * RingVertices);
                // Two rings, the second's texture scrolling the other way.
                for (int pass = 0; pass < 2; pass++)
                {
                    Span<Vector4> ring = sprites.Slice((slot + pass) * SpriteVectors, SpriteVectors);
                    ring[0] = new Vector4(clip, inner.X, inner.Y);
                    ring[1] = new Vector4(outer, innerOffset.X, innerOffset.Y);
                    ring[2] = new Vector4(dirNorm, pass == 0 ? scroll[i] : -scroll[i], step);
                    ring[3] = new Vector4(fx.Colour.X, fx.Colour.Y, fx.Colour.Z, emissive);
                    ring[4] = params0;
                    ring[5] = params1;
                    ring[6] = new Vector4(fx.Texture, Ring, settings.ChromaticFadeFactor, CurrentFade);
                }
                written[fx.Texture] += 2;
                continue;
            }

            float x1 = -halfSize, y1 = halfSize, x2 = halfSize, y2 = -halfSize;
            if (!artefact)
            {
                x1 -= length * fx.AnamorphicScaleU;
                y1 += length * fx.AnamorphicScaleV;
                x2 += length * fx.AnamorphicScaleU;
                y2 -= length * fx.AnamorphicScaleV;
            }
            Vector4 uv = CellOf(fx.SubGroup);
            Vector3 final = Vector3.Min(Vector3.One, colour * fx.Colour.AsVector3());
            if (artefact)
            {
                float fade = Math.Clamp((.707f - length) * settings.ArtefactDistanceFadeFactor, 0, 1);
                final *= 1 - fade * fade;
            }
            final *= 1 - CurrentFade;

            Vector2 p0, p1, p2, p3;
            if (fx.Texture != GtaLensFlare.Corona || fx.WidthRotate != 0)
            {
                float turn;
                if (artefact)
                {
                    if (fx.AlignRotationToSun)
                    {
                        float angle = MathF.Acos(Math.Clamp(Vector2.Dot(new Vector2(0, -1), fromLight), -1, 1));
                        turn = fromLight.X > 0 ? -angle : angle;
                    }
                    else
                        turn = exposureRotation * settings.ArtefactRotationFactor;
                }
                else if (fx.Texture == GtaLensFlare.Corona)
                    turn = (float)(milliseconds * .001 * fx.WidthRotate);
                else
                    turn = fx.WidthRotate * MathF.PI;
                float sin = MathF.Sin(turn), cos = MathF.Cos(turn);
                Vector2 rot1 = new(cos, sin), rot2 = new(-sin, cos);
                Vector2 finalAspect = aspectRatio;
                if (artefact)
                    finalAspect += length * new Vector2(fx.AnamorphicScaleU, fx.AnamorphicScaleV);
                Vector2 Corner(float x, float y) => clip + (rot1 * x + rot2 * y) * finalAspect;
                (p0, p1, p2, p3) = (Corner(x2, y1), Corner(x1, y1), Corner(x2, y2), Corner(x1, y2));
            }
            else
            {
                (p0, p1, p2, p3) = (new(clip.X + x2, clip.Y + y1 * aspect), new(clip.X + x1, clip.Y + y1 * aspect),
                    new(clip.X + x2, clip.Y + y2 * aspect), new(clip.X + x1, clip.Y + y2 * aspect));
            }
            Span<Vector4> quad = sprites.Slice(slot * SpriteVectors, SpriteVectors);
            quad[0] = new Vector4(p0, p1.X, p1.Y);
            quad[1] = new Vector4(p2, p3.X, p3.Y);
            quad[2] = uv;
            quad[3] = new Vector4(final, emissive);
            quad[4] = params0;
            quad[5] = params1;
            quad[6] = new Vector4(fx.Texture, artefact ? GradientQuad : Quad, 0, 0);
            written[fx.Texture]++;
        }
        return written;
    }

    // The artefact sheet's cells, as the game lays them out over flare_artefactx2: the whole texture first (the
    // other elements' cell), then its cells of half its width, row by row (the game's sheet is 256 by 512: two by
    // four). Past the last cell, and without the sheet, the whole texture.
    private Vector4 CellOf(int subGroup)
    {
        if (subGroup <= 0 || flare.Textures[GtaLensFlare.Artefact] is not { } sheet)
            return new Vector4(0, 0, 1, 1);
        int width = sheet.Width, height = sheet.Height, cell = Math.Max(1, width / 2);
        float cellU = cell / (float)width, cellV = cell / (float)height;
        int index = 0;
        for (float v = 0; v < 1; v += cellV)
            for (float u = 0; u < 1; u += cellU)
                if (++index == subGroup)
                    return new Vector4(u, v, u + cellU, v + cellV);
        return new Vector4(0, 0, 1, 1);
    }
}
