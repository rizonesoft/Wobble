using System.Net;
using Wobble.Core;
using Wobble.Iscsi;

namespace Wobble.Tests;

public class IscsiTargetTests : IAsyncLifetime
{
    private const string Iqn = "iqn.2026-11.test.wobble:unit";
    private const int Blocks = 200;

    private readonly OpticalDrive drive = new();
    private readonly byte[] image = new byte[Blocks * OpticalDrive.BlockSize];
    private IscsiTarget target = null!;

    public Task InitializeAsync()
    {
        new Random(1).NextBytes(image);
        drive.InsertMedia(new Media(new MemoryDiscImage(image), MediaKind.CdRom));
        while (drive.Execute(new byte[6], []).Sense?.Key == 0x06) { }
        target = new IscsiTarget(drive, Iqn, new IPEndPoint(IPAddress.Loopback, 0));
        target.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await target.DisposeAsync();
        drive.Dispose();
    }

    private async Task<MiniInitiator> LoggedInAsync(int maxReceive = 65536)
    {
        var initiator = await MiniInitiator.ConnectAsync(target.LocalEndpoint);
        var (status, _) = await initiator.LoginNormalAsync(Iqn, maxReceive);
        Assert.Equal(0, status);
        return initiator;
    }

    [Fact]
    public async Task Login_NegotiatesNoDigestsAndOneConnection()
    {
        using var initiator = await MiniInitiator.ConnectAsync(target.LocalEndpoint);

        var (status, keys) = await initiator.LoginNormalAsync(Iqn);

        Assert.Equal(0, status);
        Assert.Equal("None", keys["HeaderDigest"]);
        Assert.Equal("None", keys["DataDigest"]);
        Assert.Equal("1", keys["TargetPortalGroupTag"]);
        Assert.Equal("262144", keys["MaxRecvDataSegmentLength"]);
    }

    [Fact]
    public async Task Login_ToAnUnknownTarget_IsRejectedAsNotFound()
    {
        using var initiator = await MiniInitiator.ConnectAsync(target.LocalEndpoint);

        var (status, _) = await initiator.LoginNormalAsync("iqn.2026-11.test.wobble:proof");

        Assert.Equal(0x0203, status);
    }

    [Fact]
    public async Task Discovery_SendTargets_ListsTheTargetAndItsAddress()
    {
        using var initiator = await MiniInitiator.ConnectAsync(target.LocalEndpoint);
        var (status, _) = await initiator.LoginAsync(("InitiatorName", "iqn.2026-11.test.wobble:tests"), ("SessionType", "Discovery"));
        Assert.Equal(0, status);

        var reply = await initiator.TextAsync("SendTargets", "All");

        Assert.Equal(
            [new("TargetName", Iqn), new("TargetAddress", $"127.0.0.1:{target.LocalEndpoint.Port},1")],
            reply);
    }

    [Fact]
    public async Task Inquiry_OverIscsi_MatchesTheDevice_AndReportsUnderflow()
    {
        using var initiator = await LoggedInAsync();
        byte[] cdb = [0x12, 0, 0, 0, 255, 0];

        var outcome = await initiator.CommandAsync(cdb, expectedIn: 255);

        Assert.Equal(ScsiStatus.Good, outcome.Status);
        Assert.Equal(drive.Execute(cdb, []).DataIn, outcome.Data);
        Assert.Equal(0x02, outcome.Flags & 0x06);                       // underflow
        Assert.Equal(255u - 36, outcome.Residual);
    }

    [Fact]
    public async Task Read_LargerThanTheInitiatorsSegmentLimit_ArrivesIntactInSeveralPdus()
    {
        using var initiator = await LoggedInAsync(maxReceive: 8192);

        var outcome = await initiator.CommandAsync([0x28, 0, 0, 0, 0, 10, 0, 0, 64, 0], expectedIn: 64 * 2048);

        Assert.Equal(ScsiStatus.Good, outcome.Status);
        Assert.Equal(16, outcome.DataPdus);
        Assert.Equal(image.AsSpan(10 * 2048, 64 * 2048).ToArray(), outcome.Data);
        Assert.Equal(0u, outcome.Residual);
    }

    [Fact]
    public async Task FailedCommand_ReturnsCheckConditionWithSense()
    {
        using var initiator = await LoggedInAsync();

        var outcome = await initiator.CommandAsync([0x28, 0, 0, 0, 0, Blocks, 0, 0, 1, 0], expectedIn: 2048);

        Assert.Equal(ScsiStatus.CheckCondition, outcome.Status);
        Assert.Equal(Sense.LbaOutOfRange.ToFixedFormat(), outcome.Sense);
        Assert.Equal(2048u, outcome.Residual);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DataOut_ArrivesIntact_AsImmediateDataPlusR2T_OrR2TAlone(bool immediateData)
    {
        var capture = new CapturingDevice();
        await using var writable = new IscsiTarget(capture, Iqn, new IPEndPoint(IPAddress.Loopback, 0));
        writable.Start();
        using var initiator = await MiniInitiator.ConnectAsync(writable.LocalEndpoint);
        Assert.Equal(0, (await initiator.LoginNormalAsync(Iqn)).Status);
        var sent = new byte[300_000];                                   // more than one 262144-byte burst
        new Random(2).NextBytes(sent);

        var outcome = await initiator.CommandAsync([0x2A, 0, 0, 0, 0, 0, 0, 0, 147, 0], dataOut: sent, immediateData: immediateData);

        Assert.Equal(ScsiStatus.Good, outcome.Status);
        Assert.Equal(sent, capture.DataOut);
    }

    [Fact]
    public async Task OnlyLunZeroExists_OtherLunsAnswerInquiryWithNoDevice_AndRefuseTheRest()
    {
        using var initiator = await LoggedInAsync();

        var inquiry = await initiator.CommandAsync([0x12, 0, 0, 0, 36, 0], expectedIn: 36, lun: 1);
        var ready = await initiator.CommandAsync([0x00, 0, 0, 0, 0, 0], lun: 1);

        Assert.Equal(0x7F, inquiry.Data[0]);
        Assert.Equal(ScsiStatus.CheckCondition, ready.Status);
        Assert.Equal(Sense.LunNotSupported.ToFixedFormat(), ready.Sense);
    }

    private sealed class CapturingDevice : IScsiDevice
    {
        public byte[] DataOut { get; private set; } = [];

        public ScsiResult Execute(ReadOnlySpan<byte> cdb, ReadOnlySpan<byte> dataOut)
        {
            DataOut = dataOut.ToArray();
            return ScsiResult.Good();
        }
    }
}
