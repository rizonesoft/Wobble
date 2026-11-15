namespace Wobble.Core;

/// <summary>Builds the parts of a raw CD sector that a cooked image does not store: header, EDC, ECC and subchannel Q.</summary>
public static class SectorCodec
{
    public const int RawSize = 2352;
    private static readonly byte[] Sync = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    private static readonly byte[] EccForward = new byte[256];
    private static readonly byte[] EccBackward = new byte[256];
    private static readonly uint[] EdcTable = new uint[256];

    static SectorCodec()
    {
        for (int i = 0; i < 256; i++)
        {
            int j = i << 1 ^ ((i & 0x80) != 0 ? 0x11D : 0);
            EccForward[i] = (byte)j;
            EccBackward[i ^ j] = (byte)i;
            uint edc = (uint)i;
            for (int bit = 0; bit < 8; bit++) edc = edc >> 1 ^ ((edc & 1) != 0 ? 0xD8018001 : 0);
            EdcTable[i] = edc;
        }
    }

    public static byte Bcd(int value) => (byte)(value / 10 << 4 | value % 10);

    public static uint Edc(ReadOnlySpan<byte> data)
    {
        uint edc = 0;
        foreach (byte b in data) edc = edc >> 8 ^ EdcTable[(edc ^ b) & 0xFF];
        return edc;
    }

    /// <summary>Writes sync and the header (address in BCD, mode) of a data sector.</summary>
    public static void WriteHeader(Span<byte> raw, long lba, byte mode)
    {
        Sync.CopyTo(raw);
        var (m, s, f) = Bytes.Msf(lba);
        raw[12] = Bcd(m); raw[13] = Bcd(s); raw[14] = Bcd(f);
        raw[15] = mode;
    }

    /// <summary>Wraps 2048 bytes of user data in a complete Mode 1 sector.</summary>
    public static void EncodeMode1(long lba, ReadOnlySpan<byte> userData, Span<byte> raw)
    {
        WriteHeader(raw, lba, 1);
        userData.CopyTo(raw[16..]);
        uint edc = Edc(raw[..2064]);
        raw[2064] = (byte)edc; raw[2065] = (byte)(edc >> 8); raw[2066] = (byte)(edc >> 16); raw[2067] = (byte)(edc >> 24);
        raw.Slice(2068, 8).Clear();
        EccBlock(raw[12..], 86, 24, 2, 86, raw[2076..]);        // P parity
        EccBlock(raw[12..], 52, 43, 86, 88, raw[2248..]);       // Q parity
    }

    private static void EccBlock(ReadOnlySpan<byte> source, int majorCount, int minorCount, int majorMult, int minorInc, Span<byte> destination)
    {
        int size = majorCount * minorCount;
        for (int major = 0; major < majorCount; major++)
        {
            int index = (major >> 1) * majorMult + (major & 1);
            byte a = 0, b = 0;
            for (int minor = 0; minor < minorCount; minor++)
            {
                byte value = source[index];
                index += minorInc;
                if (index >= size) index -= size;
                a ^= value;
                b ^= value;
                a = EccForward[a];
            }
            a = EccBackward[EccForward[a] ^ b];
            destination[major] = a;
            destination[major + majorCount] = (byte)(a ^ b);
        }
    }

    /// <summary>
    /// Builds the 12 bytes of subchannel Q for a sector: control and ADR 1, track, index, the address within
    /// the track, the address on the disc, and the CRC.
    /// </summary>
    public static void WriteQ(Span<byte> q, Track track, long lba)
    {
        bool pause = lba < track.Start;
        var relative = Bytes.Msf((pause ? track.Start - lba : lba - track.Start) - 150);
        var absolute = Bytes.Msf(lba);
        q[0] = (byte)(track.Control << 4 | 0x01);
        q[1] = Bcd(track.Number);
        q[2] = (byte)(pause ? 0 : 1);
        q[3] = Bcd(relative.M); q[4] = Bcd(relative.S); q[5] = Bcd(relative.F);
        q[6] = 0;
        q[7] = Bcd(absolute.M); q[8] = Bcd(absolute.S); q[9] = Bcd(absolute.F);
        ushort crc = 0;
        for (int i = 0; i < 10; i++)
        {
            crc ^= (ushort)(q[i] << 8);
            for (int bit = 0; bit < 8; bit++) crc = (ushort)((crc & 0x8000) != 0 ? crc << 1 ^ 0x1021 : crc << 1);
        }
        crc = (ushort)~crc;
        q[10] = (byte)(crc >> 8);
        q[11] = (byte)crc;
    }
}
