using System.Numerics;
using CommonFury.Assets;
using CommonFury.Hashing;
using CommonFury.Vertices;
using FiveFury.Drawables;
using FiveFury.Textures;
using HikariHopper.Core;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

/// <summary>
/// What a cloud hat geometry's effect does (the game's clouds effects): whether its density is three animated
/// texture layers (clouds_anim, clouds_animsoft, clouds_altitude) or one (clouds_fast, clouds_soft); whether it fades
/// against the scene's depth (the soft ones) or is pushed to the far plane, behind everything (clouds_fast,
/// clouds_anim); and whether its layers slide against the camera, wrapping, instead of animating (clouds_altitude).
/// </summary>
internal readonly record struct CloudHatEffect(bool Animated, bool Soft, bool Infinite, bool CameraScroll)
{
    private static readonly Dictionary<uint, CloudHatEffect> Effects = new()
    {
        [new Hash32("clouds_soft").Value] = new(false, true, false, false),
        [new Hash32("clouds_fast").Value] = new(false, false, true, false),
        [new Hash32("clouds_anim").Value] = new(true, false, true, false),
        [new Hash32("clouds_animsoft").Value] = new(true, true, false, false),
        [new Hash32("clouds_altitude").Value] = new(true, true, false, true),
    };

    /// <summary>The effect of a material's shader; null for one that isn't a cloud hat's.</summary>
    internal static CloudHatEffect? Of(uint shader) => Effects.TryGetValue(shader, out CloudHatEffect effect) ? effect : null;
}

/// <summary>
/// One geometry of a cloud hat layer's model, ready to draw: its vertices (<see cref="Floats"/> each: place, normal,
/// colour, coordinates, tangent), its triangles, its effect, its material's own values (clouds_locals' art-set ones:
/// the three layers' UV offsets and rescales, the soft range, the environment map's alpha scale, the three layers'
/// animation scales) and its six textures (density and normal, then the two detail pairs).
/// </summary>
internal sealed record GtaCloudHatGeometry(float[] Vertices, uint[] Indices, CloudHatEffect Effect, Vector4[] Material, GameTexture?[] Textures)
{
    /// <summary>A vertex's floats: place, normal, colour (red, green, blue, alpha), coordinates, tangent.</summary>
    public const int Floats = 16;

    // The texture parameters, by their Legacy names, in the order the shader binds them.
    private static readonly string[] Samplers =
        ["DensitySampler", "NormalSampler", "DetailDensitySampler", "DetailNormalSampler", "DetailDensity2Sampler", "DetailNormal2Sampler"];

    /// <summary>
    /// A drawable's cloud geometries, the high level of detail's, as the game draws them: those whose material is a cloud
    /// effect, their textures from the drawable's own dictionary or else <paramref name="dictionaries"/>, nearest first.
    /// </summary>
    internal static GtaCloudHatGeometry[] From(Drawable drawable, IReadOnlyList<TextureDictionary> dictionaries)
    {
        var made = new List<GtaCloudHatGeometry>();
        GameTexture? Find(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            foreach (TextureDictionary? dictionary in (IEnumerable<TextureDictionary?>)[drawable.TextureDictionary, .. dictionaries])
                if (dictionary?.Textures.FirstOrDefault(texture => string.Equals(texture.Name, name, StringComparison.OrdinalIgnoreCase)) is { } found)
                    return found;
            return null;
        }
        foreach (DrawableModel model in drawable.Lods[DrawableLod.High].Models)
            foreach (DrawableGeometry geometry in model.Geometries)
            {
                if (geometry.PrimitiveType != 3 || geometry.MaterialIndex >= drawable.Materials.Count)
                    continue;
                DrawableMaterial material = drawable.Materials[geometry.MaterialIndex];
                if (CloudHatEffect.Of(material.ShaderHash.Value) is not { } effect)
                    continue;
                // The material's values by their Legacy names: Enhanced's own names are aliases of them in FiveFury's
                // shader catalogue, as SurfaceMaterial reads them.
                bool enhanced = material.Gen9Parameters is not null;
                ShaderDefinition? definition = (enhanced ? ShaderCatalog.Enhanced : ShaderCatalog.Legacy).Find(material.ShaderHash)
                    ?? ShaderCatalog.Legacy.Find(material.ShaderHash) ?? ShaderCatalog.Enhanced.Find(material.ShaderHash);
                var named = new Dictionary<string, MaterialParameterValue>(StringComparer.OrdinalIgnoreCase);
                foreach (MaterialParameterValue parameter in material.GetParameters())
                {
                    ShaderParameterDefinition? known = definition?.FindParameter(parameter.NameHash);
                    if (((enhanced ? known?.LegacyName : null) ?? known?.Name ?? parameter.Name) is { } name)
                        named.TryAdd(name, parameter);
                }
                // A value the material doesn't set is its shader's default, else the effect's.
                Vector4 Value(string name, Vector4 fallback)
                {
                    float[]? found = named.TryGetValue(name, out MaterialParameterValue? value) && value.Values.Length > 0 ? value.Values.ToArray()
                        : definition?.FindParameter(name) is { Defaults.Count: > 0 } parameter ? [.. parameter.Defaults] : null;
                    return found is null ? fallback
                        : new Vector4(found[0], found.Length > 1 ? found[1] : fallback.Y, found.Length > 2 ? found[2] : fallback.Z, found.Length > 3 ? found[3] : fallback.W);
                }
                Vector2 Pair(string name, Vector2 fallback)
                {
                    Vector4 value = Value(name, new Vector4(fallback, 0, 0));
                    return new Vector2(value.X, value.Y);
                }
                // clouds_locals' art-set values, packed as the effect packs them, with the effects' own defaults.
                Vector2 offset1 = Pair("gUVOffset1", Vector2.Zero), offset2 = Pair("gUVOffset2", Vector2.Zero), offset3 = Pair("gUVOffset3", Vector2.Zero);
                Vector2 rescale1 = Pair("gRescaleUV1", Vector2.One), rescale2 = Pair("gRescaleUV2", Vector2.One), rescale3 = Pair("gRescaleUV3", Vector2.One);
                Vector2 anim1 = Pair("cloudLayerAnimScale1", Vector2.One), anim2 = Pair("cloudLayerAnimScale2", Vector2.One), anim3 = Pair("cloudLayerAnimScale3", Vector2.One);
                float soft = Value("gSoftParticleRange", new Vector4(175, 0, 0, 0)).X, envMap = Value("gEnvMapAlphaScale", new Vector4(.75f, 0, 0, 0)).X;
                Vector4[] values =
                [
                    new(offset1, offset2.X, offset2.Y),
                    new(offset3, rescale1.X, rescale1.Y),
                    new(rescale2, rescale3.X, rescale3.Y),
                    new(soft, envMap, anim1.X, anim1.Y),
                    new(anim2, anim3.X, anim3.Y),
                ];
                GameTexture?[] textures = [.. Samplers.Select(name => named.TryGetValue(name, out MaterialParameterValue? value) && value.Texture is { } texture
                    ? texture.EmbeddedTexture ?? Find(texture.Name) : null)];

                VertexBuffer buffer = geometry.Vertices;
                bool colours = buffer.Layout.TryFind(VertexSemantic.Color0, out _), coordinates = buffer.Layout.TryFind(VertexSemantic.TexCoord0, out _);
                bool normals = buffer.Layout.TryFind(VertexSemantic.Normal, out _), tangents = buffer.Layout.TryFind(VertexSemantic.Tangent, out _);
                var vertices = new float[buffer.Count * Floats];
                for (int i = 0; i < buffer.Count; i++)
                {
                    Vector4 place = buffer.ReadElement(i, VertexSemantic.Position);
                    Vector4 normal = normals ? buffer.ReadElement(i, VertexSemantic.Normal) : Vector4.UnitZ;
                    Vector4 colour = colours ? buffer.ReadElement(i, VertexSemantic.Color0) : Vector4.One;
                    Vector4 uv = coordinates ? buffer.ReadElement(i, VertexSemantic.TexCoord0) : Vector4.Zero;
                    Vector4 tangent = tangents ? buffer.ReadElement(i, VertexSemantic.Tangent) : Vector4.UnitX;
                    new[] { place.X, place.Y, place.Z, normal.X, normal.Y, normal.Z, colour.X, colour.Y, colour.Z, colour.W, uv.X, uv.Y,
                        tangent.X, tangent.Y, tangent.Z, tangent.W }.CopyTo(vertices, i * Floats);
                }
                ReadOnlySpan<ushort> from = geometry.Indices.Indices;
                int start = (int)Math.Min(geometry.IndexBufferOffset, (uint)from.Length);
                int count = Math.Min(geometry.IndexCount > 0 ? geometry.IndexCount : from.Length, from.Length - start) / 3 * 3;
                var indices = new uint[count];
                for (int i = 0; i < count; i++)
                    indices[i] = from[start + i] < buffer.Count ? from[start + i] : 0u;
                if (count > 0)
                    made.Add(new GtaCloudHatGeometry(vertices, indices, effect, values, textures));
            }
        return [.. made];
    }
}

/// <summary>A cloud hat's layer: as clouds.xml has it, and its model's geometries (none where its model can't be read).</summary>
internal sealed record GtaCloudHatLayer(CloudHatLayerDefinition Definition, GtaCloudHatGeometry[] Geometries);

/// <summary>
/// A weather's cloud settings at a time of day, as the game blends them each frame (CloudFrame, from
/// the game's blend): the cloud keyframes' colours and lighting,
/// the light from the timecycle's sun and moon, and the direction it comes from.
/// </summary>
internal readonly record struct CloudFrame(Vector4 PiercingLightPowerStrengthNormalStrengthThickness, Vector3 ScaleDiffuseFillAmbient,
    Vector4 ScaleSkyBounceEastWest, Vector3 LightColor, Vector3 CloudColor, Vector3 AmbientColor, Vector3 SkyColor, Vector3 BounceColor, Vector3 EastColor,
    Vector3 WestColor, Vector2 DensityShiftScale, float ScatteringConst, float ScatteringScale, float WrapAmount, float MsaaErrorRef, Vector3 SunDirection);

/// <summary>
/// How far a cloud hat's animation has gone: its turn, the hat's
/// angular velocity times the hats' speed a second, and its three texture layers' offsets, each layer's UV velocity
/// times how strong the wind is a second. The game steps them by its clock's time step; here by the renderer's.
/// </summary>
internal sealed class CloudHatAnimation
{
    internal Vector3 Rotation;
    internal readonly Vector2[] Offsets = new Vector2[3];

    /// <summary>Moves the animation on by <paramref name="seconds"/>, with the wind's multiplier <paramref name="wind"/>.</summary>
    internal void Advance(GtaCloudHats hats, float seconds, float wind)
    {
        if (!(seconds > 0) || !float.IsFinite(seconds))
            return;
        Rotation += hats.Hat.AngularVelocity * seconds * hats.HatSpeed;
        if (!hats.Hat.EnableAnimations)
            return;
        for (int i = 0; i < Offsets.Length; i++)
            Offsets[i] += hats.Hat.UVVelocity[i] * seconds * wind;
    }
}

/// <summary>
/// GTA V's cloud hat for the viewer's weather, as the game draws it:
/// the hat the weather's cloud list picks, its layers' models from the game's archives,
/// the weather's keyframed cloud settings, and the manager's and visualsettings.dat's values that move them.
/// </summary>
/// <param name="HatSpeed">visualsettings.dat's cloud.speed.hats: how fast the hats turn.</param>
/// <param name="WindMin">The weather's lightest wind (weather.xml's WindMin), nought to one.</param>
/// <param name="WindMax">The weather's strongest wind.</param>
internal sealed record GtaCloudHats(CloudHatDefinition Hat, GtaCloudHatLayer[] Layers, CloudSettings Settings, Vector3 CamPositionScaler,
    float AltitudeScrollScaler, float HatSpeed, float WindMin, float WindMax)
{
    /// <summary>The constants a geometry is drawn with: gta_cloud_hats.wgsl's CloudHatLayer.</summary>
    public const int Bytes = 32 * 16;

    /// <summary>How many geometries the layers have between them.</summary>
    internal int GeometryCount { get; } = Layers.Sum(layer => layer.Geometries.Length);

    // How far below its height trigger an altitude layer streams in.
    private const float AltitudeStreamIn = 50;

    // The weather's wind: the wind's speed at its strongest, how much higher it is from the lowest height it's made stronger at up to
    // the highest, and the speeds over which it scrolls the hats not at all to fully (gWindEffectMinSpeed,
    // gWindEffectMaxSpeed).
    private const float WindSpeedMax = 12, WindMultZMin = 50, WindMultZMax = 200, WindMultValue = 2, WindEffectMin = 0, WindEffectMax = 5;

    /// <summary>
    /// Where the weather's wind is between its lightest and strongest: the game wanders it
    /// at random, towards a target drawn evenly from nought to one; the viewer holds it at that draw's mean.
    /// </summary>
    internal const float WindInterp = .5f;

    /// <summary>
    /// The hat a weather's cloud list shows: the game rolls for one of
    /// those it lists, each as likely as its weight; the viewer takes the likeliest, the first of them on a tie, as the
    /// game walks the list. <paramref name="hats"/> (no hat) where it lists none, or no hat wins.
    /// </summary>
    internal static int Select(CloudSettings settings, int hats)
    {
        int best = hats, weight = 0;
        for (int i = 0; i < Math.Min(settings.Bits.Length, settings.Probability.Length); i++)
            if (settings.Bits[i] && settings.Probability[i] > weight)
                (best, weight) = (i, settings.Probability[i]);
        return best;
    }

    /// <summary>
    /// The weather's cloud settings at <paramref name="hours"/> (one weather, no transitions): the
    /// keyframes at the hour (scattering's scale scaled by 0.2), then the light: the timecycle's moon and sun colours
    /// by how far each is faded in, squared, then both it and the scattering scaled by the moon's phase while the moon
    /// shows; from the sun while it's up, else from the moon.
    /// </summary>
    internal static CloudFrame Frame(CloudSettings settings, float hours, SkyPath? path, TimecycleSample? timecycle = null)
    {
        // The hour stretched as the timecycle blends its keyframes, which Timecycle.Sample
        // does by the hour.
        float time = hours % 24;
        if (time < 0)
            time += 24;
        static Vector3 Xyz(Vector4 value) => new(value.X, value.Y, value.Z);
        Vector4 density = settings.DensityShiftScaleScatteringConstScale.Query(time);
        Vector4 lighting = settings.ScaleDiffuseFillAmbientWrapAmount.Query(time);

        TimecycleSample tc = timecycle ?? Timecycle.Sample(hours);
        var (sunFade, moonFade) = DayCycle.Fades(hours, path);
        // The timecycle's own defaults where its file hasn't them (its variable table).
        Vector3 light = new Vector3(tc.Get("sky_moon_col_r", 1), tc.Get("sky_moon_col_g", 1), tc.Get("sky_moon_col_b", 1)) * moonFade
            + new Vector3(tc.Get("sky_sun_col_r", .5f), tc.Get("sky_sun_col_g", .5f), tc.Get("sky_sun_col_b", .5f)) * sunFade;
        light *= light;
        float moonPhase = moonFade == 0 ? 1 : GtaSky.MoonPhaseIntensity;
        Vector3 direction = sunFade <= 0 ? DayCycle.MoonDirection(hours, path) : DayCycle.SunDirection(hours, path);
        return new CloudFrame(
            settings.PiercingLightPowerStrengthNormalStrengthThickness.Query(time),
            Xyz(lighting),
            settings.ScaleFillColors.Query(time),
            light * moonPhase,
            Xyz(settings.CloudColor.Query(time)),
            Xyz(settings.AmbientColor.Query(time)),
            Xyz(settings.SkyColor.Query(time)),
            Xyz(settings.BounceColor.Query(time)),
            Xyz(settings.EastColor.Query(time)),
            Xyz(settings.WestColor.Query(time)),
            new Vector2(density.X, density.Y),
            density.Z,
            density.W * .2f * moonPhase,
            lighting.W,
            1,
            direction);
    }

    /// <summary>
    /// How strongly the wind scrolls the hats' texture layers for a camera at <paramref name="height"/>
    /// (the layer's wind): the weather's wind (made stronger up to twice from 50 m
    /// to 200 m up, at most one) times its strongest speed and the timecycle's wind_speed_mult, from no scroll at
    /// <see cref="WindEffectMin"/> metres a second to full at <see cref="WindEffectMax"/>.
    /// </summary>
    internal float Wind(float height, float hours, TimecycleSample? timecycle = null)
    {
        float wind = WindMin + (WindMax - WindMin) * WindInterp;
        if (height > WindMultZMin)
            wind *= height > WindMultZMax ? WindMultValue : (height - WindMultZMin) / (WindMultZMax - WindMultZMin) * (WindMultValue - 1) + 1;
        wind = MathF.Min(wind, 1);
        float speed = wind * WindSpeedMax * (timecycle ?? Timecycle.Sample(hours)).Get("wind_speed_mult", 1);
        return Math.Clamp((speed - WindEffectMin) / (WindEffectMax - WindEffectMin), 0, 1);
    }

    /// <summary>
    /// Which layers are shown, as the hat's transition in leaves them with nothing going out:
    /// in order, each while what's left of a cost of one covers its own,
    /// the altitude layers only once the camera is within <see cref="AltitudeStreamIn"/> metres below their trigger.
    /// </summary>
    internal void Active(float height, Span<bool> active)
    {
        float cost = 1;
        for (int i = 0; i < Layers.Length; i++)
        {
            CloudHatLayerDefinition layer = Layers[i].Definition;
            bool wanted = layer.HeightTrigger <= 0 || height > layer.HeightTrigger - AltitudeStreamIn;
            active[i] = wanted && cost >= layer.CostFactor;
            if (active[i])
                cost -= layer.CostFactor;
        }
    }

    /// <summary>
    /// How far a layer is faded in for a camera at <paramref name="height"/> (once
    /// fully transitioned in): nothing below its height trigger, faded in over its fade range above it; nothing above
    /// its second trigger, faded out over its second range below it.
    /// </summary>
    internal static float Fade(CloudHatLayerDefinition layer, float height)
    {
        float t = 1;
        if (layer.HeightTrigger > 0)
        {
            float delta = height - layer.HeightTrigger;
            if (delta < 0)
                t = 0;
            else if (layer.HeightFadeRange > 0)
                t *= Math.Clamp(delta / layer.HeightFadeRange, 0, 1);
        }
        if (layer.HeightTrigger2 > 0)
        {
            float delta = layer.HeightTrigger2 - height;
            if (delta < 0)
                t = 0;
            else if (layer.HeightFadeRange2 > 0)
                t *= Math.Clamp(delta / layer.HeightFadeRange2, 0, 1);
        }
        return t;
    }

    /// <summary>
    /// The hat's turn with a layer's share of its animation (mRotation's angles plus the accumulated turn
    /// times the layer's rotation scale: about X, then Y, then Z).
    /// </summary>
    internal Matrix4x4 Rotation(CloudHatLayerDefinition layer, CloudHatAnimation animation)
    {
        Vector3 angles = Hat.Rotation + animation.Rotation * layer.RotationScale;
        return Matrix4x4.CreateRotationX(angles.X) * Matrix4x4.CreateRotationY(angles.Y) * Matrix4x4.CreateRotationZ(angles.Z);
    }

    /// <summary>
    /// Each layer's constants for this frame, as the game sets them, into <paramref name="values"/>
    /// (<see cref="Bytes"/> a geometry, the layers' geometries in order), and into <paramref name="drawn"/> whether
    /// each geometry is drawn (not where its layer isn't shown, is faded out, or is above or below its triggers): how
    /// many are. <paramref name="camera"/> is in the world, <paramref name="near"/> the projection's near plane,
    /// <paramref name="waterLevel"/> the water's height.
    /// </summary>
    internal int Write(Span<Vector4> values, Span<bool> drawn, CloudFrame frame, CloudHatAnimation animation, Vector3 camera, float near,
        float waterLevel)
    {
        if (values.Length < GeometryCount * (Bytes / 16) || drawn.Length < GeometryCount)
            throw new ArgumentException("Too small for the cloud hat's constants.", nameof(values));
        drawn.Clear();
        int count = 0;
        Span<bool> active = stackalloc bool[Layers.Length];
        Active(camera.Z, active);

        // Wrap lighting, and Henyey-Greenstein's constants with g kept off one.
        float wrap = frame.WrapAmount, wrapMult = 1 / (1 + wrap);
        var wrapLighting = new Vector4(wrapMult, wrap * wrapMult, frame.MsaaErrorRef, 0);
        float g = Math.Clamp(frame.ScatteringConst, -.999999f, .999999f), g2 = g * g;
        var scattering = new Vector4(g, g2, 3 * (1 - g2) / (2 * (2 + g2)), frame.ScatteringScale);
        // The soft clouds' depth conversion: near, far, Q = far / (far - near) and -near Q; the viewer's projection is
        // infinite, so Q is one (and far isn't used).
        var nearFarQ = new Vector4(near, 0, 1, -near);
        Vector3 ModeIs(int mode) => new(Hat.AnimMode[0] == mode ? 1 : 0, Hat.AnimMode[1] == mode ? 1 : 0, Hat.AnimMode[2] == mode ? 1 : 0);
        Vector3 combine = ModeIs(0), sculpt = ModeIs(1);
        var show = new Vector3(Hat.ShowLayer[0] ? 1 : 0, Hat.ShowLayer[1] ? 1 : 0, Hat.ShowLayer[2] ? 1 : 0);
        Vector4 scale = frame.ScaleSkyBounceEastWest;
        // The texture layers' offsets; the altitude layers' camera scroll goes in the second's last two, and stays for
        // the layers after, as the game keeps one array for them all.
        var uv0 = new Vector4(animation.Offsets[0], animation.Offsets[1].X, animation.Offsets[1].Y);
        var uv1 = new Vector4(animation.Offsets[2], 0, 0);
        var cameraScroll = new Vector3(camera.X * AltitudeScrollScaler, camera.Y * AltitudeScrollScaler, 0);

        int slot = 0;
        for (int i = 0; i < Layers.Length; i++)
        {
            GtaCloudHatLayer layer = Layers[i];
            CloudHatLayerDefinition definition = layer.Definition;
            int first = slot;
            slot += layer.Geometries.Length;
            if (!active[i] || layer.Geometries.Length == 0)
                continue;
            if ((definition.HeightTrigger > 0 && camera.Z < definition.HeightTrigger) || (definition.HeightTrigger2 > 0 && camera.Z > definition.HeightTrigger2))
                continue;
            float t = Fade(definition, camera.Z);
            if (t <= 0)
                continue;

            // The density, faded in by the transition (here, by the layer's heights).
            float shift = frame.DensityShiftScale.X, densityScale = frame.DensityShiftScale.Y;
            float alphaT = Hat.TransitionAlphaRange > 0 ? Math.Clamp(t / Hat.TransitionAlphaRange, 0, 1) : 1;
            float alphaScale = float.Lerp(0, densityScale, alphaT);
            float shifted = float.Lerp(1, shift, t);
            float desired = shifted < 1 ? alphaScale / (1 - shifted) : 0;
            float scaled = float.Lerp(desired, densityScale, t);

            // The altitude layers' light fades with the sun as it sets.
            float sunFade = 1;
            if (definition.HeightTrigger > 0)
                sunFade = 1 - Math.Clamp((.05f - frame.SunDirection.Z) / (.05f - -.1f), 0, 1);

            Matrix4x4 rotation = Rotation(definition, animation);
            if (definition.HeightTrigger > 0)
            {
                // The camera's place against the hat's turn (Multiply(cameraScroll, rotation): along its X and Y axes).
                uv1.Z = Vector3.Dot(cameraScroll, new Vector3(rotation.M11, rotation.M12, rotation.M13));
                uv1.W = Vector3.Dot(cameraScroll, new Vector3(rotation.M21, rotation.M22, rotation.M23));
            }
            // Where the hat is: its offset from the camera, following it by the manager's scaler (up, by the layer's
            // share of it), turned, then each axis scaled by the hat's scale, relative to the camera.
            Vector3 follow = CamPositionScaler * new Vector3(1, 1, definition.CamPositionScalerAdjust);
            Vector3 place = Hat.Position + camera * follow;
            Vector3 Axis(float x, float y, float z) => new Vector3(x, y, z) * Hat.Scale;
            Vector3 axisX = Axis(rotation.M11, rotation.M12, rotation.M13), axisY = Axis(rotation.M21, rotation.M22, rotation.M23),
                axisZ = Axis(rotation.M31, rotation.M32, rotation.M33);

            for (int j = 0; j < layer.Geometries.Length; j++)
            {
                GtaCloudHatGeometry geometry = layer.Geometries[j];
                Span<Vector4> v = values.Slice((first + j) * (Bytes / 16), Bytes / 16);
                v.Clear();
                v[0] = new(frame.SkyColor * scale.X, 0);
                // "East" is east less west, so the shader needn't take one from the other.
                v[1] = new(frame.EastColor * scale.Z - frame.WestColor * scale.W, 0);
                v[2] = new(frame.WestColor * scale.W, 0);
                v[3] = new(frame.SunDirection, 0);
                v[4] = new(frame.LightColor * sunFade, 0);
                v[5] = new(frame.CloudColor, 0);
                v[6] = new(frame.AmbientColor, 0);
                v[7] = new(frame.BounceColor * scale.Y, 0);
                // The game's global cloud alpha is one; there's a depth buffer, so the soft clouds use it.
                v[8] = new(shifted, scaled, 1, 0);
                v[9] = scattering;
                v[10] = frame.PiercingLightPowerStrengthNormalStrengthThickness;
                v[11] = new(frame.ScaleDiffuseFillAmbient, 0);
                v[12] = wrapLighting;
                v[13] = nearFarQ;
                v[14] = new(combine, 0);
                v[15] = new(sculpt, 0);
                v[16] = new(Hat.AnimBlendWeights * show, 0);
                v[17] = uv0;
                v[18] = uv1;
                // The camera, with the inverted depth flag (the viewer's depth is reversed).
                v[19] = new(camera, 1);
                geometry.Material.CopyTo(v[20..25]);
                v[25] = new(axisX, 0);
                v[26] = new(axisY, 0);
                v[27] = new(axisZ, 0);
                v[28] = new(place - camera, 1);
                CloudHatEffect effect = geometry.Effect;
                v[29] = new(effect.Animated ? 1 : 0, effect.Soft ? 1 : 0, effect.Infinite ? 1 : 0, effect.CameraScroll ? 1 : 0);
                v[30] = new(waterLevel, 0, 0, 0);
                drawn[first + j] = true;
                count++;
            }
        }
        return count;
    }
}
