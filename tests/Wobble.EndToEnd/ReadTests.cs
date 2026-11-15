using System.Runtime.Versioning;
using Wobble.Testing;

namespace Wobble.EndToEnd;

/// <summary>What the iSCSI proof checked by hand, against Wobble itself: Windows sees an ordinary optical drive.</summary>
[SupportedOSPlatform("windows")]
public class ReadTests(EmulatorFixture fixture) : IClassFixture<EmulatorFixture>
{
    private static readonly TimeSpan MediaChange = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task TheDriveGetsALetter_AndTheDiscMountsAsCdfs()
    {
        await fixture.LoadAsync("cd");

        var info = new DriveInfo(fixture.Drive.Root);

        Assert.Equal(DriveType.CDRom, info.DriveType);
        Assert.Equal("CDFS", info.DriveFormat);
        Assert.Equal("WOBBLE_TEST", info.VolumeLabel);
    }

    [Fact]
    public async Task FilesReadBackByteForByte()
    {
        await fixture.LoadAsync("cd");
        var pattern = new byte[100_000];
        for (int i = 0; i < pattern.Length; i++) pattern[i] = (byte)(i * 7 % 251);

        Assert.Equal("wobble test disc\n", await File.ReadAllTextAsync(Path.Combine(fixture.Drive.Root, "HELLO.TXT")));
        Assert.Equal(pattern, await File.ReadAllBytesAsync(Path.Combine(fixture.Drive.Root, "sub", "PATTERN.BIN")));
    }

    [Theory]
    [InlineData("cd", 0x08)]
    [InlineData("dvd", 0x10)]
    public async Task Imapi2ListsTheDrive_WithTheProfileOfTheLoadedDisc(string kind, int profile)
    {
        await fixture.LoadAsync(kind);

        var drive = Assert.Single(WindowsDrive.List(), d => d.Vendor == "WOBBLE");

        Assert.Equal("VIRTUAL DVD-ROM", drive.Product);
        Assert.Equal(fixture.Drive.Root, drive.Root);
        Assert.Equal([profile], drive.CurrentProfiles);
        Assert.Equal([0x10, 0x08], drive.SupportedProfiles);
    }

    [Fact]
    public async Task RemovingTheDiscEmptiesTheDrive_AndInsertingItMountsAgain()
    {
        await fixture.LoadAsync("cd");

        await fixture.Emulator.Control.RemoveMediaAsync();
        await fixture.Drive.WaitForReadyAsync(false, MediaChange);
        Assert.Empty(Assert.Single(WindowsDrive.List(), d => d.Vendor == "WOBBLE").CurrentProfiles);

        await fixture.LoadAsync("cd");
        Assert.Equal("WOBBLE_TEST", new DriveInfo(fixture.Drive.Root).VolumeLabel);
    }

    [Fact]
    public async Task WindowsSendsNothingWobbleRejects_ExceptCopyProtectionQueries()
    {
        await fixture.LoadAsync("cd");
        await File.ReadAllBytesAsync(Path.Combine(fixture.Drive.Root, "sub", "PATTERN.BIN"));

        var trace = await fixture.Emulator.Control.TraceAsync();

        Assert.Contains(trace, line => line.Cdb.StartsWith("28"));                // READ(10)
        var refused = trace
            .Where(line => line.Sense is not null)
            .Where(line => !line.Sense!.StartsWith("06/"))                         // unit attentions after media changes
            .Where(line => !line.Sense!.StartsWith("02/3A"))                       // polling an empty drive
            .Where(line => !line.Cdb.StartsWith("A4"))                             // REPORT KEY: CSS is out of scope
            .Select(line => $"{line.Cdb} -> {line.Sense}")
            .Distinct();
        Assert.Empty(refused);
    }
}
