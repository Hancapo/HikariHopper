using System.Numerics;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Rendering;

// Red Dead Redemption 2's colour grading after the tone map (Post/tonemap.wgsl's rdr2_display): in the display's sRGB, the
// scotopic shift towards the timecycle's night colour where the scene is dark, then its levels table and its weather's
// (Rdr2Grading.cs), each baked as the game bakes it when its values change. Off (and the tables the identity) without an
// RDR2 world's sky.
internal sealed unsafe partial class ViewportRenderer
{
    // The grading's constants (Post/tonemap.wgsl's Rdr2Grade): whether it's on; the scotopic colour and its intensity; the
    // scotopic weight's scale and bias.
    private const int Rdr2GradeBytes = 48;

    private Buffer? rdr2GradeBuffer;
    private WebGpuSharp.Texture? rdr2LevelsTable, rdr2WeatherTable;
    private TextureView? rdr2LevelsView, rdr2WeatherView;
    // What the tables were last baked from, so they're baked again only when it changes.
    private (Vector4 Correct, Vector4 Shift, float Desaturation)? rdr2LevelsBaked;
    private (Rdr2Grading Grading, float[] Weights)? rdr2WeatherBaked;
    // The grading of a sky without the game's weather tables: the identity, the same one every frame.
    private static readonly Rdr2Grading NoRdr2WeatherTables = Rdr2Grading.Of(new float[]?[9]);

    private void CreateRdr2Grading()
    {
        rdr2GradeBuffer = device.CreateBuffer(new BufferDescriptor { Size = Rdr2GradeBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        WebGpuSharp.Texture Table() => device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(Rdr2Grading.Width, Rdr2Grading.Height, 1),
            Format = TextureFormat.RGBA16Float,
            MipLevelCount = 1,
            SampleCount = 1,
        })!;
        rdr2LevelsTable = Table();
        rdr2WeatherTable = Table();
        rdr2LevelsView = rdr2LevelsTable.CreateView();
        rdr2WeatherView = rdr2WeatherTable.CreateView();
        float[] identity = Rdr2Grading.Levels(Vector4.One, Vector4.One, 1);
        WriteRdr2Table(rdr2LevelsTable, identity);
        WriteRdr2Table(rdr2WeatherTable, identity);
        queue.WriteBuffer(rdr2GradeBuffer, 0, (ReadOnlySpan<Vector4>)[Vector4.Zero, Vector4.Zero, Vector4.Zero]);
    }

    private void WriteRdr2Table(WebGpuSharp.Texture table, float[] texels)
    {
        var halves = new Half[texels.Length];
        for (int i = 0; i < texels.Length; i++)
            halves[i] = (Half)texels[i];
        queue.WriteTexture(new TexelCopyTextureInfo { Texture = table },
            System.Runtime.InteropServices.MemoryMarshal.AsBytes((ReadOnlySpan<Half>)halves),
            new TexelCopyBufferLayout { BytesPerRow = Rdr2Grading.Width * 8, RowsPerImage = Rdr2Grading.Height },
            new Extent3D(Rdr2Grading.Width, Rdr2Grading.Height, 1));
    }

    /// <summary>The grading for this frame: on with an RDR2 world's sky, its tables baked again where they've changed.</summary>
    private void WriteRdr2Grade()
    {
        if (!worldShown || rdr2Sky is not { } sky)
        {
            queue.WriteBuffer(rdr2GradeBuffer!, 0, (ReadOnlySpan<Vector4>)[Vector4.Zero, Vector4.Zero, Vector4.Zero]);
            return;
        }
        Rdr2GradeValues grade = sky.Sky.GradeAt(Hours);
        var levels = (grade.Correct, grade.Shift, grade.Desaturation);
        if (rdr2LevelsBaked != levels)
        {
            WriteRdr2Table(rdr2LevelsTable!, Rdr2Grading.Levels(grade.Correct, grade.Shift, grade.Desaturation));
            rdr2LevelsBaked = levels;
        }
        Rdr2Grading tables = sky.Sky.Grading ?? NoRdr2WeatherTables;
        if (rdr2WeatherBaked is not { } baked || !ReferenceEquals(baked.Grading, tables) || !baked.Weights.AsSpan().SequenceEqual(grade.WeatherWeights))
        {
            WriteRdr2Table(rdr2WeatherTable!, tables.Weather(grade.WeatherWeights));
            rdr2WeatherBaked = (tables, grade.WeatherWeights);
        }
        queue.WriteBuffer(rdr2GradeBuffer!, 0, (ReadOnlySpan<Vector4>)[Vector4.UnitX, grade.Scotopic,
            new Vector4(grade.ScotopicWeight, 0, 0)]);
    }
}
