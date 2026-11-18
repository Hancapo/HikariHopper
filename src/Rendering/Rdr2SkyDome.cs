using System.Numerics;
using RedFury.Rpf;
using RedFury.World;
using GameTexture = FiveFury.Textures.Texture;
using RedGeometry = RedFury.Drawables.DrawableGeometry;

namespace HikariHopper.Rendering;

/// <summary>
/// The dome Red Dead Redemption 2 draws its sky on (models/skydome.ydd: a unit sphere, its coordinates the sky's angle
/// from the zenith out to the horizon at a half and round by its bearing) and the sky's textures (textures/skydome.ytd):
/// the stars' (the galaxy in red, the sparse stars in green, the dense in alpha), the air glow's and the moon's.
/// </summary>
/// <param name="Vertices">Each vertex's place on the unit sphere and its coordinates, <see cref="Stride"/> bytes apart.</param>
internal sealed record Rdr2SkyDome(float[] Vertices, uint[] Indices, GameTexture? Stars, GameTexture? AirGlow, GameTexture? Moon)
{
    /// <summary>A vertex's bytes: its place, then its coordinates.</summary>
    public const int Stride = 5 * sizeof(float);

    internal const string ModelPath = "platform:/models/skydome.ydd", TexturesPath = "platform:/textures/skydome.ytd";

    /// <summary>The game's dome and textures, read through <paramref name="sources"/>; null without its model.</summary>
    public static Rdr2SkyDome? Read(WorldGameSource sources, Rdr2Crypto crypto)
    {
        if (sources.Find(ModelPath) is not [.., var model])
            return null;
        RedFury.Ydd.Ydd dome = model.Load(crypto, entry => RedFury.Ydd.Ydd.FromEntry(entry));
        var vertices = new List<float>();
        var indices = new List<uint>();
        foreach (var member in dome.Drawables)
            foreach (var part in member.Drawable.Models())
                foreach (RedGeometry geometry in part.Geometries)
                {
                    Vector3[] positions = geometry.Vertices.DecodePositions();
                    Vector4[]? uv = geometry.Vertices.Decode(RedFury.Drawables.VertexSemantic.TexCoord0);
                    if (uv is null || uv.Length < positions.Length)
                        continue;
                    uint first = (uint)(vertices.Count / 5);
                    for (int i = 0; i < positions.Length; i++)
                        vertices.AddRange([positions[i].X, positions[i].Y, positions[i].Z, uv[i].X, uv[i].Y]);
                    foreach (uint index in geometry.Indices.Decode())
                        indices.Add(first + index);
                }
        if (indices.Count == 0)
            return null;
        GameTexture? stars = null, airGlow = null, moon = null;
        if (sources.Find(TexturesPath) is [.., var dictionary])
            foreach (RedFury.Textures.Texture texture in dictionary.Load(crypto, entry => RedFury.Ytd.Ytd.FromEntry(entry)).Textures)
                switch (texture.Name.ToLowerInvariant())
                {
                    case "stars":
                        stars = Rdr2Textures.Of(texture);
                        break;
                    case "airglow":
                        airGlow = Rdr2Textures.Of(texture);
                        break;
                    case "moon-new":
                        moon = Rdr2Textures.Of(texture);
                        break;
                }
        return new Rdr2SkyDome([.. vertices], [.. indices], stars, airGlow, moon);
    }

    /// <summary>
    /// A sphere with the game's coordinates, for a sky without the game's dome: <paramref name="rings"/> from the zenith
    /// to the nadir, <paramref name="segments"/> round.
    /// </summary>
    public static Rdr2SkyDome Sphere(int rings = 32, int segments = 64)
    {
        var vertices = new List<float>();
        var indices = new List<uint>();
        for (int ring = 0; ring <= rings; ring++)
        {
            float angle = MathF.PI * ring / rings;
            for (int segment = 0; segment <= segments; segment++)
            {
                float bearing = 2 * MathF.PI * segment / segments;
                var place = new Vector3(MathF.Sin(angle) * MathF.Cos(bearing), MathF.Sin(angle) * MathF.Sin(bearing), MathF.Cos(angle));
                Vector2 uv = Coordinates(place);
                vertices.AddRange([place.X, place.Y, place.Z, uv.X, uv.Y]);
            }
        }
        for (int ring = 0; ring < rings; ring++)
            for (int segment = 0; segment < segments; segment++)
            {
                uint a = (uint)(ring * (segments + 1) + segment), b = a + (uint)segments + 1;
                indices.AddRange([a, b, a + 1, a + 1, b, b + 1]);
            }
        return new Rdr2SkyDome([.. vertices], [.. indices], null, null, null);
    }

    /// <summary>
    /// The game's dome coordinates of a way from its centre: from the middle, the angle from the zenith over pi, towards
    /// minus Y and minus X.
    /// </summary>
    internal static Vector2 Coordinates(Vector3 place)
    {
        float across = MathF.Sqrt(place.X * place.X + place.Y * place.Y);
        float angle = MathF.Atan2(across, place.Z) / MathF.PI;
        return across > 1e-6f ? new Vector2(.5f - place.Y / across * angle, .5f - place.X / across * angle) : new Vector2(.5f);
    }
}
