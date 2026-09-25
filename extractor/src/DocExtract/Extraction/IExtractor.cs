namespace DocExtract.Extraction;

public sealed class ExtractionInput
{
    public required byte[] Data { get; init; }
    public required string LogicalPath { get; init; }
    public required int Depth { get; init; }
    public required bool IsRoot { get; init; }
    public required string Kind { get; init; }
    public required string Mime { get; init; }
}

public interface IExtractor
{
    string Kind { get; }

    void Extract(ExtractionInput input, ExtractionContext context);
}
