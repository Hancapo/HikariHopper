using FiveFury.Cryptography;

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
