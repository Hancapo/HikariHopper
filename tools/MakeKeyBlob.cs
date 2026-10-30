#:project ../../../FiveFury.NET/FiveFury/FiveFury.csproj
// Builds src/HikariHopper/Assets/Keys/keys.bin: the NG decryption and encryption tables,
// deflated and AES-256-ECB encrypted with the game's PC AES key. The blob is useless
// without the user's own GTA5.exe, from which HikariHopper reads that key at runtime.
//
//   dotnet run tools/MakeKeyBlob.cs -- <GTA5.exe> <ng.dat> <ng_encrypt.dat (zlib)> <output>
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using FiveFury.Cryptography;
using FiveFury.Rpf;

if (args.Length != 4)
{
    Console.Error.WriteLine("usage: MakeKeyBlob <GTA5.exe> <ng.dat> <ng_encrypt.dat> <output>");
    return 2;
}
byte[] aesKey = FindAesKey(args[0]);
byte[] decrypt = File.ReadAllBytes(args[1]);
byte[] encrypt;
using (var input = new ZLibStream(File.OpenRead(args[2]), CompressionMode.Decompress))
using (var buffer = new MemoryStream())
{
    input.CopyTo(buffer);
    encrypt = buffer.ToArray();
}
if (decrypt.Length != GameCrypto.NgDecryptionDataSize || encrypt.Length != GameCrypto.NgEncryptionDataSize)
    throw new InvalidDataException("NG table sizes do not match FiveFury's GameCrypto layout.");

// Prove the tables are self-consistent before publishing them.
using (var crypto = new GameCrypto(aesKey, decrypt, encrypt))
{
    byte[] sample = RandomNumberGenerator.GetBytes(4096);
    byte[] sealedData = crypto.Encrypt(sample, RpfEncryption.Ng, "sample.ydr", sample.Length);
    if (sealedData.AsSpan().SequenceEqual(sample) || !crypto.Decrypt(sealedData, RpfEncryption.Ng, "sample.ydr", sample.Length).AsSpan().SequenceEqual(sample))
        throw new InvalidDataException("NG encryption round trip failed.");
}

using var plain = new MemoryStream();
using (var deflate = new ZLibStream(plain, CompressionLevel.SmallestSize, leaveOpen: true))
{
    Span<byte> header = stackalloc byte[16];
    "HHNG"u8.CopyTo(header);
    BinaryPrimitives.WriteInt32LittleEndian(header[4..], 1);
    BinaryPrimitives.WriteInt32LittleEndian(header[8..], decrypt.Length);
    BinaryPrimitives.WriteInt32LittleEndian(header[12..], encrypt.Length);
    deflate.Write(header);
    deflate.Write(decrypt);
    deflate.Write(encrypt);
}
using var aes = Aes.Create();
aes.Key = aesKey;
File.WriteAllBytes(args[3], aes.EncryptEcb(plain.ToArray(), PaddingMode.PKCS7));
Console.WriteLine($"Wrote {new FileInfo(args[3]).Length:N0} bytes to {args[3]}");
return 0;

static byte[] FindAesKey(string executable)
{
    byte[] expected = Convert.FromHexString("A0796128A775720AC204D9819F68C172E3952C6D");
    byte[] image = File.ReadAllBytes(executable);
    for (int offset = 0; offset + 32 <= image.Length; offset += 8)
        if (SHA1.HashData(image.AsSpan(offset, 32)).AsSpan().SequenceEqual(expected))
            return image.AsSpan(offset, 32).ToArray();
    throw new InvalidDataException($"Unable to locate the AES key in {executable}");
}
