using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wobble.Testing;

/// <summary>
/// Runs the wobble executable as a child process for the length of a test run.
/// The process sits in a job object that kills it when this object is disposed or the test process dies.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EmulatorProcess : IDisposable
{
    private readonly Process process;
    private readonly nint job;

    private EmulatorProcess(Process process, nint job, WobbleControl control)
    {
        this.process = process;
        this.job = job;
        Control = control;
    }

    public WobbleControl Control { get; }

    public static async Task<EmulatorProcess> StartAsync(string executable, string? image = null, int port = 3260, int controlPort = 3261)
    {
        var start = new ProcessStartInfo(executable)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add(port.ToString());
        start.ArgumentList.Add("--control-port");
        start.ArgumentList.Add(controlPort.ToString());
        if (image is not null)
        {
            start.ArgumentList.Add("--image");
            start.ArgumentList.Add(image);
        }

        nint job = CreateKillOnCloseJob();
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        }
        catch
        {
            CloseHandle(job);
            throw;
        }
        var emulator = new EmulatorProcess(process, job, new WobbleControl($"http://127.0.0.1:{controlPort}"));
        try
        {
            if (!AssignProcessToJobObject(job, process.Handle))
            {
                // Outside the job nothing would stop the process later, so stop its whole tree now.
                var error = new Win32Exception();
                process.Kill(entireProcessTree: true);
                throw error;
            }
            var errors = process.StandardError.ReadToEndAsync();
            var ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
            if (ready is null || !ready.StartsWith("wobble:"))
                throw new InvalidOperationException($"wobble did not start: {ready} {await errors.WaitAsync(TimeSpan.FromSeconds(5))}");
            return emulator;
        }
        catch
        {
            emulator.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Control.Dispose();
        CloseHandle(job);                       // kills the process and anything it started
        process.WaitForExit(10_000);
        process.Dispose();
    }

    private static nint CreateKillOnCloseJob()
    {
        nint job = CreateJobObjectW(0, null);
        if (job == 0) throw new Win32Exception();
        var limits = new JobObjectExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = 0x2000;      // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
            throw new Win32Exception();
        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateJobObjectW(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(nint job, int infoClass, ref JobObjectExtendedLimitInformation info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}
