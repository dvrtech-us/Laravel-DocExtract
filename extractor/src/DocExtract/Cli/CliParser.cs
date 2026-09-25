using DocExtract.Contract;
using DocExtract.Extraction;

namespace DocExtract.Cli;

public enum CliCommandKind
{
    Extract,
    SelfTest,
    Version,
}

public sealed class ExtractInvocation
{
    public required string InputPath { get; init; }
    public required string OutputDir { get; init; }
    public string? FileName { get; init; }
    public int MaxOutputChars { get; init; } = Limits.LimitBudget.DefaultMaxOutputChars;
    public int MaxDepth { get; init; } = Limits.LimitBudget.DefaultMaxDepth;
    public OcrChoice Ocr { get; init; } = OcrChoice.Auto;
    public TableChoice Tables { get; init; } = TableChoice.Auto;
    public int MemoryMb { get; init; } = 1024;
    public int CpuSeconds { get; init; } = 90;
    public int TimeoutSeconds { get; init; } = 100;
    public bool Child { get; init; }
    public bool ChildTestHang { get; init; }
    public bool ChildTestAllocate { get; init; }
    public bool ChildTestFloodStderr { get; init; }

    public string LogicalName => string.IsNullOrWhiteSpace(FileName) ? Path.GetFileName(InputPath) : FileName;
}

public sealed class SelfTestInvocation
{
    public bool RequireOcr { get; init; }
}

public sealed class CliCommand
{
    public required CliCommandKind Kind { get; init; }
    public ExtractInvocation? Extract { get; init; }
    public SelfTestInvocation? SelfTest { get; init; }
}

public static class CliParser
{
    public static CliCommand Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw Usage();
        }

        return args[0] switch
        {
            "version" => args.Length == 1 ? new CliCommand { Kind = CliCommandKind.Version } : throw Usage(),
            "self-test" => new CliCommand { Kind = CliCommandKind.SelfTest, SelfTest = ParseSelfTest(args) },
            "extract" => new CliCommand { Kind = CliCommandKind.Extract, Extract = ParseExtract(args) },
            _ => throw Usage(),
        };
    }

    public static ExtractInvocation ParseExtract(string[] args)
    {
        string? input = null;
        string? output = null;
        string? fileName = null;
        var maxChars = Limits.LimitBudget.DefaultMaxOutputChars;
        var maxDepth = Limits.LimitBudget.DefaultMaxDepth;
        var ocr = OcrChoice.Auto;
        var tables = TableChoice.Auto;
        var memory = 1024;
        var cpu = 90;
        var timeout = 100;
        var child = false;
        var hang = false;
        var allocate = false;
        var floodStderr = false;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--child":
                    child = true;
                    break;
                case "--child-test-hang":
                    if (!Sandbox.ChildLaunch.TestHooksEnabled)
                    {
                        throw Usage();
                    }

                    hang = true;
                    break;
                case "--child-test-allocate":
                    if (!Sandbox.ChildLaunch.TestHooksEnabled)
                    {
                        throw Usage();
                    }

                    allocate = true;
                    break;
                case "--child-test-flood-stderr":
                    if (!Sandbox.ChildLaunch.TestHooksEnabled)
                    {
                        throw Usage();
                    }

                    floodStderr = true;
                    break;
                case "--input":
                    input = Need(args, ref i);
                    break;
                case "--output-dir":
                    output = Need(args, ref i);
                    break;
                case "--file-name":
                    fileName = Need(args, ref i);
                    break;
                case "--max-output-chars":
                    maxChars = Positive(Need(args, ref i));
                    break;
                case "--max-depth":
                    maxDepth = NonNegative(Need(args, ref i));
                    break;
                case "--ocr":
                    ocr = Need(args, ref i) switch
                    {
                        "auto" => OcrChoice.Auto,
                        "off" => OcrChoice.Off,
                        _ => throw Usage(),
                    };
                    break;
                case "--tables":
                    tables = Need(args, ref i) switch
                    {
                        "auto" => TableChoice.Auto,
                        "off" => TableChoice.Off,
                        _ => throw Usage(),
                    };
                    break;
                case "--memory-mb":
                    memory = Positive(Need(args, ref i));
                    break;
                case "--cpu-seconds":
                    cpu = Positive(Need(args, ref i));
                    break;
                case "--timeout-seconds":
                    timeout = Positive(Need(args, ref i));
                    break;
                default:
                    throw Usage();
            }
        }

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
        {
            throw Usage();
        }

        return new ExtractInvocation
        {
            InputPath = input,
            OutputDir = output,
            FileName = fileName,
            MaxOutputChars = maxChars,
            MaxDepth = maxDepth,
            Ocr = ocr,
            Tables = tables,
            MemoryMb = memory,
            CpuSeconds = cpu,
            TimeoutSeconds = timeout,
            Child = child,
            ChildTestHang = hang,
            ChildTestAllocate = allocate,
            ChildTestFloodStderr = floodStderr,
        };
    }

    public static SelfTestInvocation ParseSelfTest(string[] args)
    {
        var requireOcr = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--require-ocr")
            {
                requireOcr = true;
                continue;
            }

            throw Usage();
        }

        return new SelfTestInvocation { RequireOcr = requireOcr };
    }

    public static IReadOnlyList<string> ToArgs(ExtractInvocation invocation, bool child)
    {
        var args = new List<string>
        {
            "extract",
            "--input", invocation.InputPath,
            "--output-dir", invocation.OutputDir,
            "--file-name", invocation.LogicalName,
            "--max-output-chars", invocation.MaxOutputChars.ToString(),
            "--max-depth", invocation.MaxDepth.ToString(),
            "--ocr", invocation.Ocr == OcrChoice.Off ? "off" : "auto",
            "--tables", invocation.Tables == TableChoice.Off ? "off" : "auto",
            "--memory-mb", invocation.MemoryMb.ToString(),
            "--cpu-seconds", invocation.CpuSeconds.ToString(),
            "--timeout-seconds", invocation.TimeoutSeconds.ToString(),
        };
        if (child)
        {
            args.Add("--child");
        }

        if (invocation.ChildTestHang)
        {
            args.Add("--child-test-hang");
        }

        if (invocation.ChildTestAllocate)
        {
            args.Add("--child-test-allocate");
        }

        if (invocation.ChildTestFloodStderr)
        {
            args.Add("--child-test-flood-stderr");
        }

        return args;
    }

    private static string Need(string[] args, ref int index)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw Usage();
        }

        index++;
        return args[index];
    }

    private static int Positive(string text)
    {
        if (!int.TryParse(text, out var value) || value <= 0)
        {
            throw Usage();
        }

        return value;
    }

    private static int NonNegative(string text)
    {
        if (!int.TryParse(text, out var value) || value < 0)
        {
            throw Usage();
        }

        return value;
    }

    private static ExtractionException Usage() => new(ExitCodes.Failure, ErrorCodes.Usage);
}
