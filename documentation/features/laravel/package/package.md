# Laravel package

`dvrtech/laravel-docextract` runs the DocExtract executable from a Laravel application. It creates a private temp directory, passes an argument array to the executable, and reads `result.json` into typed values.

## User flow

1. The host application installs the package with Composer and runs `php artisan docextract:install`.
2. The install command downloads `DocExtract-win-x64-<version>.zip` and the matching `.sha256` from the public GitHub release, with no token.
3. A matching digest is extracted to `storage/docextract/bin/<version>/`. A mismatch stops before extraction.
4. The command runs `self-test` through the installed executable.
5. Application code calls `DocExtract::extract($path, $originalName, $options)` and reads Markdown, image bytes, or a UTF-16 slice of the concatenated text.
6. `cleanup()` deletes the private temp directory. The destructor calls `cleanup()` again, and a second call does nothing.
7. `php artisan docextract:self-test` repeats the executable self-test. `--require-ocr` fails when OCR is unavailable.

## Technical flow

1. `DocExtractServiceProvider` merges `config/docextract.php`, registers the `DocExtract` singleton, and, in the console, publishes the config and registers the Artisan commands.
2. `DocExtract::extract` resolves an absolute input path and an absolute binary path. A `binary_path` that ends in `.php` is started as `PHP_BINARY <script> …`. Any other path is started as the executable itself. The command is a Symfony Process argument array, not a shell string.
3. The process environment is the current environment plus `TEMP`, `TMP`, and `TMPDIR` pointed at the private directory. The process timeout is `timeout_seconds`. The same value is passed as `--timeout-seconds`.
4. A logical file name is the basename of `$originalName` or of the input path. A name that starts with `-` is prefixed with `_` so it cannot be parsed as a flag.
5. Exit 0 with `result.json` becomes an `ExtractionResult`. Relative part and image paths are resolved under the output directory. `..`, absolute paths, and `:` are `E_UNSAFE_PATH`.
6. Non-zero exits become the exception for that exit code. The exception message is only the last stderr line matching `^E_[A-Z_]+$`. A Symfony process timeout becomes `ExtractionTimeoutException` with `E_TIMEOUT` and exit 5.
7. `page($charStart, $charEnd)` encodes the concatenated Markdown as UTF-16LE and slices code units. A range that would split a surrogate pair drops the dangling unit.

## Key classes

| Class | Path | Role |
|---|---|---|
| `DocExtract` | `src/DocExtract.php` | Runs the executable and maps exit codes. |
| `ExtractionResult` | `src/ExtractionResult.php` | Parsed `result.json`, `text()`, `page()`, `cleanup()`. |
| `ExtractedPart` | `src/ExtractedPart.php` | One part. `markdown()` reads `parts/<index>.md`. |
| `ExtractedImage` | `src/ExtractedImage.php` | One image. `bytes()` reads the image file. |
| `Warning` | `src/Warning.php` | Warning code and detail. |
| `OutputFiles` | `src/OutputFiles.php` | Rejects paths that leave the output directory. |
| `TempDirectory` | `src/TempDirectory.php` | Idempotent delete of the private directory. |
| `Version` | `src/Version.php` | `VERSION` constant, kept equal to the extractor version. |
| `DocExtractServiceProvider` | `src/DocExtractServiceProvider.php` | Config, singleton, Artisan commands, publish tag `docextract-config`. |
| `Facades\DocExtract` | `src/Facades/DocExtract.php` | Facade accessor. |
| `InstallCommand` | `src/Console/InstallCommand.php` | `docextract:install`. |
| `SelfTestCommand` | `src/Console/SelfTestCommand.php` | `docextract:self-test`. |
| `DocExtractException` and the five subclasses | `src/Exceptions/` | Typed failures. The message is the stderr code only. |

## Integration points

- The executable contract is the extractor feature. This package does not reimplement parsing of PDF, Office, or mail files.
- Install uses `Illuminate\Support\Facades\Http` against `https://github.com/<repository>/releases/download/v<version>/`. No `Authorization` header is set.
- Tessdata is inside the release zip at `tessdata/eng.traineddata` next to `DocExtract.exe`. There is no separate tessdata setting.
- Artisan already owns a global `--version` flag, so the install command takes `--release=` for the version to download.

## Routes and access control

| Command | Role |
|---|---|
| `docextract:install {--release=} {--force}` | Download, verify, extract, self-test. |
| `docextract:self-test {--require-ocr}` | Print the self-test JSON. Exit 1 when it does not pass. |
| `vendor:publish --tag=docextract-config` | Copy `config/docextract.php`. |

No HTTP routes. The package does not authenticate. The host application chooses which file is passed to `extract()`.

## Database schema

None.

## SQL artifacts

None.

## Output and failure behavior

| Exit | Exception | Typical code |
|---|---|---|
| 2 | `UnsupportedFileException` | `E_UNSUPPORTED` |
| 3 | `LimitExceededException` | `E_LIMIT_*` |
| 4 | `CorruptFileException` | `E_CORRUPT` or `E_ENCRYPTED` |
| 5 | `ExtractionTimeoutException` | `E_TIMEOUT` |
| other non-zero | `ExtractionFailedException` | the stderr code, or `E_FAILED` |
| process timeout | `ExtractionTimeoutException` | `E_TIMEOUT` |

On any exception from `extract()`, the private directory is removed before the exception leaves the method. After `cleanup()`, later reads throw `ExtractionFailedException` with `E_FAILED`.
