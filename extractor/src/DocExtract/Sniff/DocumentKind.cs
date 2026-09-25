namespace DocExtract.Sniff;

public enum DocumentKind
{
    Text,
    Image,
    Pdf,
    Docx,
    Xlsx,
    Pptx,
    Eml,
    Msg,
    Zip,
    Unsupported,
}

public readonly record struct Detection(DocumentKind Kind, string Mime)
{
    public string KindName => Kind switch
    {
        DocumentKind.Text => "text",
        DocumentKind.Image => "image",
        DocumentKind.Pdf => "pdf",
        DocumentKind.Docx => "docx",
        DocumentKind.Xlsx => "xlsx",
        DocumentKind.Pptx => "pptx",
        DocumentKind.Eml => "eml",
        DocumentKind.Msg => "msg",
        DocumentKind.Zip => "zip",
        _ => "unsupported",
    };
}
