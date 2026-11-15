using Wobble.Core;

namespace Wobble.Tests;

public class OpticalDriveTests
{
    private const int Blocks = 1649;                      // the size of the image used in the iSCSI proof

    /// <summary>A drive holding a disc whose block n is filled with the byte n.</summary>
    private static OpticalDrive DriveWith(MediaKind kind, int blocks = Blocks)
    {
        var data = new byte[blocks * OpticalDrive.BlockSize];
        for (int i = 0; i < blocks; i++) data.AsSpan(i * OpticalDrive.BlockSize, OpticalDrive.BlockSize).Fill((byte)i);
        var drive = new OpticalDrive();
        drive.InsertMedia(new Media(new MemoryDiscImage(data), kind));
        Settle(drive);
        return drive;
    }

    /// <summary>Consumes pending unit attentions, as a host does after a media change.</summary>
    private static void Settle(OpticalDrive drive)
    {
        while (drive.Execute(Cdb(6, 0x00), []).Sense?.Key == 0x06) { }
    }

    private static byte[] Cdb(int length, params byte[] start)
    {
        var cdb = new byte[length];
        start.CopyTo(cdb, 0);
        return cdb;
    }

    private static byte[] ReadTocCdb(int format, bool msf, int allocation = 804, byte track = 0) =>
        [0x43, (byte)(msf ? 0x02 : 0), (byte)format, 0, 0, 0, track, (byte)(allocation >> 8), (byte)allocation, 0];

    private static byte[] Read10(uint lba, ushort count) =>
        [0x28, 0, (byte)(lba >> 24), (byte)(lba >> 16), (byte)(lba >> 8), (byte)lba, 0, (byte)(count >> 8), (byte)count, 0];

    // The fault that made Windows reject tgt's disc: READ TOC answers two bytes short, losing the lead-out address.
    [Fact]
    public void ReadToc_FormattedToc_IsCompleteAndEndsWithTheLeadOutAddress()
    {
        var result = DriveWith(MediaKind.DvdRom).Execute(ReadTocCdb(0, msf: false), []);

        Assert.Equal(ScsiStatus.Good, result.Status);
        Assert.Equal(
            new byte[] { 0x00, 0x12, 0x01, 0x01, 0x00, 0x14, 0x01, 0x00, 0, 0, 0, 0, 0x00, 0x14, 0xAA, 0x00, 0x00, 0x00, 0x06, 0x71 },
            result.DataIn);
    }

    [Fact]
    public void ReadToc_Msf_AddsTheTwoSecondOffset()
    {
        var data = DriveWith(MediaKind.CdRom).Execute(ReadTocCdb(0, msf: true), []).DataIn;

        Assert.Equal(new byte[] { 0, 0, 2, 0 }, data[8..12]);                 // track 1 at 00:02:00
        Assert.Equal(new byte[] { 0, 0, 23, 74 }, data[16..20]);              // 1649 + 150 frames = 00:23:74
    }

    [Fact]
    public void ReadToc_SessionInfo_IsTwelveBytes()
    {
        var data = DriveWith(MediaKind.CdRom).Execute(ReadTocCdb(1, msf: false), []).DataIn;

        Assert.Equal(new byte[] { 0x00, 0x0A, 0x01, 0x01, 0x00, 0x14, 0x01, 0x00, 0, 0, 0, 0 }, data);
    }

    [Fact]
    public void ReadToc_FullToc_HasFourEntriesAndAMatchingLength()
    {
        var data = DriveWith(MediaKind.CdRom).Execute(ReadTocCdb(2, msf: true), []).DataIn;

        Assert.Equal(48, data.Length);
        Assert.Equal(46, data[0] << 8 | data[1]);
        Assert.Equal(new byte[] { 0xA0, 0xA1, 0xA2, 0x01 }, new[] { data[7], data[18], data[29], data[40] });
        Assert.Equal(new byte[] { 0, 23, 74 }, data[34..37]);                 // lead-out
    }

    [Fact]
    public void ReadToc_ShortAllocation_TruncatesButKeepsTheFullLengthField()
    {
        var data = DriveWith(MediaKind.CdRom).Execute(ReadTocCdb(0, msf: false, allocation: 4), []).DataIn;

        Assert.Equal(new byte[] { 0x00, 0x12, 0x01, 0x01 }, data);
    }

    [Fact]
    public void ReadToc_UnsupportedFormat_IsAnIllegalRequest()
    {
        var result = DriveWith(MediaKind.CdRom).Execute(ReadTocCdb(4, msf: false), []);

        Assert.Equal(Sense.InvalidFieldInCdb, result.Sense);
    }

    // tgt could not read the last block of its backing file.
    [Fact]
    public void Read_LastBlock_Succeeds_AndOnePastIt_IsOutOfRange()
    {
        var drive = DriveWith(MediaKind.CdRom);

        var last = drive.Execute(Read10(Blocks - 1, 1), []);
        Assert.Equal(ScsiStatus.Good, last.Status);
        Assert.Equal(OpticalDrive.BlockSize, last.DataIn.Length);
        Assert.All(last.DataIn, b => Assert.Equal(unchecked((byte)(Blocks - 1)), b));

        Assert.Equal(Sense.LbaOutOfRange, drive.Execute(Read10(Blocks - 1, 2), []).Sense);
        Assert.Equal(Sense.LbaOutOfRange, drive.Execute(Read10(Blocks, 1), []).Sense);
    }

    [Fact]
    public void ReadCapacity_ReportsTheLastBlockAndBlockSize()
    {
        var data = DriveWith(MediaKind.CdRom).Execute(Cdb(10, 0x25), []).DataIn;

        Assert.Equal(new byte[] { 0, 0, 0x06, 0x70, 0, 0, 0x08, 0 }, data);
    }

    [Fact]
    public void InsertingMedia_RaisesOneUnitAttention_WhichInquiryDoesNotConsume()
    {
        var drive = new OpticalDrive();
        Settle(drive);
        drive.InsertMedia(new Media(new MemoryDiscImage(new byte[4096]), MediaKind.CdRom));

        Assert.Equal(ScsiStatus.Good, drive.Execute(Cdb(6, 0x12, 0, 0, 0, 36), []).Status);
        Assert.Equal(Sense.MediumMayHaveChanged, drive.Execute(Cdb(6, 0x00), []).Sense);
        Assert.Equal(ScsiStatus.Good, drive.Execute(Cdb(6, 0x00), []).Status);
    }

    [Fact]
    public void WithoutMedia_TheDriveIsNotReady_AndHasNoCurrentProfile()
    {
        var drive = new OpticalDrive();
        Settle(drive);

        Assert.Equal(Sense.NoMediumTrayClosed, drive.Execute(Cdb(6, 0x00), []).Sense);
        Assert.Equal(Sense.NoMediumTrayClosed, drive.Execute(Cdb(10, 0x25), []).Sense);
        var config = drive.Execute(Cdb(10, 0x46, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF), []).DataIn;
        Assert.Equal(0, config[6] << 8 | config[7]);
    }

    [Theory]
    [InlineData(MediaKind.CdRom, 0x0008)]
    [InlineData(MediaKind.DvdRom, 0x0010)]
    public void GetConfiguration_ReportsTheCurrentProfile_AndALengthThatMatchesTheData(MediaKind kind, int profile)
    {
        var data = DriveWith(kind).Execute(Cdb(10, 0x46, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF), []).DataIn;

        Assert.Equal(profile, data[6] << 8 | data[7]);
        Assert.Equal(data.Length - 4, (int)Bytes.U32(data, 0));

        // Walk the feature descriptors: they must tile the data exactly.
        int at = 8;
        var codes = new List<int>();
        while (at < data.Length)
        {
            codes.Add(data[at] << 8 | data[at + 1]);
            Assert.Equal(0, data[at + 3] % 4);
            at += 4 + data[at + 3];
        }
        Assert.Equal(data.Length, at);
        Assert.Equal([0x0000, 0x0001, 0x0002, 0x0003, 0x0010, 0x001D, 0x001E, 0x001F, 0x0100, 0x0105, 0x0108], codes);
    }

    [Fact]
    public void GetConfiguration_SingleFeatureRequest_ReturnsOnlyThatFeature()
    {
        var data = DriveWith(MediaKind.DvdRom).Execute(Cdb(10, 0x46, 0x02, 0x00, 0x1F, 0, 0, 0, 0xFF, 0xFF), []).DataIn;

        Assert.Equal(16, data.Length);
        Assert.Equal(new byte[] { 0x00, 0x1F, 0x05, 0x04 }, data[8..12]);     // DVD read, version 1, current
    }

    [Fact]
    public void GetEventStatusNotification_ReportsNewMediaOnce()
    {
        var drive = DriveWith(MediaKind.CdRom);
        byte[] poll = [0x4A, 0x01, 0, 0, 0x10, 0, 0, 0, 8, 0];

        // The length field counts what follows it: 2 header bytes and the 4-byte event. Windows checks for 6.
        Assert.Equal(new byte[] { 0, 6, 0x04, 0x16, 0x02, 0x02, 0, 0 }, drive.Execute(poll, []).DataIn);
        Assert.Equal(new byte[] { 0, 6, 0x04, 0x16, 0x00, 0x02, 0, 0 }, drive.Execute(poll, []).DataIn);
    }

    [Fact]
    public void GetEventStatusNotification_HeaderOnlyProbe_DoesNotConsumeTheEvent()
    {
        var drive = DriveWith(MediaKind.CdRom);

        Assert.Equal(new byte[] { 0, 6, 0x04, 0x16 }, drive.Execute([0x4A, 0x01, 0, 0, 0x10, 0, 0, 0, 4, 0], []).DataIn);
        Assert.Equal(0x02, drive.Execute([0x4A, 0x01, 0, 0, 0x10, 0, 0, 0, 8, 0], []).DataIn[4]);
    }

    [Fact]
    public void GetEventStatusNotification_ForUnsupportedClasses_ReportsNoEventAvailable()
    {
        var data = DriveWith(MediaKind.CdRom).Execute([0x4A, 0x01, 0, 0, 0x40, 0, 0, 0, 8, 0], []).DataIn;

        Assert.Equal(new byte[] { 0, 2, 0x80, 0x16 }, data);
    }

    [Fact]
    public void Read_OfMoreThanTheTransferLimit_IsRefused()
    {
        var drive = new OpticalDrive();
        drive.InsertMedia(new Media(new EmptyDisc(1_000_000), MediaKind.DvdRom));
        Settle(drive);
        byte[] read12 = [0xA8, 0, 0, 0, 0, 0, 0, 0, 0x80, 0x01, 0, 0];         // 32769 blocks

        Assert.Equal(Sense.InvalidFieldInCdb, drive.Execute(read12, []).Sense);
        read12[9] = 0x00;                                                      // 32768 blocks
        Assert.Equal(OpticalDrive.MaxTransferBlocks * OpticalDrive.BlockSize, drive.Execute(read12, []).DataIn.Length);
    }

    [Fact]
    public void ReadCd_OnACookedImage_ReturnsUserData_OrTheWholeGeneratedSector()
    {
        var drive = DriveWith(MediaKind.CdRom);

        var user = drive.Execute([0xBE, 0, 0, 0, 0, 5, 0, 0, 1, 0x10, 0, 0], []).DataIn;
        var raw = drive.Execute([0xBE, 0, 0, 0, 0, 5, 0, 0, 1, 0xF8, 0, 0], []).DataIn;

        Assert.Equal(2048, user.Length);
        Assert.All(user, b => Assert.Equal(5, b));
        Assert.Equal(2352, raw.Length);
        Assert.Equal(new byte[] { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x02, 0x05, 0x01 }, raw[..16]);
        Assert.Equal(user, raw[16..2064]);
    }

    [Fact]
    public void ReadCd_RefusesWhatIsNotModelled()
    {
        var drive = DriveWith(MediaKind.CdRom);

        Assert.Equal(Sense.InvalidFieldInCdb, drive.Execute([0xBE, 0, 0, 0, 0, 5, 0, 0, 1, 0x12, 0, 0], []).Sense);   // C2 pointers
        Assert.Equal(Sense.InvalidFieldInCdb, drive.Execute([0xBE, 0, 0, 0, 0, 5, 0, 0, 1, 0x10, 4, 0], []).Sense);   // corrected R to W
        Assert.Equal(Sense.IncompatibleMedium, DriveWith(MediaKind.DvdRom).Execute([0xBE, 0, 0, 0, 0, 5, 0, 0, 1, 0x10, 0, 0], []).Sense);
    }

    [Fact]
    public void Reset_RaisesAUnitAttention_AndAllowsRemovalAgain()
    {
        var drive = DriveWith(MediaKind.CdRom);
        drive.Execute(Cdb(6, 0x1E, 0, 0, 0, 0x01), []);

        drive.Reset();

        Assert.Equal(Sense.PowerOnReset, drive.Execute(Cdb(6, 0x00), []).Sense);
        Assert.Equal(ScsiStatus.Good, drive.Execute(Cdb(6, 0x1B, 0, 0, 0, 0x02), []).Status);
    }

    /// <summary>A large blank image that costs no memory.</summary>
    private sealed class EmptyDisc(long blocks) : IDiscImage
    {
        public long BlockCount => blocks;
        public void Read(long lba, Span<byte> destination) => destination.Clear();
        public void Dispose() { }
    }

    [Fact]
    public void Eject_IsRefusedWhileRemovalIsPrevented_AndOpensTheTrayOtherwise()
    {
        var drive = DriveWith(MediaKind.CdRom);
        byte[] eject = Cdb(6, 0x1B, 0, 0, 0, 0x02), load = Cdb(6, 0x1B, 0, 0, 0, 0x03);

        drive.Execute(Cdb(6, 0x1E, 0, 0, 0, 0x01), []);
        Assert.Equal(Sense.RemovalPrevented, drive.Execute(eject, []).Sense);

        drive.Execute(Cdb(6, 0x1E, 0, 0, 0, 0x00), []);
        Assert.Equal(ScsiStatus.Good, drive.Execute(eject, []).Status);
        Assert.Equal(Sense.NoMediumTrayOpen, drive.Execute(Cdb(6, 0x00), []).Sense);

        Assert.Equal(ScsiStatus.Good, drive.Execute(load, []).Status);
        Assert.Equal(Sense.MediumMayHaveChanged, drive.Execute(Cdb(6, 0x00), []).Sense);
        Assert.Equal(ScsiStatus.Good, drive.Execute(Cdb(6, 0x00), []).Status);
    }

    [Fact]
    public void ModeSense10_CapabilitiesPage_HasConsistentLengths()
    {
        var data = DriveWith(MediaKind.DvdRom).Execute(Cdb(10, 0x5A, 0, 0x2A, 0, 0, 0, 0, 0xFF, 0xFF), []).DataIn;

        Assert.Equal(8 + 32, data.Length);
        Assert.Equal(data.Length - 2, data[0] << 8 | data[1]);
        Assert.Equal(0x2A, data[8]);
        Assert.Equal(30, data[9]);
    }

    [Fact]
    public void Inquiry_DescribesARemovableOpticalDevice()
    {
        var data = new OpticalDrive().Execute(Cdb(6, 0x12, 0, 0, 0, 36), []).DataIn;

        Assert.Equal(36, data.Length);
        Assert.Equal(0x05, data[0]);
        Assert.Equal(0x80, data[1]);
        Assert.Equal(31, data[4]);
        Assert.Equal("WOBBLE  VIRTUAL DVD-ROM 0.1 ", System.Text.Encoding.ASCII.GetString(data, 8, 28));
    }

    [Fact]
    public void ReadDiscStructure_OnACd_IsRefused_AndOnADvd_DescribesTheDataArea()
    {
        byte[] cdb = Cdb(12, 0xAD, 0, 0, 0, 0, 0, 0, 0x00, 0x08, 0x04);

        Assert.Equal(Sense.IncompatibleMedium, DriveWith(MediaKind.CdRom).Execute(cdb, []).Sense);

        var data = DriveWith(MediaKind.DvdRom).Execute(cdb, []).DataIn;
        Assert.Equal(2052, data.Length);
        Assert.Equal(2050, data[0] << 8 | data[1]);
        Assert.Equal(0x030000u + Blocks - 1, Bytes.U32(data, 12));
    }

    [Fact]
    public void UnknownOpcode_IsAnIllegalRequest_AndEveryCommandIsTraced()
    {
        var drive = DriveWith(MediaKind.CdRom);
        drive.ClearTrace();

        Assert.Equal(Sense.InvalidOpcode, drive.Execute(Cdb(10, 0x5D), []).Sense);

        var entry = Assert.Single(drive.Trace);
        Assert.Equal("5D000000000000000000", entry.Cdb);
        Assert.Equal("05/20/00", entry.Sense);
    }
}
