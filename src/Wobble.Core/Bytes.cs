using System.Buffers.Binary;
using System.Text;

namespace Wobble.Core;

/// <summary>Big-endian helpers for building and reading SCSI structures.</summary>
public static class Bytes
{
    public static ushort U16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16BigEndian(b[at..]);
    public static uint U24(ReadOnlySpan<byte> b, int at) => (uint)(b[at] << 16 | b[at + 1] << 8 | b[at + 2]);
    public static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32BigEndian(b[at..]);
    public static void Put16(Span<byte> b, int at, int value) => BinaryPrimitives.WriteUInt16BigEndian(b[at..], (ushort)value);
    public static void Put32(Span<byte> b, int at, long value) => BinaryPrimitives.WriteUInt32BigEndian(b[at..], (uint)value);

    /// <summary>Writes ASCII text left-aligned and space-padded to a fixed width.</summary>
    public static void PutAscii(Span<byte> b, int at, int width, string text)
    {
        b.Slice(at, width).Fill((byte)' ');
        Encoding.ASCII.GetBytes(text.AsSpan(0, Math.Min(text.Length, width)), b.Slice(at, width));
    }

    /// <summary>Truncates a response to the allocation length. Length fields inside it keep the full size.</summary>
    public static byte[] Fit(byte[] data, long allocationLength) =>
        data.Length <= allocationLength ? data : data[..(int)allocationLength];

    /// <summary>Converts a logical block address to minute, second, frame (with the 150 frame offset).</summary>
    public static (byte M, byte S, byte F) Msf(long lba)
    {
        long frames = lba + 150;
        // Addresses past 255 minutes (any DVD-sized disc) do not fit; drives report the maximum.
        if (frames / 4500 > 255) return (255, 59, 74);
        return ((byte)(frames / 4500), (byte)(frames / 75 % 60), (byte)(frames % 75));
    }
}
