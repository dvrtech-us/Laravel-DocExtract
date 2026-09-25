using System.Text.RegularExpressions;
using DocExtract.Cli;
using DocExtract.Contract;

namespace DocExtract.Sandbox;

public static class ChildLaunch
{
    public const string TestHooksVariable = "DOCEXTRACT_TEST_HOOKS";

    public static bool TestHooksEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(TestHooksVariable), "1", StringComparison.Ordinal);

    public static bool IsDotnetHost(string? processPath)
    {
        var name = Path.GetFileName(processPath ?? "");
        return name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Arguments after the executable. A dotnet host must be <c>dotnet exec &lt;dll&gt; extract …</c>
    /// so the extract verb is not dropped. An apphost receives <c>extract …</c> directly.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string? processPath, string assemblyLocation, IReadOnlyList<string> childArgs)
    {
        if (!IsDotnetHost(processPath))
        {
            return childArgs;
        }

        var args = new List<string>(childArgs.Count + 2) { "exec", assemblyLocation };
        args.AddRange(childArgs);
        return args;
    }
}

public static class ChildExitMapper
{
    public const uint ErrorNotEnoughQuota = 1816;
    public const uint StatusQuotaExceeded = 0xC0000044;
    public const uint StatusNoMemory = 0xC0000017;
    public const uint StatusCommitmentLimit = 0xC000012D;

    private static readonly Regex ErrorCodeLine = new(@"^E_[A-Z_]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsMemoryKill(uint nativeExit) => nativeExit is StatusQuotaExceeded or StatusNoMemory or StatusCommitmentLimit;

    public static (int ExitCode, string? ErrorCode) Map(uint nativeExit, string? stderr)
    {
        if (nativeExit == ErrorNotEnoughQuota)
        {
            return (ExitCodes.Limits, ErrorCodes.LimitCpu);
        }

        if (IsMemoryKill(nativeExit))
        {
            return (ExitCodes.Limits, ErrorCodes.LimitMemory);
        }

        if (nativeExit == 0)
        {
            return (ExitCodes.Success, null);
        }

        if (nativeExit is >= 1 and <= 5)
        {
            var line = LastErrorCode(stderr);
            return line is null
                ? (ExitCodes.Failure, ErrorCodes.Failed)
                : (unchecked((int)nativeExit), line);
        }

        return (ExitCodes.Failure, ErrorCodes.Failed);
    }

    public static string? LastErrorCode(string? stderr)
    {
        if (string.IsNullOrEmpty(stderr))
        {
            return null;
        }

        string? last = null;
        foreach (var raw in stderr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (ErrorCodeLine.IsMatch(line))
            {
                last = line;
            }
        }

        return last;
    }
}
