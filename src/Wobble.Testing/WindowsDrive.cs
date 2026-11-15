using System.Diagnostics;
using System.Runtime.Versioning;

namespace Wobble.Testing;

/// <summary>An optical drive as Windows sees it through IMAPI2.</summary>
public sealed record WindowsDrive(string Vendor, string Product, string Root, int[] CurrentProfiles, int[] SupportedProfiles)
{
    /// <summary>Lists every optical drive IMAPI2 knows about (MsftDiscMaster2).</summary>
    [SupportedOSPlatform("windows")]
    public static List<WindowsDrive> List()
    {
        var drives = new List<WindowsDrive>();
        dynamic master = Create("IMAPI2.MsftDiscMaster2");
        foreach (string id in master)
        {
            dynamic recorder = Create("IMAPI2.MsftDiscRecorder2");
            recorder.InitializeDiscRecorder(id);
            var paths = ((object[])recorder.VolumePathNames).Cast<string>().ToArray();
            drives.Add(new WindowsDrive(
                ((string)recorder.VendorId).Trim(), ((string)recorder.ProductId).Trim(), paths.FirstOrDefault() ?? "",
                Profiles(recorder.CurrentProfiles), Profiles(recorder.SupportedProfiles)));
        }
        return drives;
    }

    /// <summary>
    /// Waits for the drive of the given vendor to be attached. The Windows iSCSI initiator reconnects by itself
    /// after the emulator starts, which can take half a minute.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static async Task<WindowsDrive> WaitForAsync(string vendor, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (List().FirstOrDefault(d => d.Vendor == vendor && d.Root != "") is { } drive) return drive;
            if (clock.Elapsed > timeout)
                throw new TimeoutException(
                    $"No optical drive from vendor '{vendor}' appeared within {timeout.TotalSeconds:0} s. " +
                    "Run tools\\attach.ps1 from an elevated PowerShell once per boot to connect the Windows iSCSI initiator.");
            await Task.Delay(1000);
        }
    }

    /// <summary>Waits until Windows has mounted (or lost) the disc's file system.</summary>
    public async Task WaitForReadyAsync(bool ready, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (new DriveInfo(Root).IsReady != ready)
        {
            if (clock.Elapsed > timeout)
                throw new TimeoutException($"{Root} did not become {(ready ? "ready" : "empty")} within {timeout.TotalSeconds:0} s.");
            await Task.Delay(500);
        }
    }

    [SupportedOSPlatform("windows")]
    private static dynamic Create(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId, throwOnError: true)!)!;

    private static int[] Profiles(object values) => ((object[])values).Select(Convert.ToInt32).ToArray();
}
