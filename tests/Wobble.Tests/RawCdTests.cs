using Wobble.Core;

namespace Wobble.Tests;

/// <summary>
/// A mixed-mode disc from a cue sheet: a Mode 1 track stored raw, an audio track after a two second
/// pregap that is not in the file, and an audio track whose pregap is in the file.
/// </summary>
public sealed class RawCdTests : IDisposable
{
    private const int DataSectors = 300, Audio2Sectors = 200, Audio3Gap = 75, Audio3Sectors = 120;
    private readonly string folder = Directory.CreateTempSubdirectory("wobble-cue-").FullName;
    private readonly byte[] bin;
    private readonly OpticalDrive drive = new();

    public RawCdTests()
    {
        bin = new byte[(DataSectors + Audio2Sectors + Audio3Gap + Audio3Sectors) * 2352];
        var user = new byte[2048];
        for (int i = 0; i < DataSectors; i++)
        {
            user.AsSpan().Fill((byte)(i + 1));
            SectorCodec.EncodeMode1(i, user, bin.AsSpan(i * 2352, 2352));
        }
        new Random(3).NextBytes(bin.AsSpan(DataSectors * 2352));           // audio
        File.WriteAllBytes(Path.Combine(folder, "disc.bin"), bin);
        File.WriteAllText(Path.Combine(folder, "disc.cue"), """
            FILE "disc.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                PREGAP 00:02:00
                INDEX 01 00:04:00
              TRACK 03 AUDIO
                INDEX 00 00:06:50
                INDEX 01 00:07:50
            """);
        drive.InsertMedia(Media.Open(Path.Combine(folder, "disc.cue")));
        while (drive.Execute(new byte[6], []).Sense?.Key == 0x06) { }
    }

    public void Dispose()
    {
        drive.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    // Disc addresses: track 1 at 0..299, the virtual pregap at 300..449, track 2 at 450..649,
    // track 3's pregap at 650..724 and track 3 at 725..844. The lead-out is 845.
    private const int Track2 = 450, Track3 = 725, LeadOut = 845;

    private byte[] BinSector(int fileSector) => bin.AsSpan(fileSector * 2352, 2352).ToArray();

    private static byte[] ReadCd(int lba, int count, byte fields, byte subchannel = 0, int sectorType = 0) =>
        [0xBE, (byte)(sectorType << 2), 0, (byte)(lba >> 16), (byte)(lba >> 8), (byte)lba, 0, 0, (byte)count, fields, subchannel, 0];

    [Fact]
    public void TheCueSheetLaysOutTracksAndPregaps()
    {
        var disc = drive.LoadedMedia!;

        Assert.Equal(
            [new Track(1, TrackMode.Mode1, 0, 300), new Track(2, TrackMode.Audio, Track2, 200, 150), new Track(3, TrackMode.Audio, Track3, 120, 75)],
            disc.Tracks);
        Assert.Equal(LeadOut, disc.BlockCount);
    }

    [Fact]
    public void ReadToc_ListsEveryTrackWithItsControl_AndTheLeadOut()
    {
        var data = drive.Execute([0x43, 0, 0, 0, 0, 0, 0, 0x03, 0x24, 0], []).DataIn;

        Assert.Equal(new byte[]
        {
            0x00, 0x22, 0x01, 0x03,
            0x00, 0x14, 0x01, 0x00, 0, 0, 0x00, 0x00,           // track 1, data, at 0
            0x00, 0x10, 0x02, 0x00, 0, 0, 0x01, 0xC2,           // track 2, audio, at 450
            0x00, 0x10, 0x03, 0x00, 0, 0, 0x02, 0xD5,           // track 3, audio, at 725
            0x00, 0x10, 0xAA, 0x00, 0, 0, 0x03, 0x4D,           // lead-out at 845
        }, data);
    }

    [Fact]
    public void ReadToc_FromALaterTrack_StartsThere()
    {
        var data = drive.Execute([0x43, 0, 0, 0, 0, 0, 3, 0x03, 0x24, 0], []).DataIn;

        Assert.Equal(4 + 16, data.Length);
        Assert.Equal(3, data[6]);
    }

    [Fact]
    public void ReadToc_FullToc_HasThePointersThenEveryTrack()
    {
        var data = drive.Execute([0x43, 0x02, 0x02, 0, 0, 0, 0, 0x03, 0x24, 0], []).DataIn;

        Assert.Equal(4 + 6 * 11, data.Length);
        Assert.Equal(data.Length - 2, data[0] << 8 | data[1]);
        Assert.Equal(new byte[] { 0xA0, 0xA1, 0xA2, 1, 2, 3 }, Enumerable.Range(0, 6).Select(i => data[4 + i * 11 + 3]).ToArray());
        Assert.Equal(new byte[] { 3, 0, 0 }, data[(15 + 8)..(15 + 11)]);          // A1: last track is 3
        Assert.Equal(new byte[] { 0, 11, 50 }, data[(59 + 8)..(59 + 11)]);        // track 3 at 725 + 150 frames
    }

    [Fact]
    public void Read10_ReturnsUserDataOfTheDataTrack_AndRefusesAudio()
    {
        var data = drive.Execute([0x28, 0, 0, 0, 0, 7, 0, 0, 2, 0], []);
        var audio = drive.Execute([0x28, 0, 0, 0, 0x01, 0xC2, 0, 0, 1, 0], []);

        Assert.Equal(4096, data.DataIn.Length);
        Assert.All(data.DataIn[..2048], b => Assert.Equal(8, b));
        Assert.All(data.DataIn[2048..], b => Assert.Equal(9, b));
        Assert.Equal(Sense.IllegalModeForThisTrack, audio.Sense);
    }

    [Fact]
    public void ReadCd_WholeSector_ReturnsTheStoredRawSector()
    {
        Assert.Equal(BinSector(10), drive.Execute(ReadCd(10, 1, 0xF8), []).DataIn);
        Assert.Equal(BinSector(DataSectors + 5), drive.Execute(ReadCd(Track2 + 5, 1, 0x10), []).DataIn);
        Assert.Equal(BinSector(DataSectors + Audio2Sectors + Audio3Gap), drive.Execute(ReadCd(Track3, 1, 0xF8), []).DataIn);
    }

    [Fact]
    public void ReadCd_SelectsFieldsOfADataSector()
    {
        var sector = BinSector(10);

        Assert.Equal(sector[16..2064], drive.Execute(ReadCd(10, 1, 0x10), []).DataIn);            // user data
        Assert.Equal(sector[12..16], drive.Execute(ReadCd(10, 1, 0x20), []).DataIn);              // header
        Assert.Equal(sector[2064..], drive.Execute(ReadCd(10, 1, 0x08), []).DataIn);              // EDC and ECC
        Assert.Empty(drive.Execute(ReadCd(10, 1, 0x00), []).DataIn);
    }

    [Fact]
    public void ReadCd_TheUnstoredPregap_IsSilence()
    {
        var data = drive.Execute(ReadCd(Track2 - 150, 150, 0x10), []).DataIn;

        Assert.Equal(150 * 2352, data.Length);
        Assert.All(data, b => Assert.Equal(0, b));
    }

    [Fact]
    public void ReadCd_ChecksTheExpectedSectorType()
    {
        Assert.Equal(ScsiStatus.Good, drive.Execute(ReadCd(10, 1, 0x10, sectorType: 2), []).Status);          // Mode 1
        Assert.Equal(ScsiStatus.Good, drive.Execute(ReadCd(Track2, 1, 0x10, sectorType: 1), []).Status);      // CD-DA
        Assert.Equal(Sense.IllegalModeForThisTrack, drive.Execute(ReadCd(10, 1, 0x10, sectorType: 1), []).Sense);
        Assert.Equal(Sense.IllegalModeForThisTrack, drive.Execute(ReadCd(Track2 - 1, 2, 0x10, sectorType: 2), []).Sense);
    }

    [Fact]
    public void ReadCdMsf_ReadsTheSameSectorsAsReadCd()
    {
        // Track 2 starts at 450 + 150 frames = 00:08:00. CarbonCD reads one sector: start, and start + 1 as the end.
        var data = drive.Execute([0xB9, 0x04, 0, 0, 8, 0, 0, 8, 1, 0x10, 0, 0], []).DataIn;

        Assert.Equal(drive.Execute(ReadCd(Track2, 1, 0x10), []).DataIn, data);
    }

    [Fact]
    public void ReadCd_FormattedQ_DescribesThePosition()
    {
        // Sector 5 of track 3: 730 on the disc, 00:09:55 absolute, 00:00:05 in the track.
        var data = drive.Execute(ReadCd(Track3 + 5, 1, 0x10, subchannel: 2), []).DataIn;
        var q = data[2352..];

        Assert.Equal(2352 + 16, data.Length);
        Assert.Equal(new byte[] { 0x01, 0x03, 0x01, 0x00, 0x00, 0x05, 0x00, 0x00, 0x11, 0x55 }, q[..10]);
        Assert.Equal(Crc16(q[..10]), q[10] << 8 | q[11]);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, q[12..]);
    }

    [Fact]
    public void ReadCd_FormattedQ_InAPregap_CountsDownAtIndexZero_WithThePFlag()
    {
        // Ten sectors before track 3 starts.
        var q = drive.Execute(ReadCd(Track3 - 10, 1, 0x00, subchannel: 2), []).DataIn;

        Assert.Equal(16, q.Length);
        Assert.Equal(new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x10 }, q[..6]);
        Assert.Equal(0x80, q[15]);
    }

    [Fact]
    public void ReadCd_RawSubchannel_CarriesQInBitSixOfEachByte()
    {
        var formatted = drive.Execute(ReadCd(5, 1, 0x00, subchannel: 2), []).DataIn;
        var raw = drive.Execute(ReadCd(5, 1, 0x00, subchannel: 1), []).DataIn;

        Assert.Equal(96, raw.Length);
        var q = new byte[12];
        for (int i = 0; i < 96; i++)
        {
            Assert.Equal(0, raw[i] & 0xBF);                                         // nothing but Q outside a pregap
            if ((raw[i] & 0x40) != 0) q[i / 8] |= (byte)(0x80 >> (i % 8));
        }
        Assert.Equal(formatted[..12], q);
        Assert.Equal(0x41, q[0]);                                                   // data track, ADR 1
    }

    [Fact]
    public void ReadTrackInformation_DescribesEachTrack()
    {
        var audio = drive.Execute([0x52, 0x01, 0, 0, 0, 2, 0, 0, 36, 0], []).DataIn;
        var byAddress = drive.Execute([0x52, 0x00, 0, 0, 0, 10, 0, 0, 36, 0], []).DataIn;

        Assert.Equal(2, audio[2]);
        Assert.Equal(0x00, audio[5]);                                               // audio
        Assert.Equal(0x0F, audio[6]);
        Assert.Equal((uint)Track2, Bytes.U32(audio, 8));
        Assert.Equal(200u, Bytes.U32(audio, 24));
        Assert.Equal(1, byAddress[2]);
        Assert.Equal(0x04, byAddress[5]);                                           // data
        Assert.Equal(0x01, byAddress[6]);                                           // Mode 1
    }

    [Fact]
    public void GeneratedMode1Sectors_HaveAValidChecksumAndParity()
    {
        var sector = BinSector(123);

        // The EDC is a CRC stored after the data it covers, so running it over both gives zero.
        Assert.Equal(0u, SectorCodec.Edc(sector.AsSpan(0, 2068)));

        // P parity: 86 Reed-Solomon codewords of 24 data bytes and 2 parity bytes over bytes 12..2075.
        for (int word = 0; word < 86; word++)
        {
            var symbols = Enumerable.Range(0, 24).Select(k => sector[12 + word + k * 86])
                .Append(sector[2076 + word]).Append(sector[2076 + 86 + word]).ToArray();
            AssertCodeword(symbols);
        }
        // Q parity: 52 codewords of 43 data bytes taken diagonally over bytes 12..2247, and 2 parity bytes.
        for (int word = 0; word < 52; word++)
        {
            var symbols = Enumerable.Range(0, 43).Select(k => sector[12 + ((word >> 1) * 86 + (word & 1) + k * 88) % 2236])
                .Append(sector[2248 + word]).Append(sector[2248 + 52 + word]).ToArray();
            AssertCodeword(symbols);
        }
    }

    /// <summary>A codeword of the CD-ROM Reed-Solomon code has 1 and alpha as roots.</summary>
    private static void AssertCodeword(byte[] symbols)
    {
        int atOne = 0, atAlpha = 0;
        foreach (byte symbol in symbols)
        {
            atOne ^= symbol;
            atAlpha = Times2(atAlpha) ^ symbol;
        }
        Assert.Equal(0, atOne);
        Assert.Equal(0, atAlpha);

        static int Times2(int x) => (x << 1 ^ ((x & 0x80) != 0 ? 0x11D : 0)) & 0xFF;
    }

    private static int Crc16(ReadOnlySpan<byte> data)
    {
        int crc = 0;
        foreach (byte b in data)
        {
            for (int bit = 7; bit >= 0; bit--)
            {
                bool feedback = ((crc >> 15) & 1) != ((b >> bit) & 1);
                crc = (crc << 1) & 0xFFFF;
                if (feedback) crc ^= 0x1021;
            }
        }
        return ~crc & 0xFFFF;
    }
}
