using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FiveFury.Cryptography;

namespace HikariHopper.Core;

/// <summary>
/// Builds FiveFury <see cref="GameCrypto"/> instances from the user's own game executable.
/// </summary>
/// <remarks>
/// The PC AES key is located in <c>GTA5.exe</c>/<c>GTA5_Enhanced.exe</c> by its SHA-1 digest and
/// cached in <c>%LOCALAPPDATA%\fivefury\keys.json</c> (the cache format FiveFury Python already
/// uses, keyed by executable path, size and mtime). The NG tables ship only as the embedded
/// <c>keys.bin</c>, which is AES-encrypted with that key and therefore useless without the game;
/// see <c>tools/MakeKeyBlob.cs</c>.
/// </remarks>
public static class GameKeys
{
    private static readonly byte[] AesKeySha1 = Convert.FromHexString("A0796128A775720AC204D9819F68C172E3952C6D");
    private static readonly ConcurrentDictionary<string, GameCrypto> CryptoByKey = new(StringComparer.Ordinal);
    private static readonly Lock CacheLock = new();

    /// <summary>Location of the shared AES key cache; tests may redirect it.</summary>
    public static string CachePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fivefury", "keys.json");

    /// <summary>Loads (or reuses) the crypto for <paramref name="executable"/>; the instance lives for the process.</summary>
    public static GameCrypto Load(string executable)
    {
        string path = Path.GetFullPath(executable);
        byte[] aesKey = CachedAesKey(path) ?? FindAesKey(path);
        return FromAesKey(aesKey);
    }

    /// <summary>Crypto from any game whose key is already cached, for archives opened without a game.</summary>
    public static GameCrypto? TryLoadDefault()
    {
        if (!CryptoByKey.IsEmpty)
            return CryptoByKey.Values.First();
        foreach (var item in ReadCache())
        {
            if (item.Value?["aes_key"]?.GetValue<string>() is not string encoded)
                continue;
            try
            {
                byte[] key = Convert.FromBase64String(encoded);
                if (key.Length == 32 && SHA1.HashData(key).AsSpan().SequenceEqual(AesKeySha1))
                    return FromAesKey(key);
            }
            catch (Exception exception) when (exception is FormatException or InvalidDataException) { }
        }
        return null;
    }

    private static GameCrypto FromAesKey(byte[] aesKey) =>
        CryptoByKey.GetOrAdd(Convert.ToBase64String(aesKey), _ =>
        {
            var (decrypt, encrypt) = ReadTables(aesKey);
            return new GameCrypto(aesKey, decrypt, encrypt);
        });

    private static (byte[] Decrypt, byte[] Encrypt) ReadTables(byte[] aesKey)
    {
        using Stream resource = typeof(GameKeys).Assembly.GetManifestResourceStream("HikariHopper.keys.bin")
            ?? throw new InvalidOperationException("The embedded NG key blob is missing from this build.");
        using var sealedData = new MemoryStream();
        resource.CopyTo(sealedData);
        byte[] plain;
        using (var aes = Aes.Create())
        {
            aes.Key = aesKey;
            try
            { plain = aes.DecryptEcb(sealedData.ToArray(), PaddingMode.PKCS7); }
            catch (CryptographicException exception)
            { throw new InvalidDataException("The game executable's AES key does not unlock the NG key tables.", exception); }
        }
        using var inflate = new ZLibStream(new MemoryStream(plain), CompressionMode.Decompress);
        Span<byte> header = stackalloc byte[16];
        inflate.ReadExactly(header);
        if (!header[..4].SequenceEqual("HHNG"u8) || BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != 1)
            throw new InvalidDataException("The NG key tables have an unknown layout.");
        int decryptLength = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
        int encryptLength = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
        if (decryptLength != GameCrypto.NgDecryptionDataSize || encryptLength != GameCrypto.NgEncryptionDataSize)
            throw new InvalidDataException("The NG key tables do not match FiveFury's GameCrypto layout.");
        byte[] decrypt = new byte[decryptLength];
        byte[] encrypt = new byte[encryptLength];
        inflate.ReadExactly(decrypt);
        inflate.ReadExactly(encrypt);
        return (decrypt, encrypt);
    }

    private static byte[]? CachedAesKey(string executable)
    {
        var info = new FileInfo(executable);
        if (!info.Exists)
            throw new FileNotFoundException($"Could not locate a GTA V executable: {executable}", executable);
        JsonNode? item = ReadCache()[executable.ToLowerInvariant()];
        if (item is null || ReadLong(item["size"]) != info.Length || ReadLong(item["mtime_ns"]) != MtimeNs(info))
            return null;
        try
        {
            byte[] key = Convert.FromBase64String(item["aes_key"]?.GetValue<string>() ?? "");
            return key.Length == 32 && SHA1.HashData(key).AsSpan().SequenceEqual(AesKeySha1) ? key : null;
        }
        catch (FormatException) { return null; }
    }

    private static byte[] FindAesKey(string executable)
    {
        byte[] image = File.ReadAllBytes(executable);
        byte[] found = Search(image) ?? throw new InvalidDataException($"Unable to locate AES key in {executable}");
        WriteCache(executable, found);
        return found;

        static byte[]? Search(byte[] image)
        {
            Span<byte> digest = stackalloc byte[20];
            for (int offset = 0; offset + 32 <= image.Length; offset += 8)
            {
                SHA1.HashData(image.AsSpan(offset, 32), digest);
                if (digest.SequenceEqual(AesKeySha1))
                    return image.AsSpan(offset, 32).ToArray();
            }
            return null;
        }
    }

    private static JsonObject ReadCache()
    {
        lock (CacheLock)
        {
            try
            { return JsonNode.Parse(File.ReadAllText(CachePath)) as JsonObject ?? new JsonObject(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            { return new JsonObject(); }
        }
    }

    private static void WriteCache(string executable, byte[] aesKey)
    {
        var info = new FileInfo(executable);
        lock (CacheLock)
        {
            try
            {
                JsonObject cache = ReadCache();
                cache[executable.ToLowerInvariant()] = new JsonObject
                {
                    ["aes_key"] = Convert.ToBase64String(aesKey),
                    ["mtime_ns"] = MtimeNs(info),
                    ["size"] = info.Length,
                };
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                File.WriteAllText(CachePath, cache.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    // Python's st_mtime_ns on Windows: 100 ns FILETIME ticks since the Unix epoch.
    private static long MtimeNs(FileInfo info) => (info.LastWriteTimeUtc - DateTime.UnixEpoch).Ticks * 100;

    private static long ReadLong(JsonNode? node) => node switch
    {
        null => -1,
        JsonValue value when value.TryGetValue(out long number) => number,
        JsonValue value when value.TryGetValue(out string? text) && long.TryParse(text, out long parsed) => parsed,
        _ => -1,
    };
}
