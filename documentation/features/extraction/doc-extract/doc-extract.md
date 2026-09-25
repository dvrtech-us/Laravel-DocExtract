# Document extraction

DocExtract turns one document into a folder of Markdown, images, and `result.json` for the host application to store and page.

## User flow

1. The caller runs `DocExtract extract --input <file> --output-dir <dir>` with the optional caps from the contract (`--file-name`, `--max-output-chars`, `--max-depth`, `--ocr`, `--tables`, `--memory-mb`, `--cpu-seconds`, `--timeout-seconds`).
2. On Windows the parent process does not parse the file. It creates a Job Object, starts `extract --child` suspended, assigns the child, resumes it, and enforces `--timeout-seconds`.
3. On macOS and Linux the process prints `W_SANDBOX_UNAVAILABLE` to stdout and extracts in-process. stderr stays empty on success.
4. The run writes `<output-dir>/parts/<index>.md`, `<output-dir>/images/img-N.png` or `.jpg`, and `<output-dir>/result.json`.
5. The process exits `0` when `result.json` is written. Non-zero exits print a single error code on stderr and do not promise `result.json`. On Windows the parent drains stdout and stderr on background threads while the child runs (stderr kept to the last 64 KB, parent pipe-end copies closed after `CreateProcess`), then keeps only a stderr line matching `^E_[A-Z_]+$`, and maps job CPU and memory kills to `E_LIMIT_CPU` and `E_LIMIT_MEMORY`. A ZIP central-directory offset that is negative, the ZIP64 marker `0xFFFFFFFF`, or past the buffer is treated as not encrypted.
6. `self-test` checks every generable kind, including a synthetic MSG, and exits `0` or `1`. `--require-ocr` (the default on Windows) also requires Tesseract. `version` prints the extractor version and pinned engines.

## Technical flow

1. `Program.Run` parses arguments in `CliParser`.
2. `Extract` either calls `WindowsJobSandbox.RunChild` or `ExtractInProcess`.
3. `ExtractInProcess` rejects a root file over 25 MB (`E_LIMIT_INPUT_SIZE`), reads the bytes, and calls `ExtractionPipeline.Extract` with a `LimitBudget` and a `CancellationTokenSource` of `--timeout-seconds`.
4. `ExtractionPipeline.Process` asks `TypeSniffer.Detect`. Unsupported roots throw `E_UNSUPPORTED`. Supported roots dispatch an `IExtractor`.
5. Container extractors (`ZipExtractor`, `EmlExtractor`, `MsgExtractor`) append their own part, then call `ExtractionContext.ProcessChild` depth-first.
6. `ResultWriter.Write` joins every relative path through `SafePath.Combine` and writes UTF-8 (no BOM).

Hard failures throw `ExtractionException` out of the pipeline. Nested corrupt, encrypted, or unsupported files become warnings on a part instead of failing the run. Nested zip-ratio, decompressed, input, and pixel limits still fail the run.

## Key classes

| Class | Path | Role |
|---|---|---|
| `Program` | `extractor/src/DocExtract/Program.cs` | Process entry, exit codes, in-process timeout. |
| `CliParser` | `extractor/src/DocExtract/Cli/CliParser.cs` | `extract`, `self-test`, `version`, and the child argument list. |
| `WindowsJobSandbox` | `extractor/src/DocExtract/Sandbox/WindowsJobSandbox.cs` | Job Object limits, suspended child launch, and stderr filtering. | 
| `ChildLaunch` | `extractor/src/DocExtract/Sandbox/ChildLaunch.cs` | Keeps the `extract` verb and switches to `dotnet exec` when the host is `dotnet`. |
| `ChildExitMapper` | `extractor/src/DocExtract/Sandbox/ChildLaunch.cs` | Maps 1816, memory NTSTATUS values, and contract exit codes. |
| `ExtractionPipeline` | `extractor/src/DocExtract/Extraction/ExtractionPipeline.cs` | Kind dispatch, root versus child failure policy, char offsets, `ok` / `partial`. |
| `ExtractionContext` | `extractor/src/DocExtract/Extraction/ExtractionContext.cs` | Shared budget, part list, image list, child recursion, image attach. |
| `IExtractor` | `extractor/src/DocExtract/Extraction/IExtractor.cs` | One extractor per kind. |
| `TypeSniffer` | `extractor/src/DocExtract/Sniff/TypeSniffer.cs` | Magic bytes. OOXML versus ZIP via `[Content_Types].xml` and the main part. |
| `ImageHeader` | `extractor/src/DocExtract/Sniff/ImageHeader.cs` | PNG, JPEG, GIF, BMP, WEBP, and TIFF dimensions before decode. |
| `LimitBudget` | `extractor/src/DocExtract/Limits/LimitBudget.cs` | Cumulative caps. `TakeOutput` and `Utf16Units.Truncate` keep surrogate pairs intact. |
| `SafePath` | `extractor/src/DocExtract/Paths/SafePath.cs` | Rejects absolute paths, `..`, `:`, and reserved device names. |
| `TextExtractor` | `extractor/src/DocExtract/Extractors/TextExtractor.cs` | Text, HTML, and a light RTF strip. Shared `HandleOrRethrow`. |
| `ImageExtractor` | `extractor/src/DocExtract/Extractors/ImageExtractor.cs` | Root images. |
| `PdfExtractor` | `extractor/src/DocExtract/Extractors/PdfExtractor.cs` | PdfPig layout, Tabula tables, PDFtoImage for pages without a text layer. |
| `DocxExtractor`, `XlsxExtractor`, `PptxExtractor` | `extractor/src/DocExtract/Extractors/OfficeExtractors.cs` | Open XML. `OfficeLimits.MaxCharactersInPart`, row and column caps. |
| `EmlExtractor` | `extractor/src/DocExtract/Extractors/EmlExtractor.cs` | MimeKit. Inline images stay on the message part. Other attachments recurse. |
| `MsgExtractor` | `extractor/src/DocExtract/Extractors/MsgExtractor.cs` | MsgReader. Nested messages are walked in place. |
| `ZipExtractor` | `extractor/src/DocExtract/Extractors/ZipExtractor.cs` | Ratio check before `Open`, then a counted read. |
| `ImageNormalizer` | `extractor/src/DocExtract/Images/ImageNormalizer.cs` | SkiaSharp decode, resize, PNG/JPEG encode. |
| `TesseractOcrEngine` | `extractor/src/DocExtract/Ocr/TesseractOcrEngine.cs` | `IOcrEngine` adapter over the Tesseract 5.2.0 wrapper. |
| `ResultWriter` | `extractor/src/DocExtract/Output/ResultWriter.cs` | `parts/*.md`, `images/*`, `result.json`. |
| `SelfTestRunner` | `extractor/src/DocExtract/SelfTest/SelfTestRunner.cs` | In-memory fixtures from `FixtureFactory`. |
| `EngineCatalog` | `extractor/src/DocExtract/Contract/EngineCatalog.cs` | Version `1.0.0` and the engine version map. |

## Integration points

- The host application launches the published executable with an argument array and reads `result.json` plus the part and image files. This repository does not call the host application. The Laravel package in this repo is that caller.
- No network calls. MimeKit does not resolve remote MIME resources. Open XML external relationships are not fetched. HTML is stripped locally by `HtmlToText`.
- OCR looks only at `<exe directory>/tessdata/eng.traineddata` or the same folder under `AppContext.BaseDirectory`.
- `IOcrEngine` is the swap point if the Tesseract wrapper is replaced with a pinned CLI.

## Routes and access control

None. This is a local console process. It does not authenticate. The caller is responsible for which file is passed as `--input`.

## Database schema

None.

## SQL artifacts

None.

## Output and failure behavior

- Root unsupported: exit 2, `E_UNSUPPORTED`, no `result.json`.
- Root corrupt (`PdfDocumentFormatException` and other format or invalid-data errors): exit 4, `E_CORRUPT`.
- Root encrypted (`PdfDocument.IsEncrypted` or an encryption exception): exit 4, `E_ENCRYPTED`.
- Hard limits: exit 3 and `E_LIMIT_INPUT_SIZE`, `E_LIMIT_ZIP_RATIO`, `E_LIMIT_DECOMPRESSED`, or `E_LIMIT_PIXELS`.
- Timeout: exit 5, `E_TIMEOUT`. On Windows the parent kills the job. Off Windows cancellation is checked between pages, parts, and archive entries.
- Success with limiting warnings: exit 0, `status` `partial`, `result.json` written.
- `W_OCR_UNAVAILABLE` does not by itself fail the process. A text PDF with no OCR need does not get that warning.

`pages` is null for text, images, email, msg, and zip. It is the document page, slide, or sheet count for pdf, pptx, and xlsx.

Markdown for each part starts with `## <logical path>`. OCR text uses `### OCR text (img-N)`. Hidden sheets and slides are marked `*(hidden)*`.
