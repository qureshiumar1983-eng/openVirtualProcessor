using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenVP.Desktop;

/// <summary>
/// The hidden server process. Its output goes to a log file. It's put in a Windows job object that kills it
/// when this app exits, so the server never outlives the window, even if the app crashes.
/// </summary>
internal sealed class ServerProcess : IDisposable
{
    private Process? _process;
    private StreamWriter? _log;
    private IntPtr _job;

    public bool Owned => _process != null;
    public bool HasExited => _process?.HasExited ?? true;

    public void Start(string exe, string workingDir, string logPath)
    {
        _log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add($"--urls=http://0.0.0.0:{Program.Port}");
        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => Write(e.Data);
        _process.ErrorDataReceived += (_, e) => Write(e.Data);
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        AddToKillOnCloseJob(_process);
    }

    private void Write(string? line)
    {
        if (line == null) return;
        lock (this) _log?.WriteLine(line);
    }

    /// <summary>Waits until the server answers, or it exits, or the timeout passes.</summary>
    public async Task<bool> WaitUntilReadyAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (Owned && HasExited) return false;
            if (await Task.Run(Program.IsOpenVpRunning)) return true;
            await Task.Delay(250);
        }
        return false;
    }

    public void Dispose()
    {
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); } catch { }
        try { _process?.WaitForExit(3000); } catch { }
        _process?.Dispose();
        lock (this) { _log?.Dispose(); _log = null; }
        if (_job != IntPtr.Zero) { CloseHandle(_job); _job = IntPtr.Zero; }
    }

    // ---------------------------------------------------------------- job object

    private void AddToKillOnCloseJob(Process p)
    {
        try
        {
            _job = CreateJobObject(IntPtr.Zero, null);
            if (_job == IntPtr.Zero) return;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                SetInformationJobObject(_job, JobObjectExtendedLimitInformation, ptr, (uint)size);
            }
            finally { Marshal.FreeHGlobal(ptr); }
            AssignProcessToJobObject(_job, p.Handle);
        }
        catch { /* best effort: Dispose still kills the server on a normal exit */ }
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
