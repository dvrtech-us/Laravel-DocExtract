using System.ComponentModel;
using System.Runtime.InteropServices;
using DocExtract.Cli;
using DocExtract.Contract;
using DocExtract;

namespace DocExtract.Sandbox;

public readonly record struct JobLimits(uint Flags, uint ActiveProcessLimit, ulong ProcessMemoryBytes, long JobCpu100Ns)
{
    public const uint KillOnJobClose = 0x00002000;
    public const uint ProcessMemory = 0x00000100;
    public const uint JobTime = 0x00000004;
    public const uint ActiveProcess = 0x00000008;

    public static JobLimits Build(int memoryMb, int cpuSeconds)
    {
        if (memoryMb <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryMb));
        }

        if (cpuSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cpuSeconds));
        }

        return new JobLimits(
            KillOnJobClose | ProcessMemory | JobTime | ActiveProcess,
            ActiveProcessLimit: 1,
            ProcessMemoryBytes: (ulong)memoryMb * 1024UL * 1024UL,
            JobCpu100Ns: (long)cpuSeconds * 10_000_000L);
    }
}

public static class WindowsJobSandbox
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    public static int RunChild(ExtractInvocation invocation)
    {
        if (!IsSupported)
        {
            throw ExtractionException.Sandbox();
        }

        return Native.Run(invocation);
    }

    private static class Native
    {
        public static int Run(ExtractInvocation invocation)
        {
            var limits = JobLimits.Build(invocation.MemoryMb, invocation.CpuSeconds);
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                throw ExtractionException.Sandbox();
            }

            try
            {
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                    {
                        PerJobUserTimeLimit = limits.JobCpu100Ns,
                        LimitFlags = limits.Flags,
                        ActiveProcessLimit = limits.ActiveProcessLimit,
                    },
                    ProcessMemoryLimit = (UIntPtr)limits.ProcessMemoryBytes,
                };
                var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                var ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    if (!SetInformationJobObject(job, 9, ptr, (uint)size))
                    {
                        throw ExtractionException.Sandbox(new Win32Exception(Marshal.GetLastWin32Error()));
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }

                var exe = Environment.ProcessPath ?? throw ExtractionException.Sandbox();
                var childArgs = CliParser.ToArgs(invocation, child: true);
                var assemblyLocation = typeof(Program).Assembly.Location;
                var argv = ChildLaunch.Arguments(exe, assemblyLocation, childArgs);
                var commandLine = Quote(exe) + " " + string.Join(' ', argv.Select(Quote));
                var stdoutPipe = CreateStdPipe(inheritRead: false);
                var stderrPipe = CreateStdPipe(inheritRead: false);
                var stdinPipe = CreateStdPipe(inheritRead: true);
                CloseHandle(stdinPipe.Write);
                stdinPipe.Write = IntPtr.Zero;
                try
                {
                    var startup = new STARTUPINFO
                    {
                        cb = Marshal.SizeOf<STARTUPINFO>(),
                        dwFlags = 0x00000100,
                        hStdInput = stdinPipe.Read,
                        hStdOutput = stdoutPipe.Write,
                        hStdError = stderrPipe.Write,
                    };
                    if (!CreateProcess(exe, commandLine, IntPtr.Zero, IntPtr.Zero, true, 0x00000004 | 0x08000000, IntPtr.Zero, null, ref startup, out var process))
                    {
                        throw ExtractionException.Sandbox(new Win32Exception(Marshal.GetLastWin32Error()));
                    }

                    CloseHandle(stdoutPipe.Write);
                    stdoutPipe.Write = IntPtr.Zero;
                    CloseHandle(stderrPipe.Write);
                    stderrPipe.Write = IntPtr.Zero;
                    CloseHandle(stdinPipe.Read);
                    stdinPipe.Read = IntPtr.Zero;

                    var stderrTail = new StderrTail();
                    var stdoutDrain = new Thread(() => Drain(stdoutPipe.Read)) { IsBackground = true, Name = "docextract-stdout" };
                    var stderrDrain = new Thread(() => ReadIntoTail(stderrPipe.Read, stderrTail)) { IsBackground = true, Name = "docextract-stderr" };
                    stdoutDrain.Start();
                    stderrDrain.Start();

                    try
                    {
                        if (!AssignProcessToJobObject(job, process.hProcess))
                        {
                            TerminateProcess(process.hProcess, 1);
                            throw ExtractionException.Sandbox(new Win32Exception(Marshal.GetLastWin32Error()));
                        }

                        if (ResumeThread(process.hThread) == 0xFFFFFFFF)
                        {
                            TerminateProcess(process.hProcess, 1);
                            throw ExtractionException.Sandbox(new Win32Exception(Marshal.GetLastWin32Error()));
                        }

                        var timeoutMs = invocation.TimeoutSeconds >= int.MaxValue / 1000
                            ? uint.MaxValue
                            : (uint)invocation.TimeoutSeconds * 1000u;
                        var wait = WaitForSingleObject(process.hProcess, timeoutMs);
                        if (wait == 0x00000102)
                        {
                            TerminateJobObject(job, (uint)ExitCodes.Timeout);
                            JoinDrains(stdoutDrain, stderrDrain);
                            Console.Error.WriteLine(ErrorCodes.Timeout);
                            return ExitCodes.Timeout;
                        }

                        if (wait == 0xFFFFFFFF)
                        {
                            throw ExtractionException.Sandbox(new Win32Exception(Marshal.GetLastWin32Error()));
                        }

                        JoinDrains(stdoutDrain, stderrDrain);
                        if (!GetExitCodeProcess(process.hProcess, out var code))
                        {
                            throw ExtractionException.Sandbox(new Win32Exception(Marshal.GetLastWin32Error()));
                        }

                        var mapped = ChildExitMapper.Map(code, stderrTail.Text);
                        if (mapped.ErrorCode is not null)
                        {
                            Console.Error.WriteLine(mapped.ErrorCode);
                        }

                        return mapped.ExitCode;
                    }
                    finally
                    {
                        if (WaitForSingleObject(process.hProcess, 0) != 0)
                        {
                            TerminateProcess(process.hProcess, 1);
                        }

                        JoinDrains(stdoutDrain, stderrDrain);
                        CloseHandle(process.hThread);
                        CloseHandle(process.hProcess);
                    }
                }
                finally
                {
                    ClosePipe(stdoutPipe);
                    ClosePipe(stderrPipe);
                    ClosePipe(stdinPipe);
                }
            }
            finally
            {
                CloseHandle(job);
            }
        }

        private static (IntPtr Read, IntPtr Write) CreateStdPipe(bool inheritRead)
        {
            var attributes = new SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                bInheritHandle = 1,
            };
            if (!CreatePipe(out var read, out var write, ref attributes, 0))
            {
                throw ExtractionException.Sandbox(new Win32Exception(Marshal.GetLastWin32Error()));
            }

            var clearInherit = inheritRead ? write : read;
            if (!SetHandleInformation(clearInherit, 1, 0))
            {
                CloseHandle(read);
                CloseHandle(write);
                throw ExtractionException.Sandbox(new Win32Exception(Marshal.GetLastWin32Error()));
            }

            return (read, write);
        }

        private static void ClosePipe((IntPtr Read, IntPtr Write) pipe)
        {
            if (pipe.Read != IntPtr.Zero)
            {
                CloseHandle(pipe.Read);
            }

            if (pipe.Write != IntPtr.Zero)
            {
                CloseHandle(pipe.Write);
            }
        }

        private static void JoinDrains(Thread stdoutDrain, Thread stderrDrain)
        {
            stdoutDrain.Join();
            stderrDrain.Join();
        }

        private static void ReadIntoTail(IntPtr handle, StderrTail tail)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var buffer = new byte[4096];
            while (ReadFile(handle, buffer, buffer.Length, out var read, IntPtr.Zero) && read > 0)
            {
                tail.Append(buffer.AsSpan(0, read));
            }
        }

        private static void Drain(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var buffer = new byte[4096];
            while (ReadFile(handle, buffer, buffer.Length, out var read, IntPtr.Zero) && read > 0)
            {
            }
        }

        internal static string Quote(string arg)
        {
            if (arg.Length == 0)
            {
                return "\"\"";
            }

            if (arg.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            {
                return arg;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append('"');
            var slashes = 0;
            foreach (var ch in arg)
            {
                if (ch == '\\')
                {
                    slashes++;
                    continue;
                }

                if (ch == '"')
                {
                    sb.Append('\\', (slashes * 2) + 1);
                    sb.Append('"');
                    slashes = 0;
                    continue;
                }

                if (slashes > 0)
                {
                    sb.Append('\\', slashes);
                    slashes = 0;
                }

                sb.Append(ch);
            }

            if (slashes > 0)
            {
                sb.Append('\\', slashes * 2);
            }

            sb.Append('"');
            return sb.ToString();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(
            string? lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToRead, out int lpNumberOfBytesRead, IntPtr lpOverlapped);
    }
}
