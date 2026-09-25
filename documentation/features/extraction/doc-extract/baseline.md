# Document extraction baseline

Behavior invariants for DocExtract 1.0.0. Update this file only when the intended behavior changes.

## Pipeline invariants

- `extract` writes `result.json` only when the process exits 0.
- stderr on every command is either empty or a single error code. It never contains a file name or document text.
- On non-Windows, `extract` without `--child` prints `W_SANDBOX_UNAVAILABLE` on stdout and then extracts in-process.
- On Windows, `extract` without `--child` does not parse the input. The child command keeps the `extract` verb. A `dotnet` host launches `dotnet exec <assembly> extract --child …`. The child is created with `CREATE_SUSPENDED`, assigned to the job, then resumed.
- The parent reads the child's stderr from a pipe and prints only the last line matching `^E_[A-Z_]+$`. Exit 1816 is `E_LIMIT_CPU`. Job memory termination and `OutOfMemoryException` are `E_LIMIT_MEMORY`. Other unrecognized codes are `E_FAILED`.
- The job limit flags are `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_PROCESS_MEMORY | JOB_OBJECT_LIMIT_JOB_TIME | JOB_OBJECT_LIMIT_ACTIVE_PROCESS`, with `ActiveProcessLimit` 1.
- `--memory-mb` is process memory in mebibytes. `--cpu-seconds` is job user CPU time, stored as 100-nanosecond ticks (`seconds * 10_000_000`). `--timeout-seconds` is the parent's wall clock.
- Root depth is 0. A child is kept when its depth is less than or equal to `--max-depth` (default 3). The next level records `W_DEPTH_LIMIT` and is not opened.
- Parts are emitted depth-first. `charStart` of part N equals `charEnd` of part N-1. Offsets count UTF-16 code units in the concatenated Markdown.
- Output truncation never ends a string on a high surrogate.
- Every filesystem write under `--output-dir` goes through `SafePath.Combine`. Absolute paths, empty segments, `.`, `..`, `:`, and reserved device names are rejected.
- Zip entry ratio is `uncompressed / compressed > 100`. That is a hard failure only when the uncompressed size is over 1,048,576 bytes. Below that floor the entry is read. At depth greater than 0 a ratio failure is `W_PART_FAILED` on the archive part. The read is also capped at `min(uncompressed, compressed × ratio)`. Listing stops after `MaxParts` entries. Encrypted zip entries, marked by flag 0 of the general-purpose flags, are `W_ENCRYPTED` and are not inflated.
- Image pixel count is taken from the file header (PNG IHDR and the other decoders in `ImageHeader`, then `SKCodec` info) before `SKBitmap.Decode`. Over 40,000,000 pixels raises `E_LIMIT_PIXELS` for a depth-0 image and `W_IMAGE_SKIPPED` for a nested image. OCR uses the full-resolution bytes; the file written under `images/` is the downscaled copy.
- Returned images have a long edge of at most 1568 px and a file size of at most 3,932,160 bytes.
- OCR runs only when `--ocr auto` (the default), the engine is available, and an OCR-page slot remains. Missing tessdata or a missing native library records `W_OCR_UNAVAILABLE` and does not fail the extract.
- Scanned-page rasters are produced only when OCR will run. DPI is at most 300 and is lowered so the page stays inside the pixel cap.
- PDF tables use lattice mode when ruling lines produce a table of at least 2×2. Stream mode is used only when lattice finds nothing, and only for a table that is at least 3×3 with at most 50% empty cells. Stream mode records `W_TABLE_LOW_CONFIDENCE`.
- `PdfDocument.IsEncrypted` alone does not reject a PDF. `PdfDocumentEncryptedException` from `Open` is exit 4 `E_ENCRYPTED`. An encrypted file that opens with no text layer records `W_ENCRYPTED`.
- A compound file that contains `EncryptedPackage` is exit 4 `E_ENCRYPTED` at the root and `W_ENCRYPTED` when nested.
- DOCX text includes `SdtBlock` content, headers, footers, and footnotes.
- UTF-16 LE and BE without a BOM are detected from zero bytes on odd or even positions before the UTF-8 fallback.
- `self-test --require-ocr` fails when OCR is unavailable. On Windows, `self-test` requires OCR even without the flag.
- XLSX emits cached cell values. A formula records `W_FORMULAS_CACHED_VALUES` once. Hidden and very-hidden sheets are included, marked `*(hidden)*`, and record `W_HIDDEN_CONTENT`.
- Hidden PPTX slides (`show` = false) are included and marked.
- `OpenSettings.MaxCharactersInPart` is 10,000,000 for DOCX, XLSX, and PPTX.
- XLSX reads at most 10,000 rows and 256 columns per sheet.
- HTML script and style blocks are dropped. Remote HTML and MIME resources are not fetched.
- `status` is `partial` when `truncated` is true or any warning is `W_TRUNCATED`, `W_DEPTH_LIMIT`, `W_CHILD_LIMIT`, `W_UNSUPPORTED_CHILD`, `W_ENCRYPTED`, `W_PART_FAILED`, `W_IMAGE_SKIPPED`, `W_OCR_UNAVAILABLE`, or `W_NO_TEXT_LAYER`. Otherwise `status` is `ok`.
- `self-test` exits 0 when text, png, pdf, docx, xlsx, pptx, eml, msg, and zip fixtures extract. Off Windows it still exits 0 when OCR is unavailable unless `--require-ocr` is set. On Windows OCR is required.
- `version` is `1.0.0`. `schemaVersion` is 1.

## Flag rules

| Condition | Code | Severity | Blocks the run? |
|---|---|---|---|
| Root type unsupported | `E_UNSUPPORTED` | exit 2 | yes |
| Root corrupt | `E_CORRUPT` | exit 4 | yes |
| Root encrypted | `E_ENCRYPTED` | exit 4 | yes |
| Root over 25 MB | `E_LIMIT_INPUT_SIZE` | exit 3 | yes |
| Zip entry ratio over 100:1 and uncompressed size over 1 MB, at depth 0 | `E_LIMIT_ZIP_RATIO` | exit 3 | yes |
| Nested zip entry over the ratio | `W_PART_FAILED` | partial | no |
| Decompressed bytes would pass 200 MB | `E_LIMIT_DECOMPRESSED` | exit 3 | yes |
| Root image header over 40 MP | `E_LIMIT_PIXELS` | exit 3 | yes |
| Nested image header over 40 MP | `W_IMAGE_SKIPPED` | partial | no |
| Windows job CPU limit (exit 1816) | `E_LIMIT_CPU` | exit 3 | yes |
| Windows job memory limit or `OutOfMemoryException` | `E_LIMIT_MEMORY` | exit 3 | yes |
| Wall clock elapsed | `E_TIMEOUT` | exit 5 | yes |
| Output, page, OCR-page, or sheet cap | `W_TRUNCATED` | partial | no |
| Depth | `W_DEPTH_LIMIT` | partial | no |
| Part cap | `W_CHILD_LIMIT` | partial | no |
| Nested unsupported | `W_UNSUPPORTED_CHILD` | partial | no |
| Nested encrypted | `W_ENCRYPTED` | partial | no |
| Nested read failure or unsafe entry name | `W_PART_FAILED` | partial | no |
| Image cap or encode failure | `W_IMAGE_SKIPPED` | partial | no |
| OCR needed but unavailable | `W_OCR_UNAVAILABLE` | partial | no |
| PDF page without a text layer | `W_NO_TEXT_LAYER` | partial | no |
| OCR produced text | `W_OCR_USED` | info | no |
| Table uncertain | `W_TABLE_LOW_CONFIDENCE` | info | no |
| Image resized or recompressed | `W_IMAGE_DOWNSCALED` | info | no |
| Formula cells | `W_FORMULAS_CACHED_VALUES` | info | no |
| Hidden sheet or slide | `W_HIDDEN_CONTENT` | info | no |
| Non-Windows extract | `W_SANDBOX_UNAVAILABLE` on stdout | info | no |

## Side effects in order

1. Parent either arms the job and starts the child, or prints `W_SANDBOX_UNAVAILABLE`.
2. The extractor reads the input only after the 25 MB check.
3. Parts and images accumulate in memory.
4. `ResultWriter` creates `parts/` and `images/` under `--output-dir` and writes `result.json` last.
5. On a hard failure the writer is not called.

No webhook, database, or network side effect exists.

## Configuration defaults

| Setting | Default | Source |
|---|---|---|
| `MaxInputBytes` | 26,214,400 | `LimitBudget` |
| `MaxDecompressedBytes` | 209,715,200 | `LimitBudget` |
| `MaxZipRatio` | 100 | `LimitBudget` |
| `MaxDepth` | 3 | CLI `--max-depth` |
| `MaxParts` | 200 | `LimitBudget` |
| `MaxPdfPages` | 300 | `LimitBudget` |
| `MaxOcrPages` | 50 | `LimitBudget` |
| `MaxRasterPixels` | 40,000,000 | `LimitBudget` |
| `MaxOutputChars` | 2,000,000 | CLI `--max-output-chars` |
| `MaxImages` | 50 | `LimitBudget` |
| `MaxImageLongEdge` | 1568 | `LimitBudget` |
| `MaxImageBytes` | 3,932,160 | `LimitBudget` |
| `--ocr` | `auto` | CLI |
| `--tables` | `auto` | CLI |
| `--memory-mb` | 1024 | CLI, Windows job only |
| `--cpu-seconds` | 90 | CLI, Windows job only |
| `--timeout-seconds` | 100 | CLI |
| `MaxCharactersInPart` | 10,000,000 | `OfficeLimits` |
| `MaxRowsPerSheet` | 10,000 | `OfficeLimits` |
| `MaxColumnsPerSheet` | 256 | `OfficeLimits` |
| Extractor version | 1.0.0 | `EngineCatalog.Version`, same string as `extractor/Directory.Build.props` `<Version>` |

## Access control

- The process does not check a user identity.
- It will not write outside `--output-dir`.
- It will not follow a zip entry whose name contains `..`, a drive or ADS colon, or a reserved device name.
