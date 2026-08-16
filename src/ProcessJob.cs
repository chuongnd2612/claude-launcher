using System.Runtime.InteropServices;

namespace ClaudeLauncher;

/// <summary>
/// A Windows job object holding every process the launcher starts.
///
/// Terminating a child only kills that child, and Claude starts its own: node
/// subprocesses, hooks, whatever a tool run spawns. Those would be left behind
/// with no window and no way to find them. A job kills the whole tree instead.
///
/// It also covers the case an exit handler cannot: the job is set to kill its
/// members when the last handle closes, and Windows closes handles even when a
/// process is killed outright, so closing the launcher window or taskkill-ing
/// it takes the sessions with it.
/// </summary>
public static class ProcessJob
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    private static readonly IntPtr Job = Create();

    /// <summary>Puts a process under the launcher's lifetime. False if jobs are unavailable.</summary>
    public static bool Assign(IntPtr process)
    {
        if (Job == IntPtr.Zero || process == IntPtr.Zero) return false;

        // Already being in a job is fine from Windows 8 on, where jobs nest.
        return AssignProcessToJobObject(Job, process);
    }

    private static IntPtr Create()
    {
        try
        {
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return IntPtr.Zero;

            var info = new ExtendedLimit
            {
                BasicLimitInformation = new BasicLimit { LimitFlags = KillOnJobClose }
            };

            var size = Marshal.SizeOf<ExtendedLimit>();
            var buffer = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(info, buffer, false);
                if (!SetInformationJobObject(job, ExtendedLimitInformation, buffer, (uint)size))
                {
                    CloseHandle(job);
                    return IntPtr.Zero;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return job;
        }
        catch (DllNotFoundException)
        {
            // Not Windows. Sessions then rely on explicit disposal alone.
            return IntPtr.Zero;
        }
        catch (EntryPointNotFoundException)
        {
            return IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
