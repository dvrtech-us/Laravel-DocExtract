namespace DocExtract.Ocr;

/// <summary>
/// CharlesW Tesseract wrapper. Swap this class for a CLI-backed engine without
/// changing extractors. Native binaries ship with the Windows build; macOS dev
/// runs report <see cref="UnavailableReason"/> instead of failing.
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine
{
    private readonly object _gate = new();
    private Tesseract.TesseractEngine? _engine;
    private bool _probed;
    private bool _available;
    private string? _reason;

    public bool IsAvailable
    {
        get
        {
            Probe();
            return _available;
        }
    }

    public string? UnavailableReason
    {
        get
        {
            Probe();
            return _reason;
        }
    }

    public static string? FindTessdataDirectory()
    {
        var roots = new List<string>();
        if (!string.IsNullOrEmpty(AppContext.BaseDirectory))
        {
            roots.Add(AppContext.BaseDirectory);
        }

        var processDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(processDir))
        {
            roots.Add(processDir);
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dir = Path.Combine(root, "tessdata");
            if (File.Exists(Path.Combine(dir, "eng.traineddata")))
            {
                return dir;
            }
        }

        return null;
    }

    public string Recognize(ReadOnlyMemory<byte> image, CancellationToken cancellationToken)
    {
        Probe();
        if (!_available || _engine is null)
        {
            throw new InvalidOperationException(_reason ?? "OCR unavailable");
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            using var pix = Tesseract.Pix.LoadFromMemory(image.ToArray());
            using var page = _engine.Process(pix);
            return page.GetText() ?? "";
        }
    }

    private void Probe()
    {
        if (_probed)
        {
            return;
        }

        lock (_gate)
        {
            if (_probed)
            {
                return;
            }

            var dir = FindTessdataDirectory();
            if (dir is null)
            {
                _available = false;
                _reason = "tessdata/eng.traineddata missing";
                _probed = true;
                return;
            }

            try
            {
                _engine = new Tesseract.TesseractEngine(dir, "eng", Tesseract.EngineMode.Default);
                _available = true;
                _reason = null;
            }
            catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException or FileNotFoundException or PlatformNotSupportedException or Tesseract.TesseractException)
            {
                _available = false;
                _reason = "tesseract native library unavailable";
                _engine = null;
            }
            catch (Exception)
            {
                _available = false;
                _reason = "tesseract native library unavailable";
                _engine = null;
            }

            _probed = true;
        }
    }
}
