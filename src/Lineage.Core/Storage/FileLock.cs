using System.Security.Cryptography;
using System.Text;

namespace Lineage.Core.Storage;

/// <summary>The password given for a locked file was wrong (or the file was damaged; the two can't be told apart).</summary>
public sealed class WrongPasswordException() : Exception("That password isn't right for this file.");

/// <summary>The file is password-protected and no password was given.</summary>
public sealed class PasswordNeededException() : Exception("This file is password-protected.");

/// <summary>
/// A password on a .lineage file. A locked file is the whole tree encrypted as one block with AES-256
/// (CBC, then HMAC-SHA256 over everything, so a wrong password or a damaged file is caught before
/// anything is read). The key comes from the password with PBKDF2-SHA256. The tree is decrypted
/// straight into memory: an unlocked copy never touches the disk. There is no way back in without
/// the password — that is the point, and the reason the program warns about it.
/// <para>Layout: "LRLOCK01", iterations (int), salt (16), IV (16), hint length (int) + hint (UTF-8),
/// encrypted data, HMAC (32).</para>
/// </summary>
public sealed class FileLock
{
    private static readonly byte[] Magic = "LRLOCK01"u8.ToArray();
    public const int DefaultIterations = 600_000;

    /// <summary>A reminder shown when asking for the password. Stored unencrypted, so never the password itself.</summary>
    public string Hint { get; }
    private readonly int _iterations;
    private readonly byte[] _salt;
    private readonly byte[] _encKey;
    private readonly byte[] _macKey;

    private FileLock(string password, byte[] salt, int iterations, string hint)
    {
        Hint = hint;
        _iterations = iterations;
        _salt = salt;
        // Deriving the key is slow on purpose (it is what makes guessing slow), so it is done once
        // here and reused for every save; each save still gets a fresh IV.
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 64);
        _encKey = key[..32];
        _macKey = key[32..];
    }

    /// <summary>A new lock for a password the user just chose.</summary>
    public static FileLock Create(string password, string hint = "", int iterations = DefaultIterations)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("A password can't be empty.", nameof(password));
        return new FileLock(password, RandomNumberGenerator.GetBytes(16), iterations, hint.Trim());
    }

    /// <summary>True for a password-protected file.</summary>
    public static bool IsLocked(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[Magic.Length];
            return f.Read(head, 0, head.Length) == head.Length && head.AsSpan().SequenceEqual(Magic);
        }
        catch (IOException) { return false; }
    }

    /// <summary>The hint saved in a locked file ("" when none).</summary>
    public static string HintOf(string path)
    {
        var data = File.ReadAllBytes(path);
        var (_, _, _, hint, _) = ReadHeader(data);
        return hint;
    }

    internal byte[] Seal(byte[] plain)
    {
        var iv = RandomNumberGenerator.GetBytes(16);
        using var aes = Aes.Create();
        aes.Key = _encKey;
        var cipher = aes.EncryptCbc(plain, iv);
        var hint = Encoding.UTF8.GetBytes(Hint);
        using var output = new MemoryStream();
        output.Write(Magic);
        output.Write(BitConverter.GetBytes(_iterations));
        output.Write(_salt);
        output.Write(iv);
        output.Write(BitConverter.GetBytes(hint.Length));
        output.Write(hint);
        output.Write(cipher);
        output.Write(HMACSHA256.HashData(_macKey, output.ToArray()));
        return output.ToArray();
    }

    /// <summary>Decrypts a locked file's bytes; also returns the lock, so saving keeps the same password.</summary>
    internal static (FileLock Lock, byte[] Plain) Open(byte[] data, string password)
    {
        var (iterations, salt, iv, hint, start) = ReadHeader(data);
        if (data.Length < start + 16 + 32) throw new InvalidDataException("This locked file is damaged (too short).");
        var fileLock = new FileLock(password, salt, iterations, hint);
        var body = data.AsSpan(0, data.Length - 32);
        var mac = HMACSHA256.HashData(fileLock._macKey, body);
        if (!CryptographicOperations.FixedTimeEquals(mac, data.AsSpan(data.Length - 32))) throw new WrongPasswordException();
        using var aes = Aes.Create();
        aes.Key = fileLock._encKey;
        return (fileLock, aes.DecryptCbc(body[start..], iv));
    }

    private static (int Iterations, byte[] Salt, byte[] Iv, string Hint, int DataStart) ReadHeader(byte[] data)
    {
        if (data.Length < Magic.Length + 4 + 16 + 16 + 4 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("This isn't a password-protected Lineage Roots file.");
        var at = Magic.Length;
        var iterations = BitConverter.ToInt32(data, at); at += 4;
        var salt = data[at..(at + 16)]; at += 16;
        var iv = data[at..(at + 16)]; at += 16;
        var hintLength = BitConverter.ToInt32(data, at); at += 4;
        if (iterations <= 0 || hintLength < 0 || at + hintLength > data.Length)
            throw new InvalidDataException("This locked file is damaged.");
        var hint = Encoding.UTF8.GetString(data, at, hintLength); at += hintLength;
        return (iterations, salt, iv, hint, at);
    }
}
