namespace HikariHopper.Rendering;

/// <summary>
/// How Red Dead Redemption 2 exposes and tone-maps its world, as its post-process works the exposure out
/// (PS_CalculateExposure): the stops for a scene of luminance L, scale ln(L + horizontal offset) + vertical offset - 10 +
/// bending L, from its curve and blend curve (by the timecycle's weight), plus the timecycle's tweak, kept in its range;
/// eased towards by the gap times its step multiplier a second, at least its least step and at most its most, taken at
/// once at its threshold. Then the bright and dark filmic curves, as GTA V's are.
/// </summary>
/// <param name="Curve">Exposure.curve's scale, vertical offset, horizontal offset and bending.</param>
/// <param name="BlendCurve">Exposure.curve.blend's, the same.</param>
internal sealed record Rdr2Exposure(System.Numerics.Vector4 Curve, System.Numerics.Vector4 BlendCurve, float BlendWeight, float Tweak,
    float Min, float Max, float StepMult, float MaxStep, float MinStep, float Threshold, GtaToneMapVisuals ToneMap)
{
    /// <summary>The stops for a scene of this luminance, before the adaptation.</summary>
    public float TargetFor(float luminance)
    {
        static float Of(System.Numerics.Vector4 curve, float luminance) =>
            curve.X * MathF.Log(MathF.Max(luminance + curve.Z, 1e-30f)) + curve.Y - 10 + curve.W * luminance;
        float main = Of(Curve, luminance), blend = Of(BlendCurve, luminance);
        return Math.Clamp(main + (blend - main) * BlendWeight + Tweak, Min, Max);
    }
}
