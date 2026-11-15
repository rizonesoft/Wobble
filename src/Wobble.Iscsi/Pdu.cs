using System.Text;
using Wobble.Core;

namespace Wobble.Iscsi;

/// <summary>An iSCSI protocol data unit: the 48-byte basic header segment and its data segment.</summary>
public sealed class Pdu(byte[] header, byte[] data)
{
    public const int HeaderLength = 48;

    public byte[] Header { get; } = header;
    public byte[] Data { get; } = data;

    public int Opcode => Header[0] & 0x3F;
    public bool Immediate => (Header[0] & 0x40) != 0;
    public uint InitiatorTaskTag => Bytes.U32(Header, 16);

    public static Pdu Create(byte opcode, byte[]? data = null)
    {
        var header = new byte[HeaderLength];
        header[0] = opcode;
        return new Pdu(header, data ?? []);
    }

    /// <summary>Reads one PDU, or returns null when the peer closed the connection between PDUs.</summary>
    public static async Task<Pdu?> ReadAsync(Stream stream, CancellationToken cancel)
    {
        var header = new byte[HeaderLength];
        int got = await stream.ReadAtLeastAsync(header, HeaderLength, throwOnEndOfStream: false, cancel);
        if (got == 0) return null;
        if (got < HeaderLength) throw new EndOfStreamException("Connection closed inside a PDU header.");

        int extraHeader = header[4] * 4;
        int dataLength = (int)Bytes.U24(header, 5);
        var rest = new byte[extraHeader + (dataLength + 3) / 4 * 4];
        await stream.ReadExactlyAsync(rest, cancel);
        return new Pdu(header, rest.AsSpan(extraHeader, dataLength).ToArray());
    }

    public async Task WriteAsync(Stream stream, CancellationToken cancel)
    {
        Header[5] = (byte)(Data.Length >> 16);
        Header[6] = (byte)(Data.Length >> 8);
        Header[7] = (byte)Data.Length;
        var wire = new byte[HeaderLength + (Data.Length + 3) / 4 * 4];
        Header.CopyTo(wire, 0);
        Data.CopyTo(wire, HeaderLength);
        await stream.WriteAsync(wire, cancel);
    }

    /// <summary>Parses login and text data: NUL-separated key=value pairs.</summary>
    public static List<KeyValuePair<string, string>> ParseText(byte[] data)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var item in Encoding.ASCII.GetString(data).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = item.IndexOf('=');
            if (eq > 0) pairs.Add(new(item[..eq], item[(eq + 1)..]));
        }
        return pairs;
    }

    public static byte[] FormatText(IEnumerable<KeyValuePair<string, string>> pairs) =>
        Encoding.ASCII.GetBytes(string.Concat(pairs.Select(p => $"{p.Key}={p.Value}\0")));
}
