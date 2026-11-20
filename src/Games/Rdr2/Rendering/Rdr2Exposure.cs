namespace HikariHopper.Rendering;

/// <summary>How Red Dead Redemption 2 grades its frame at an hour (Rdr2Sky.GradeAt).</summary>
/// <param name="Correct">The colour correction and its cut-off.</param>
/// <param name="Shift">The colour shift and its cut-off.</param>
/// <param name="WeatherWeights">The weather tables' weights, [temperature + 3 humidity].</param>
/// <param name="Scotopic">The scotopic colour, and its blue shift's intensity.</param>
/// <param name="ScotopicWeight">The scotopic weight's scale and bias.</param>
/// <param name="Gradient">The gradient filter the frame is multiplied by before its exposure.</param>
internal sealed record Rdr2GradeValues(System.Numerics.Vector4 Correct, System.Numerics.Vector4 Shift, float Desaturation, float[] WeatherWeights,
    System.Numerics.Vector4 Scotopic, System.Numerics.Vector2 ScotopicWeight, Rdr2Gradient Gradient);

/// <summary>
/// Red Dead Redemption 2's gradient filter at an hour, as its post-process sets it up for PS_Composite
/// (GradientFilterColTop, Middle, Bottom and Extra): the colour its HDR frame is multiplied by before the exposure,
/// blended down the screen from the top colour through the middle's to the bottom's (Post/tonemap.wgsl's
/// rdr2_gradient).
/// </summary>
/// <param name="Top">The timecycle's top colour times its fade, raised to the front end's gamma (1/2.2), times its
/// multiplier; and one less twice the top-middle midpoint.</param>
/// <param name="Middle">The middle colour times its fade and its multiplier, not raised; and where it sits down the screen.</param>
/// <param name="Bottom">As the top's, from the bottom colour and the middle-bottom midpoint.</param>
/// <param name="Extra">One over one less where the middle sits, and one over where it sits.</param>
internal sealed record Rdr2Gradient(System.Numerics.Vector4 Top, System.Numerics.Vector4 Middle, System.Numerics.Vector4 Bottom,
    System.Numerics.Vector2 Extra);

/// <summary>
/// How Red Dead Redemption 2 exposes and tone-maps its world, as its post-process works the exposure out
/// (PS_CalculateExposure): the stops for a scene of luminance L, scale ln(L + horizontal offset) + vertical offset - 10 +
/// bending L, from its curve and blend curve (by the timecycle's weight), then blended with its curve for the luminance
/// round the camera (its cube's) by the timecycle's cubemap amount, plus the timecycle's tweak, kept in its range; eased
/// towards by the gap times its step multiplier a second, at least its least step and at most its most, taken at once at
/// its threshold. Then the bright and dark filmic curves, as GTA V's are. L is metered over the timecycle's spot meter,
/// the darkest lowlights and brightest highlights fractions of it left out (avgLuminanceCompute's histogram).
/// </summary>
/// <param name="Curve">Exposure.curve's scale, vertical offset, horizontal offset and bending.</param>
/// <param name="BlendCurve">Exposure.curve.blend's, the same.</param>
internal sealed record Rdr2Exposure(System.Numerics.Vector4 Curve, System.Numerics.Vector4 BlendCurve, float BlendWeight, float Tweak,
    float Min, float Max, float StepMult, float MaxStep, float MinStep, float Threshold, GtaToneMapVisuals ToneMap)
{
    /// <summary>The spot meter over the frame, from its top left to its bottom right (0 to 1).</summary>
    public System.Numerics.Vector4 SpotMeter { get; init; } = new(0, 0, 1, 1);

    /// <summary>The fractions of the metered pixels left out, the darkest and the brightest.</summary>
    public float Lowlights { get; init; }

    /// <inheritdoc cref="Lowlights"/>
    public float Highlights { get; init; }

    /// <summary>The least luminance metered (visualsettings.dat's Exposure.AbsoluteMinThreshod).</summary>
    public float AbsoluteMin { get; init; } = 1e-6f;

    /// <summary>How much of the cube's luminance the exposure takes (the timecycle's postfx_exposure_cubemap_amount).</summary>
    public float CubemapAmount { get; init; }

    /// <summary>The stops for a scene metered at this luminance, with this luminance round the camera, before the adaptation.</summary>
    public float TargetFor(float luminance, float cube)
    {
        static float Of(System.Numerics.Vector4 curve, float luminance) =>
            curve.X * MathF.Log(MathF.Max(luminance + curve.Z, 1e-30f)) + curve.Y - 10 + curve.W * luminance;
        float main = Of(Curve, luminance), blend = Of(BlendCurve, luminance);
        float blended = main + (blend - main) * BlendWeight;
        blended += (Of(Curve, cube) - blended) * CubemapAmount;
        return Math.Clamp(blended + Tweak, Min, Max);
    }
}
