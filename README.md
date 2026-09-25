# Laravel DocExtract

Extract text from documents in PHP / Laravel. The package runs a Windows
executable out of process and returns Markdown parts, normalized images, and
a `result.json` manifest the host application can store and page.

Supported inputs include PDF, DOCX, XLSX, PPTX, EML, MSG, ZIP, plain text,
HTML, and common raster images. Type detection uses magic bytes. The extension
is only a tiebreaker.

## Install

From Packagist, once the package is published:

```bash
composer require dvrtech/laravel-docextract
```

From the public Git repository before Packagist has it:

```bash
composer config repositories.laravel-docextract vcs https://github.com/dvrtech-us/Laravel-DocExtract
composer require dvrtech/laravel-docextract:^1.0
```

Laravel auto-discovery registers the service provider and the `DocExtract` facade.

Publish the config (optional):

```bash
php artisan vendor:publish --tag=docextract-config
```

Download the Windows build, verify its sha256, and self-test it:

```bash
php artisan docextract:install
php artisan docextract:install --release=1.0.0
php artisan docextract:install --force
```

`docextract:install` fetches `DocExtract-win-x64-<version>.zip` and
`DocExtract-win-x64-<version>.zip.sha256` from the public GitHub release for
`dvrtech-us/Laravel-DocExtract`. It sends no token. Artisan already uses
`--version` to print its own version, so this command takes `--release=`.

The archive is extracted to `storage/docextract/bin/<version>/`. Traineddata
for OCR is inside that zip at `tessdata/eng.traineddata`, next to
`DocExtract.exe`.

## Platform support

The published executable is Windows x64 and needs the
[Visual C++ 2019 x64 runtime](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)
(`vcruntime140.dll`) so `tesseract50.dll` can load. On another operating
system, build DocExtract from `extractor/` and set `DOCEXTRACT_BINARY_PATH`
to that executable.

## Usage

```php
use Dvrtech\LaravelDocExtract\Facades\DocExtract;

$result = DocExtract::extract(storage_path('app/report.pdf'), 'report.pdf');

try {
    $markdown = $result->parts[0]->markdown();
    $page = $result->page($result->parts[0]->charStart, $result->parts[0]->charEnd);
    $png = $result->images[0]->bytes();
    $everything = $result->text();
} finally {
    $result->cleanup();
}
```

`page($charStart, $charEnd)` slices the concatenated Markdown by UTF-16 code
units. `$charEnd` is exclusive and matches `result.json`. `cleanup()` removes
the private temp directory. Calling it twice is safe, and the destructor
calls it if you do not.

Per-call options override the config:

```php
$result = DocExtract::extract($path, null, [
    'ocr' => 'off',
    'tables' => 'off',
    'max_depth' => 1,
    'timeout_seconds' => 30,
]);
```

A file name that starts with `-` is prefixed with `_` before it is passed to
the executable.

```php
$version = DocExtract::version();
$report = DocExtract::selfTest(requireOcr: false);
```

## Configuration

| Key | Env | Default |
|---|---|---|
| `binary_path` | `DOCEXTRACT_BINARY_PATH` | `storage/docextract/bin/<version>/DocExtract.exe` |
| `temp_path` | `DOCEXTRACT_TEMP_PATH` | system temp directory |
| `timeout_seconds` | `DOCEXTRACT_TIMEOUT_SECONDS` | `100` |
| `memory_mb` | `DOCEXTRACT_MEMORY_MB` | `1024` |
| `cpu_seconds` | `DOCEXTRACT_CPU_SECONDS` | `90` |
| `max_output_chars` | `DOCEXTRACT_MAX_OUTPUT_CHARS` | `2000000` |
| `max_depth` | `DOCEXTRACT_MAX_DEPTH` | `3` |
| `ocr` | `DOCEXTRACT_OCR` | `auto` |
| `tables` | `DOCEXTRACT_TABLES` | `auto` |
| `release_version` | `DOCEXTRACT_RELEASE_VERSION` | package version |
| `repository` | `DOCEXTRACT_REPOSITORY` | `dvrtech-us/Laravel-DocExtract` |

## Errors

Failures throw a subclass of `Dvrtech\LaravelDocExtract\Exceptions\DocExtractException`.
The message is only the stderr code.

| Exit | Exception |
|---|---|
| 2 | `UnsupportedFileException` |
| 3 | `LimitExceededException` |
| 4 | `CorruptFileException` (`E_CORRUPT` or `E_ENCRYPTED`) |
| 5 | `ExtractionTimeoutException` |
| any other non-zero, or a missing executable | `ExtractionFailedException` |

A process that exceeds `timeout_seconds` also throws `ExtractionTimeoutException`
with `E_TIMEOUT`.

```php
use Dvrtech\LaravelDocExtract\Exceptions\LimitExceededException;

try {
    $result = DocExtract::extract($path);
} catch (LimitExceededException $e) {
    $e->errorCode(); // E_LIMIT_INPUT_SIZE, E_LIMIT_MEMORY, ...
    $e->exitCode;    // 3
}
```

## CLI contract

The executable accepts three commands. On Windows, `extract` re-launches
itself as a child inside a Job Object. On macOS and Linux the same command
runs in-process so tests can execute during development, and stdout prints
`W_SANDBOX_UNAVAILABLE`. stderr is reserved for error codes.

```text
DocExtract extract --input <file> --output-dir <dir> [--file-name <original name>]
                   [--max-output-chars 2000000] [--max-depth 3] [--ocr auto|off] [--tables auto|off]
                   [--memory-mb 1024] [--cpu-seconds 90] [--timeout-seconds 100]
DocExtract self-test [--require-ocr] [--allow-missing-ocr]
DocExtract version
```

`--child` is used by the Windows parent when it relaunches itself. Passing it
skips Job Object setup and runs the extraction in the current process.

`self-test` builds text, PNG, PDF, DOCX, XLSX, PPTX, EML, MSG, and ZIP
fixtures in memory, runs each through the pipeline, and prints a JSON report.
Exit `0` when every kind extracts. `--require-ocr` fails the run when
Tesseract cannot load. On Windows that requirement is on by default; `--allow-missing-ocr` turns it off (for unit tests only).

`version` prints JSON:

```json
{ "version": "1.0.0", "engines": { "pdf": "PdfPig 0.1.16" } }
```

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Success. `result.json` was written. Status may be `ok` or `partial`. |
| 1 | Any other failure (`E_USAGE`, `E_FAILED`, `E_UNSAFE_PATH`, `E_SANDBOX`, `E_SELF_TEST`). |
| 2 | Unsupported type (`E_UNSUPPORTED`). |
| 3 | A hard limit was exceeded (`E_LIMIT_*`). |
| 4 | Input is corrupt or encrypted (`E_CORRUPT`, `E_ENCRYPTED`). |
| 5 | Wall-clock timeout (`E_TIMEOUT`). |

stderr prints one error code and nothing else. It does not include file names or document text.

| stderr | When |
|---|---|
| `E_USAGE` | Unknown command or missing arguments. |
| `E_UNSUPPORTED` | The root file is not a supported kind. |
| `E_LIMIT_INPUT_SIZE` | Root file is over 25 MB. |
| `E_LIMIT_DECOMPRESSED` | Inflated bytes would pass 200 MB. |
| `E_LIMIT_ZIP_RATIO` | A zip entry's uncompressed/compressed ratio is over 100:1. |
| `E_LIMIT_PIXELS` | A root image header declares more than 40 megapixels. A nested image is skipped with `W_IMAGE_SKIPPED`. |
| `E_LIMIT_CPU` | The Windows job CPU limit stopped the child (process exit 1816). |
| `E_LIMIT_MEMORY` | The Windows job memory limit stopped the child, or the child caught `OutOfMemoryException`. |
| `E_ENCRYPTED` | The root file is encrypted. |
| `E_CORRUPT` | The root file is truncated or malformed. |
| `E_TIMEOUT` | `--timeout-seconds` elapsed. |
| `E_FAILED` | Unexpected failure. |
| `E_UNSAFE_PATH` | A write would leave `--output-dir`. |
| `E_SANDBOX` | The Windows Job Object could not be armed. |
| `E_SELF_TEST` | A self-test fixture did not extract. |

On macOS and Linux, stdout for `extract` also prints `W_SANDBOX_UNAVAILABLE` before the in-process run. That line is not an error code and is not written to stderr.

### result.json

UTF-8, camelCase, `schemaVersion` 1. Each part's Markdown starts with `## <path>`. Container parts (zip, eml, msg) list their children; the children follow in depth-first order as later parts. `charStart` and `charEnd` are UTF-16 code unit offsets into the concatenation of `parts/*.md`. Truncation never splits a surrogate pair.

```json
{
  "schemaVersion": 1,
  "extractorVersion": "1.0.0",
  "status": "ok",
  "totalChars": 123456,
  "truncated": false,
  "parts": [
    {
      "index": 0,
      "path": "report.pdf",
      "depth": 0,
      "kind": "pdf",
      "mime": "application/pdf",
      "sizeBytes": 2048000,
      "pages": 12,
      "engine": "pdfpig+tabula+tesseract",
      "durationMs": 840,
      "charStart": 0,
      "charEnd": 50000,
      "markdownFile": "parts/0.md",
      "truncated": false,
      "warnings": [{ "code": "W_OCR_USED", "detail": "img-0" }],
      "imageIds": ["img-0"]
    }
  ],
  "images": [
    {
      "id": "img-0",
      "partIndex": 0,
      "file": "images/img-0.png",
      "mime": "image/png",
      "width": 1200,
      "height": 800,
      "sizeBytes": 350000,
      "ocrChars": 812
    }
  ]
}
```

`status` is `partial` when output was truncated or any part carries `W_TRUNCATED`, `W_DEPTH_LIMIT`, `W_CHILD_LIMIT`, `W_UNSUPPORTED_CHILD`, `W_ENCRYPTED`, `W_PART_FAILED`, `W_IMAGE_SKIPPED`, `W_OCR_UNAVAILABLE`, or `W_NO_TEXT_LAYER`. Other warnings leave `status` at `ok`.

`pages` is PDF page count, PPTX slide count, or XLSX sheet count, and `null` otherwise.

Images (root rasters, embedded email images, and scanned PDF pages that were OCR'd) are re-encoded PNG or JPEG with a long edge of at most 1568 px and at most 3.75 MB (3,932,160 bytes). OCR text is appended to the owning part under `### OCR text (img-0)`.

### Warning codes

| Code | Meaning |
|---|---|
| `W_OCR_USED` | OCR contributed text. |
| `W_OCR_UNAVAILABLE` | OCR was needed and tessdata or the native Tesseract library was missing. The extract still succeeds. |
| `W_NO_TEXT_LAYER` | A PDF page had no usable text layer. |
| `W_TABLE_LOW_CONFIDENCE` | A PDF table came from stream mode, looked sparse, or could not be read. |
| `W_TRUNCATED` | Output characters, PDF pages, OCR pages, or a sheet row/column cap cut content. |
| `W_DEPTH_LIMIT` | A container was not opened because depth would pass `--max-depth` (default 3). |
| `W_CHILD_LIMIT` | Further children were skipped at 200 parts. |
| `W_UNSUPPORTED_CHILD` | A nested file is not a supported kind. |
| `W_ENCRYPTED` | A nested file is encrypted. |
| `W_PART_FAILED` | A nested file could not be read, or its name was unsafe. |
| `W_IMAGE_DOWNSCALED` | An image was resized or recompressed to fit the return limits. |
| `W_IMAGE_SKIPPED` | An image was dropped (50-image cap, or it could not be encoded under 3.75 MB). |
| `W_FORMULAS_CACHED_VALUES` | XLSX formulas were not recalculated. Cached cell values are emitted. |
| `W_HIDDEN_CONTENT` | A hidden sheet or slide was included and marked. |

### Limits

Enforced before the allocation they guard, and cumulative across the tree.

| Limit | Default |
|---|---|
| Input file | 25 MB |
| Total decompressed bytes | 200 MB, counted while streaming |
| Zip compression ratio per entry | 100:1. Hard-fail only when uncompressed size is over 1 MB. Smaller compressible entries are read. Nested entries that exceed the ratio are `W_PART_FAILED`. The read stops at `min(uncompressed, compressed × ratio)`. |
| Depth | 3 (depths 0 through 3 are kept) |
| Total parts | 200 |
| PDF pages processed | 300 |
| OCR pages | 50 |
| Raster pixels per image | 40 MP, rejected from the header before decode. Root images exit 3. Nested images are `W_IMAGE_SKIPPED`. OCR reads the full-resolution bytes; the saved image is downscaled. |
| Output characters | 2,000,000 |
| Images returned | 50 |
| Paths | Reject absolute paths, `..`, ADS (`:`), and reserved device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`, including `name.ext`). Every write is a validated join under `--output-dir`. |
| XLSX sheet | 10,000 rows and 256 columns |
| Open XML part | `OpenSettings.MaxCharactersInPart` = 10,000,000 |
| Network | No engine fetches remote content. HTML and MIME external resources are not retrieved. |

Zip ratio, decompressed bytes, input size, and pixel dimensions fail the whole run (exit 3). Depth, part count, page caps, OCR page caps, image count, and output characters produce warnings and `partial` (exit 0) when the rest of the file can still be written.

## Security

On Windows the parent calls `CreateJobObject` and `SetInformationJobObject` with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, `JOB_OBJECT_LIMIT_PROCESS_MEMORY` (`--memory-mb`, default 1024), `JOB_OBJECT_LIMIT_JOB_TIME` (`--cpu-seconds`, default 90, in 100 ns units), and `JOB_OBJECT_LIMIT_ACTIVE_PROCESS` = 1. The child is created suspended (`CREATE_SUSPENDED`). Stdout and stderr are pipes. The parent prints only the last stderr line matching `^E_[A-Z_]+$`. Exit 1816 becomes `E_LIMIT_CPU`. A job memory kill (`0xC0000044`, `0xC0000017`, or `0xC000012D`) or a child `OutOfMemoryException` becomes `E_LIMIT_MEMORY`. The parent waits `--timeout-seconds` (default 100) and on expiry terminates the job and exits 5.

The PHP package deletes its private temp directory on failure and on `cleanup()`. It refuses to read a part or image path that leaves that directory. Install verifies the release archive with sha256 before extracting it, and it does not send credentials.

`tesseract50.dll` depends on the Visual C++ 2019 x64 runtime. Windows CI checks for `vcruntime140.dll` and runs `self-test --require-ocr` as a non-admin account.

## Building the executable

From `extractor/`, with the .NET 10 SDK (`global.json` rolls forward within the 10.0 feature band):

```bash
cd extractor
dotnet test
dotnet publish src/DocExtract/DocExtract.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=false -p:PublishTrimmed=false -o artifacts/win-x64
./scripts/fetch-tessdata.sh artifacts/win-x64/tessdata
```

```powershell
./scripts/fetch-tessdata.ps1 -Dest artifacts/win-x64/tessdata
```

The fetch scripts download `tessdata_fast` commit `923915d4ced2a7235221788285785a29c4a42d4a` `eng.traineddata` and check sha256 `7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2`. The traineddata file is not committed.

`SkiaSharp.NativeAssets.macOS` is referenced so `dotnet test` can decode images on macOS. The win-x64 publish uses the Win32 native assets. Versions are pinned in `extractor/Directory.Packages.props`.

The version string is `<Version>` in `extractor/Directory.Build.props`. It must match `EngineCatalog.Version` and `Dvrtech\LaravelDocExtract\Version::VERSION`.

## License

MIT. See `LICENSE` and `THIRD-PARTY-NOTICES.md`.
