using FiveFury.Cryptography;
using RedFury.Rpf;

namespace HikariHopper.Core;

/// <summary>
/// The keys a game's archives are opened with, whatever the format: what <see cref="ArchiveNode"/> takes and passes on
/// to the archives nested in it. GTA V's (<see cref="GameCrypto"/>) convert to it as they are.
/// </summary>
public abstract record ArchiveKeys
{
    public static implicit operator ArchiveKeys?(GameCrypto? crypto) => crypto is null ? null : new Rpf7Keys(crypto);
}

/// <summary>GTA V's keys, for its RPF7 archives.</summary>
public sealed record Rpf7Keys(GameCrypto Crypto) : ArchiveKeys;

/// <summary>Red Dead Redemption 2's keys, for its RPF8 archives, read through RedFury.</summary>
public sealed record Rdr2Keys(Rdr2Crypto Crypto) : ArchiveKeys
{
    private static readonly Lazy<Rdr2Keys> Shipped = new(() =>
    {
        using Stream stream = typeof(Rdr2Keys).Assembly.GetManifestResourceStream("HikariHopper.rpf8.dat")
            ?? throw new InvalidOperationException("The RDR2 key tables aren't in this build");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return new Rdr2Keys(Rdr2Crypto.Read(copy.ToArray()));
    });

    /// <summary>The tables HikariHopper ships, for <see cref="GameSettings.Rdr2SupportedBuild"/> of the game.</summary>
    public static Rdr2Keys Bundled => Shipped.Value;
}
