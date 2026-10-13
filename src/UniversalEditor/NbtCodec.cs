using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace UniversalEditor;

internal enum NbtType : byte { End, Byte, Short, Int, Long, Float, Double, ByteArray, String, List, Compound, IntArray, LongArray }
internal enum NbtCompression { None, Gzip, Zlib }
internal sealed class NbtTag(NbtType type, string name, object value)
{
    public NbtType Type { get; } = type;
    public string Name { get; } = name;
    public object Value { get; set; } = value;
    public NbtType ElementType { get; set; }
    public string DisplayValue() => Type switch
    {
        NbtType.Compound or NbtType.List => $"{((List<NbtTag>)Value).Count} entries",
        NbtType.ByteArray => string.Join(", ", ((byte[])Value).Select(b => unchecked((sbyte)b))),
        NbtType.IntArray => string.Join(", ", (int[])Value),
        NbtType.LongArray => string.Join(", ", (long[])Value),
        NbtType.Float => ((float)Value).ToString("R", CultureInfo.InvariantCulture),
        NbtType.Double => ((double)Value).ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? ""
    };
    public void SetText(string text)
    {
        var culture = CultureInfo.InvariantCulture;
        var tokens = text.Split([',', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
        Value = Type switch
        {
            NbtType.Byte => sbyte.Parse(text, culture), NbtType.Short => short.Parse(text, culture),
            NbtType.Int => int.Parse(text, culture), NbtType.Long => long.Parse(text, culture),
            NbtType.Float => float.Parse(text, culture), NbtType.Double => double.Parse(text, culture),
            NbtType.String => text,
            NbtType.ByteArray => tokens.Select(s => unchecked((byte)sbyte.Parse(s, culture))).ToArray(),
            NbtType.IntArray => tokens.Select(s => int.Parse(s, culture)).ToArray(),
            NbtType.LongArray => tokens.Select(s => long.Parse(s, culture)).ToArray(),
            _ => throw new InvalidOperationException("Select a scalar or array value to edit.")
        };
    }
}

internal sealed record NbtDocument(NbtTag Root, NbtCompression Compression);

internal static class NbtCodec
{
    internal const int MaxBytes = 64 * 1024 * 1024;
    private const int MaxEntries = 1_000_000;
    private const int MaxDepth = 64;
    public static NbtDocument ReadFile(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length > MaxBytes) throw new InvalidDataException("This prototype limits NBT files to 64 MB.");
        var header = new byte[2]; var count = input.Read(header); input.Position = 0;
        var compression = count == 2 && header[0] == 0x1f && header[1] == 0x8b ? NbtCompression.Gzip
            : count == 2 && (header[0] & 15) == 8 && ((header[0] << 8) | header[1]) % 31 == 0 ? NbtCompression.Zlib : NbtCompression.None;
        using Stream payload = compression switch
        {
            NbtCompression.Gzip => new GZipStream(input, CompressionMode.Decompress),
            NbtCompression.Zlib => new ZLibStream(input, CompressionMode.Decompress),
            _ => input
        };
        using var data = new MemoryStream();
        var buffer = new byte[8192]; int bytes;
        while ((bytes = payload.Read(buffer)) > 0)
        {
            if (data.Length + bytes > MaxBytes) throw new InvalidDataException("Decompressed NBT exceeds 64 MB.");
            data.Write(buffer, 0, bytes);
        }
        return new NbtDocument(Read(data.ToArray()), compression);
    }
    public static NbtTag Read(byte[] data)
    {
        if (data.Length > MaxBytes) throw new InvalidDataException("NBT exceeds 64 MB.");
        using var reader = new BinaryReader(new MemoryStream(data));
        var budget = MaxEntries;
        var type = ReadType(reader);
        if (type == NbtType.End) throw new InvalidDataException("A root TAG_End has no payload.");
        var name = ReadString(reader);
        var root = ReadPayload(reader, type, name, 0, ref budget);
        if (reader.BaseStream.Position != data.Length) throw new InvalidDataException("Unexpected data after the root NBT tag; region/network formats require separate readers.");
        return root;
    }
    public static byte[] Write(NbtTag root)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)root.Type); WriteString(writer, root.Name); WritePayload(writer, root, 0);
        if (stream.Length > MaxBytes) throw new InvalidDataException("NBT exceeds 64 MB.");
        return stream.ToArray();
    }
    public static void SaveCopy(NbtDocument document, string destination)
    {
        var data = Write(document.Root);
        // CreateNew makes saving a copy incapable of overwriting an existing world file.
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        using Stream payload = document.Compression switch
        {
            NbtCompression.Gzip => new GZipStream(output, CompressionLevel.Optimal),
            NbtCompression.Zlib => new ZLibStream(output, CompressionLevel.Optimal),
            _ => output
        };
        payload.Write(data);
    }
    private static NbtType ReadType(BinaryReader reader)
    {
        var value = reader.ReadByte();
        if (value > 12) throw new InvalidDataException($"Unknown NBT tag type {value}.");
        return (NbtType)value;
    }
    private static byte[] Exact(BinaryReader reader, int count)
    {
        if (count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new EndOfStreamException("Truncated NBT payload.");
        return reader.ReadBytes(count);
    }
    private static short Int16(BinaryReader r) => BinaryPrimitives.ReadInt16BigEndian(Exact(r, 2));
    private static int Int32(BinaryReader r) => BinaryPrimitives.ReadInt32BigEndian(Exact(r, 4));
    private static long Int64(BinaryReader r) => BinaryPrimitives.ReadInt64BigEndian(Exact(r, 8));
    private static int Length(BinaryReader r)
    {
        var length = Int32(r);
        if (length < 0 || length > MaxEntries) throw new InvalidDataException("Invalid or excessive NBT collection length.");
        return length;
    }
    private static NbtTag ReadPayload(BinaryReader r, NbtType type, string name, int depth, ref int budget)
    {
        if (depth > MaxDepth || --budget < 0) throw new InvalidDataException("NBT nesting or entry limit exceeded.");
        object value;
        var elementType = NbtType.End;
        switch (type)
        {
            case NbtType.Byte: value = r.ReadSByte(); break;
            case NbtType.Short: value = Int16(r); break;
            case NbtType.Int: value = Int32(r); break;
            case NbtType.Long: value = Int64(r); break;
            case NbtType.Float: value = BitConverter.Int32BitsToSingle(Int32(r)); break;
            case NbtType.Double: value = BitConverter.Int64BitsToDouble(Int64(r)); break;
            case NbtType.String: value = ReadString(r); break;
            case NbtType.ByteArray: value = Exact(r, Length(r)); break;
            case NbtType.IntArray:
                var ints = new int[Length(r)]; for (var i = 0; i < ints.Length; i++) ints[i] = Int32(r); value = ints; break;
            case NbtType.LongArray:
                var longs = new long[Length(r)]; for (var i = 0; i < longs.Length; i++) longs[i] = Int64(r); value = longs; break;
            case NbtType.List:
                elementType = ReadType(r); var size = Length(r);
                if (elementType == NbtType.End && size != 0) throw new InvalidDataException("A nonempty list cannot contain TAG_End.");
                var list = new List<NbtTag>();
                for (var i = 0; i < size; i++) list.Add(ReadPayload(r, elementType, "", depth + 1, ref budget));
                value = list; break;
            case NbtType.Compound:
                var children = new List<NbtTag>(); NbtType childType;
                while ((childType = ReadType(r)) != NbtType.End) children.Add(ReadPayload(r, childType, ReadString(r), depth + 1, ref budget));
                value = children; break;
            default: throw new InvalidDataException("TAG_End cannot be a named payload.");
        }
        return new NbtTag(type, name, value) { ElementType = elementType };
    }
    private static string ReadString(BinaryReader reader)
    {
        var bytes = Exact(reader, unchecked((ushort)Int16(reader)));
        var text = new StringBuilder();
        for (var i = 0; i < bytes.Length;)
        {
            var a = bytes[i++];
            if (a < 0x80) { text.Append((char)a); continue; }
            if ((a & 0xe0) == 0xc0 && i < bytes.Length && (bytes[i] & 0xc0) == 0x80)
            { text.Append((char)(((a & 31) << 6) | (bytes[i++] & 63))); continue; }
            if ((a & 0xf0) == 0xe0 && i + 1 < bytes.Length && (bytes[i] & 0xc0) == 0x80 && (bytes[i + 1] & 0xc0) == 0x80)
            { text.Append((char)(((a & 15) << 12) | ((bytes[i++] & 63) << 6) | (bytes[i++] & 63))); continue; }
            throw new InvalidDataException("Invalid Java modified UTF-8 string.");
        }
        return text.ToString();
    }
    private static void WriteString(BinaryWriter writer, string value)
    {
        using var stream = new MemoryStream();
        foreach (var c in value)
        {
            if (c is > '\0' and <= '\x7f') stream.WriteByte((byte)c);
            else if (c <= 0x7ff) { stream.WriteByte((byte)(0xc0 | (c >> 6))); stream.WriteByte((byte)(0x80 | (c & 63))); }
            else { stream.WriteByte((byte)(0xe0 | (c >> 12))); stream.WriteByte((byte)(0x80 | ((c >> 6) & 63))); stream.WriteByte((byte)(0x80 | (c & 63))); }
        }
        if (stream.Length > ushort.MaxValue) throw new InvalidDataException("NBT strings are limited to 65535 encoded bytes.");
        WriteNumber(writer, (short)stream.Length); writer.Write(stream.ToArray());
    }
    private static void WriteNumber(BinaryWriter w, short value) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteInt16BigEndian(b, value); w.Write(b); }
    private static void WriteNumber(BinaryWriter w, int value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, value); w.Write(b); }
    private static void WriteNumber(BinaryWriter w, long value) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(b, value); w.Write(b); }
    private static void WritePayload(BinaryWriter w, NbtTag tag, int depth)
    {
        if (depth > MaxDepth) throw new InvalidDataException("NBT nesting limit exceeded.");
        switch (tag.Type)
        {
            case NbtType.Byte: w.Write((sbyte)tag.Value); break;
            case NbtType.Short: WriteNumber(w, (short)tag.Value); break;
            case NbtType.Int: WriteNumber(w, (int)tag.Value); break;
            case NbtType.Long: WriteNumber(w, (long)tag.Value); break;
            case NbtType.Float: WriteNumber(w, BitConverter.SingleToInt32Bits((float)tag.Value)); break;
            case NbtType.Double: WriteNumber(w, BitConverter.DoubleToInt64Bits((double)tag.Value)); break;
            case NbtType.String: WriteString(w, (string)tag.Value); break;
            case NbtType.ByteArray: var bytes = (byte[])tag.Value; WriteNumber(w, bytes.Length); w.Write(bytes); break;
            case NbtType.IntArray: var ints = (int[])tag.Value; WriteNumber(w, ints.Length); foreach (var n in ints) WriteNumber(w, n); break;
            case NbtType.LongArray: var longs = (long[])tag.Value; WriteNumber(w, longs.Length); foreach (var n in longs) WriteNumber(w, n); break;
            case NbtType.List:
                var list = (List<NbtTag>)tag.Value; w.Write((byte)tag.ElementType); WriteNumber(w, list.Count);
                foreach (var child in list) { if (child.Type != tag.ElementType) throw new InvalidDataException("Binary NBT lists must contain one tag type."); WritePayload(w, child, depth + 1); }
                break;
            case NbtType.Compound:
                foreach (var child in (List<NbtTag>)tag.Value) { w.Write((byte)child.Type); WriteString(w, child.Name); WritePayload(w, child, depth + 1); }
                w.Write((byte)0); break;
            default: throw new InvalidDataException("Unsupported NBT payload type.");
        }
    }
}
