using System.Buffers.Binary;
using System.Text;

namespace Lineage.Core.Import.Aq;

/// <summary>
/// Low-level, read-only access to an AQ 16 (.aq) file: header, block maps,
/// NAMZ string heaps, fixed-size record addressing and deleted-record markers.
/// Every layout detail here is documented (with evidence) in docs/AQ16-teardown.md.
/// </summary>
public sealed class AqFile
{
    public const int HeaderSize = 0x800;
    private const int MapBase = 0x2800, BlockSize = 0x2000, BlocksPerSegment = 0x2000;
    private const int SegmentSize = 0x4000000;
    private static readonly byte[] Signature = "AQ\0\0100\0"u8.ToArray();

    private readonly byte[] _data;
    private readonly Dictionary<int, List<int>> _blocksByType = new();

    public AqFile(byte[] data)
    {
        _data = data;
        if (data.Length < MapBase || !data.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("Not an AQ database (bad signature).");
        Version = Encoding.ASCII.GetString(data, 8, 4);
        if (Version != "3600")
            throw new NotSupportedException($"AQ format '{Version}' is not supported; only AQ 16 ('3600') is.");
        for (var seg = 0; MapBase + (long)seg * SegmentSize < data.Length; seg++)
        {
            var mapOffset = MapBase + seg * SegmentSize;
            for (var i = 0; i < BlocksPerSegment && mapOffset + i < data.Length; i++)
            {
                int type = data[mapOffset + i];
                if (type == 0 || BlockOffset(seg * BlocksPerSegment + i) >= data.Length) continue;
                if (!_blocksByType.TryGetValue(type, out var list)) _blocksByType[type] = list = new List<int>();
                list.Add(seg * BlocksPerSegment + i);
            }
        }
    }

    public static AqFile Open(string path) => new(File.ReadAllBytes(path));

    public string Version { get; }
    public ReadOnlySpan<byte> Header => _data.AsSpan(0, HeaderSize);
    public uint HeaderU32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset, 4));
    public string DatabaseName =>
        Encoding.Unicode.GetString(_data, 0x31C, 256).Split('\0')[0];

    private static long BlockOffset(int globalBlock) =>
        MapBase + (long)(globalBlock / BlocksPerSegment) * SegmentSize + (long)(globalBlock % BlocksPerSegment) * BlockSize;

    /// <summary>Raw bytes of 1-based record <paramref name="number"/>, or null if out of range.</summary>
    public byte[]? Record(RecordType t, long number)
    {
        if (number < 1) return null;
        if (!_blocksByType.TryGetValue(t.BlockType, out var blocks)) return null;
        var n = number - 1;
        var blockIndex = n / t.PerBlock;
        if (blockIndex >= blocks.Count) return null;
        var offset = BlockOffset(blocks[(int)blockIndex]) + n % t.PerBlock * t.Size;
        if (offset + t.Size > _data.Length) return null;
        return _data.AsSpan((int)offset, t.Size).ToArray();
    }

    /// <summary>Decodes the NAMZ heap node at an absolute file offset (UTF-8, cp1252 fallback).</summary>
    public string HeapString(uint offset)
    {
        if (offset == 0) return "";
        if (offset + 19 > _data.Length || !_data.AsSpan((int)offset, 4).SequenceEqual("NAMZ"u8))
            throw new InvalidDataException($"No string at 0x{offset:X}");
        var len = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan((int)offset + 17, 2));
        return DecodeText(_data.AsSpan((int)offset + 19, Math.Min(len, _data.Length - (int)offset - 19)));
    }

    private static readonly Encoding Cp1252 = CodePagesEncoding();

    private static Encoding CodePagesEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
        catch (Exception) { return Encoding.Latin1; }
    }

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// AQ 16 stores UTF-8; older imported text that is not valid UTF-8 is cp1252.
    /// Windows line endings are normalised to "\n".
    /// </summary>
    public static string DecodeText(ReadOnlySpan<byte> raw)
    {
        string s;
        try { s = StrictUtf8.GetString(raw); }
        catch (DecoderFallbackException) { s = Cp1252.GetString(raw); }
        return s.Contains('\r') ? s.Replace("\r\n", "\n").Replace('\r', '\n') : s;
    }
}

/// <summary>Size, records-per-block and deleted marker of one AQ record type.</summary>
public readonly record struct RecordType(int BlockType, int Size, int PerBlock, int CountOffset,
                                         int DeletedOffset = -1, byte DeletedMarker = 0)
{
    public bool IsDeleted(byte[] raw) => DeletedOffset >= 0 && raw[DeletedOffset] == DeletedMarker;

    public static readonly RecordType Individual = new(2, 680, 12, 0x010, 0x00, (byte)'D');
    public static readonly RecordType Marriage = new(3, 174, 47, 0x020, 0x00, (byte)'D');
    public static readonly RecordType Note = new(4, 255, 32, 0x0CC);
    public static readonly RecordType FamilyLink = new(5, 64, 127, 0x110, 0x00, (byte)'D');
    public static readonly RecordType Citation = new(6, 70, 116, 0x0D8, 0x00, (byte)'Y');
    public static readonly RecordType Source = new(7, 142, 57, 0x0E4, 0x71, (byte)'Y');
    public static readonly RecordType Repository = new(8, 64, 127, 0x0F4, 0x2E, (byte)'Y');
    public static readonly RecordType UserEvent = new(12, 198, 41, 0x104);
    public static readonly RecordType EventType = new(13, 56, 146, 0x13C);
    public static readonly RecordType Contact = new(15, 60, 136, 0x15C, 0x2C, (byte)'D');
    public static readonly RecordType CitationLink = new(16, 44, 186, 0x168);
    public static readonly RecordType Scrapbook = new(17, 68, 120, 0x128);
    public static readonly RecordType ResearchXref = new(27, 36, 227, 0x1F4, 0x00, (byte)'D');
    public static readonly RecordType Category = new(28, 16, 511, 0x1DC, 0x00, (byte)'D');
    public static readonly RecordType ResearchItem = new(29, 100, 81, 0x1E8, 0x00, (byte)'D');
    public static readonly RecordType Dna = new(31, 454, 18, 0);
    public static readonly RecordType FamilySearch = new(33, 92, 88, 0, 0x04, (byte)'D');
}
