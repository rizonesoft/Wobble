using System.Runtime.Versioning;
using Wobble.Testing;

namespace Wobble.EndToEnd;

/// <summary>
/// Starts the emulator once for all end-to-end tests and waits for Windows to attach its drive.
/// Needs the Windows iSCSI initiator connected to the target: run tools\attach.ps1 elevated once per boot.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EmulatorFixture : IAsyncLifetime
{
    private string loadedKind = "cd";

    public EmulatorProcess Emulator { get; private set; } = null!;
    public WindowsDrive Drive { get; private set; } = null!;
    public string Image { get; } = Path.Combine(AppContext.BaseDirectory, "data-disc.iso");

    public async Task InitializeAsync()
    {
        // The test output is tests/Wobble.EndToEnd/bin/<configuration>/net11.0; the emulator is built the same way.
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Wobble.Host", "bin", configuration, "net11.0", "wobble.exe"));

        Emulator = await EmulatorProcess.StartAsync(executable, Image);
        Drive = await WindowsDrive.WaitForAsync("WOBBLE", TimeSpan.FromSeconds(90));
        await Drive.WaitForReadyAsync(true, TimeSpan.FromSeconds(60));
    }

    /// <summary>Makes sure the fixture disc is loaded as the given kind ("cd" or "dvd") and mounted.</summary>
    public async Task LoadAsync(string kind)
    {
        if (loadedKind != kind || !new DriveInfo(Drive.Root).IsReady)
        {
            await Emulator.Control.InsertMediaAsync(Image, kind);
            loadedKind = kind;
        }
        await Drive.WaitForReadyAsync(true, TimeSpan.FromSeconds(60));
    }

    public Task DisposeAsync()
    {
        Emulator?.Dispose();
        return Task.CompletedTask;
    }
}
