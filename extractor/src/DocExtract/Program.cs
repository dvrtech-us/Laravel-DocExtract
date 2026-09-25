using System.Text.Json;
using DocExtract.Cli;
using DocExtract.Contract;
using DocExtract.Extraction;
using DocExtract.Limits;
using DocExtract.Output;
using DocExtract.Sandbox;
using DocExtract.SelfTest;

namespace DocExtract;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (ExtractionException ex)
        {
            Console.Error.WriteLine(ex.ErrorCode);
            return ex.ExitCode;
        }
        catch (OutOfMemoryException)
        {
            Console.Error.WriteLine(ErrorCodes.LimitMemory);
            return ExitCodes.Limits;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(ErrorCodes.Timeout);
            return ExitCodes.Timeout;
        }
        catch (Exception)
        {
            Console.Error.WriteLine(ErrorCodes.Failed);
            return ExitCodes.Failure;
        }
    }

    public static int Run(string[] args)
    {
        CliCommand command;
        try
        {
            command = CliParser.Parse(args);
        }
        catch (ExtractionException ex) when (ex.ErrorCode == ErrorCodes.Usage)
        {
            Console.Out.WriteLine("DocExtract extract|self-test|version");
            Console.Error.WriteLine(ErrorCodes.Usage);
            return ExitCodes.Failure;
        }

        return command.Kind switch
        {
            CliCommandKind.Version => Version(),
            CliCommandKind.SelfTest => SelfTest(command.SelfTest?.RequireOcr == true || (OperatingSystem.IsWindows() && command.SelfTest?.AllowMissingOcr != true)),
            CliCommandKind.Extract => Extract(command.Extract!),
            _ => ExitCodes.Failure,
        };
    }

    private static int Version()
    {
        var payload = new { version = EngineCatalog.Version, engines = EngineCatalog.Engines };
        Console.Out.WriteLine(JsonSerializer.Serialize(payload, ResultWriter.JsonOptions));
        return ExitCodes.Success;
    }

    private static int SelfTest(bool requireOcr)
    {
        var report = SelfTestRunner.Run(requireOcr: requireOcr);
        Console.Out.WriteLine(SelfTestRunner.ToJson(report));
        if (!report.Passed)
        {
            Console.Error.WriteLine(ErrorCodes.SelfTest);
            return ExitCodes.Failure;
        }

        return ExitCodes.Success;
    }

    private static int Extract(ExtractInvocation invocation)
    {
        if (invocation.Child && invocation.ChildTestHang)
        {
            Thread.Sleep(Timeout.InfiniteTimeSpan);
        }

        if (invocation.Child && invocation.ChildTestAllocate)
        {
            AllocateUntilJobKill();
        }

        if (invocation.Child && invocation.ChildTestFloodStderr)
        {
            var line = new string('x', 200);
            for (var i = 0; i < 400; i++)
            {
                Console.Error.WriteLine(line);
            }

            Console.Error.WriteLine(ErrorCodes.LimitZipRatio);
            return ExitCodes.Limits;
        }

        if (!invocation.Child && !OperatingSystem.IsWindows())
        {
            Console.Out.WriteLine("W_SANDBOX_UNAVAILABLE");
            return ExtractInProcess(invocation);
        }

        if (!invocation.Child && OperatingSystem.IsWindows())
        {
            return WindowsJobSandbox.RunChild(invocation);
        }

        return ExtractInProcess(invocation);
    }

    private static void AllocateUntilJobKill()
    {
        var held = new List<byte[]>();
        while (true)
        {
            var block = new byte[8 * 1024 * 1024];
            block[0] = 1;
            held.Add(block);
        }
    }

    private static int ExtractInProcess(ExtractInvocation invocation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(invocation.TimeoutSeconds));
        try
        {
            var info = new FileInfo(invocation.InputPath);
            if (!info.Exists)
            {
                Console.Error.WriteLine(ErrorCodes.Failed);
                return ExitCodes.Failure;
            }

            if (info.Length > LimitBudget.DefaultMaxInputBytes)
            {
                Console.Error.WriteLine(ErrorCodes.LimitInputSize);
                return ExitCodes.Limits;
            }

            var data = File.ReadAllBytes(invocation.InputPath);
            var budget = new LimitBudget
            {
                MaxOutputChars = invocation.MaxOutputChars,
                MaxDepth = invocation.MaxDepth,
            };
            var pipeline = new ExtractionPipeline();
            var result = pipeline.Extract(data, invocation.LogicalName, budget, new ExtractSettings
            {
                Ocr = invocation.Ocr,
                Tables = invocation.Tables,
            }, timeout.Token);
            ResultWriter.Write(invocation.OutputDir, result);
            return ExitCodes.Success;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(ErrorCodes.Timeout);
            return ExitCodes.Timeout;
        }
        catch (ExtractionException ex)
        {
            Console.Error.WriteLine(ex.ErrorCode);
            return ex.ExitCode;
        }
    }
}
