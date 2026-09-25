# Initial public extractor

- Date: 2026-09-25
- Feature: doc-extract
- Related code: `extractor/src/DocExtract`, `extractor/tests/DocExtract.Tests`, `extractor/scripts`, `.github/workflows/ci.yml`

## Context

The host application needs a local process that reads one document and returns Markdown, normalized images, and `result.json`. This repository publishes that process as DocExtract, with a stable CLI, exit codes, and result schema.

## Decisions

- .NET 10 console app `DocExtract` lives under `extractor/`. The assembly, namespace, and executable are `DocExtract`. xUnit tests use nullable reference types. Package versions are pinned in `extractor/Directory.Packages.props` and `packages.lock.json`, including the `win-x64` runtime graph.
- `<Version>` in `extractor/Directory.Build.props` is the single version string. `EngineCatalog.Version` and `Dvrtech\LaravelDocExtract\Version::VERSION` must match it. CI runs `extractor/scripts/check-version.sh`.
- Publish shape is a self-contained win-x64 folder. `PublishSingleFile` and `PublishTrimmed` stay false. CI zips that folder as `DocExtract-win-x64-<version>.zip`, writes a sha256 file, and on tags `v*` attaches both files to a GitHub release. `eng.traineddata` is fetched into the folder before the zip and is not committed.
- One `IExtractor` per kind, `TypeSniffer` for magic bytes, `ExtractionContext` carrying a cumulative `LimitBudget`, and `ResultWriter` for `parts/*.md`, `images/*`, and `result.json`.
- OOXML is distinguished from a generic ZIP by `[Content_Types].xml` plus `word/document.xml`, `xl/workbook.xml`, or `ppt/presentation.xml`. The file extension does not override that.
- PdfPig 0.1.16 supplies text layout (Docstrum blocks and `UnsupervisedReadingOrderDetector`). Tabula 1.0.1 declares a dependency on PdfPig 0.1.14; the direct PdfPig 0.1.16 reference is what restores. Tables try lattice, then stream.
- OCR is the `Tesseract` 5.2.0 NuGet wrapper behind `IOcrEngine`. `eng` traineddata is downloaded by `extractor/scripts/fetch-tessdata.ps1` and `fetch-tessdata.sh` from tessdata_fast commit `923915d4ced2a7235221788285785a29c4a42d4a` and checked against sha256 `7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2`. If it or the native library is missing, the part gets `W_OCR_UNAVAILABLE` and the process still exits 0.
- Images use SkiaSharp 4.152.1. `SkiaSharp.NativeAssets.Win32` is the Windows asset. `SkiaSharp.NativeAssets.macOS` is also referenced so `dotnet test` can decode on macOS. ImageSharp is not referenced.
- Hard limits (input size, zip ratio, decompressed bytes, pixel header) fail the process with exit 3. Depth, part count, page caps, image count, and output size truncate with warnings and exit 0.
- Windows sandbox: `CreateJobObject` / `SetInformationJobObject` with kill-on-job-close, process memory, job CPU time, and active-process limit 1. The child is created suspended, then assigned and resumed. `ChildLaunch.Arguments` keeps the `extract` verb. When the host process is `dotnet` or `dotnet.exe`, the child is `dotnet exec <assembly> extract --child …`. The parent prints only the last stderr line matching `^E_[A-Z_]+$`. Exit 1816 is `E_LIMIT_CPU`. `0xC0000044`, `0xC0000017`, and `0xC000012D` are `E_LIMIT_MEMORY`.
- Hidden `--child-test-hang`, `--child-test-allocate`, and `--child-test-flood-stderr` run only when `DOCEXTRACT_TEST_HOOKS=1`.
- Zip ratio hard-fails only when the uncompressed size is over 1 MB. Nested entries that exceed the ratio become `W_PART_FAILED`.
- `self-test --require-ocr` fails closed when Tesseract is unavailable. Windows turns that on by default. A synthetic MSG is generated with OpenMcdf and checked in at `extractor/tests/corpus/synthetic.msg`.
- Checked-in corpus is `hello.txt`, `truncated.pdf`, and `synthetic.msg`. Other fixtures are generated in tests so sample documents and large binaries stay out of git.

## Alternatives Considered

- Single-file publish: rejected. Native Skia, PDFium, and Tesseract binaries are easier to service in a folder, and trimming is incompatible with those stacks.
- ImageSharp for resize: rejected. The extractor uses SkiaSharp.
- Shipping a Tesseract CLI instead of the NuGet wrapper: deferred. The wrapper is behind `IOcrEngine`.
- Failing the process when tessdata is absent: rejected. `W_OCR_UNAVAILABLE` does not fail the extract.
- Treating every limit as exit 3: rejected. Output truncation, depth, and part count are partial results the caller can still page.

## Consequences

- Positive: `dotnet test` covers kinds, limits, path safety, and the sniffer on macOS. The Windows job limits are expressed as data (`JobLimits.Build`) so they can be asserted without a Windows host.
- Negative: OCR does not run in macOS tests unless a native Tesseract library is added later. A live Job Object kill is not part of the macOS suite. Off-Windows timeouts cannot interrupt a single native call that never returns to managed code; the Windows parent can.
