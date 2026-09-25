# Laravel package baseline

Behavior invariants for `dvrtech/laravel-docextract` 1.0.0. Update this file only when the intended behavior changes.

## Pipeline invariants

- `extract()` starts the executable with a Symfony Process argument array. The first element is an absolute path. A binary path ending in `.php` is prefixed with `PHP_BINARY`.
- The logical name passed to `--file-name` is a basename. A name that starts with `-` is prefixed with `_`.
- `--input` and `--output-dir` are absolute paths inside or outside the private temp directory as appropriate: the input is the caller's file, and the output directory is `<temp>/out`.
- The process timeout and `--timeout-seconds` are the same positive integer.
- `TEMP`, `TMP`, and `TMPDIR` in the child environment are the private temp directory.
- Exit 0 without a readable `result.json` is `ExtractionFailedException` `E_FAILED`.
- `charStart` and `charEnd` are UTF-16 code unit offsets. `page()` uses those units. A requested range that splits a surrogate pair does not return an invalid UTF-8 string.
- `cleanup()` and the destructor delete the private directory. A second `cleanup()` does not throw.
- Exception messages from the package are the stderr code only. They do not include the input path or document text.
- `docextract:install` sends no `Authorization` header. It compares the archive sha256 with `hash_equals` after lowercasing the published digest. A mismatch does not create `DocExtract.exe`.
- Install extracts only after the digest matches. Zip entry names that contain `..`, a drive prefix, or `:` are rejected.
- The default install directory is `storage_path('docextract/bin/<version>/')`. `--force` replaces that directory. Without `--force`, an existing directory fails the command and is left in place.
- After a successful install, runtime `docextract.binary_path` points at the extracted `DocExtract.exe`, and `selfTest(false)` must report `passed: true`.
- `Version::VERSION`, `EngineCatalog.Version`, and `extractor/Directory.Build.props` `<Version>` are the same string.

## Flag rules

| Condition | Code | Severity | Blocks the run? |
|---|---|---|---|
| Executable missing | `E_FAILED` | `ExtractionFailedException` | yes |
| Exit 2 | stderr code | `UnsupportedFileException` | yes |
| Exit 3 | stderr code | `LimitExceededException` | yes |
| Exit 4 | stderr code | `CorruptFileException` | yes |
| Exit 5 or process timeout | `E_TIMEOUT` | `ExtractionTimeoutException` | yes |
| Other non-zero exit | stderr code or `E_FAILED` | `ExtractionFailedException` | yes |
| Part or image path leaves the output directory | `E_UNSAFE_PATH` | `ExtractionFailedException` | yes |
| sha256 mismatch | command error text | install fails | yes, nothing extracted |
| Install directory already present | command error text | install fails unless `--force` | yes |
| `ocr` or `tables` other than `auto` or `off` | `E_USAGE` | `ExtractionFailedException` | yes, process not started |

## Side effects in order

1. Create `<temp_path>/docextract-<random>/out` with mode `0700`.
2. Start the executable.
3. On success, parse `result.json` and return an `ExtractionResult` that owns the directory.
4. On failure, delete the directory and throw.
5. `cleanup()` deletes it later on the success path.

Install, in order: refuse an existing directory unless `--force`, download the zip, download the `.sha256`, compare, extract, set `binary_path`, run self-test.

No database, webhook, or authenticated HTTP call exists.

## Configuration defaults

| Key | Default |
|---|---|
| `binary_path` | `storage_path('docextract/bin/'.Version::VERSION.'/DocExtract.exe')` |
| `temp_path` | `null` (system temp directory) |
| `timeout_seconds` | 100 |
| `memory_mb` | 1024 |
| `cpu_seconds` | 90 |
| `max_output_chars` | 2000000 |
| `max_depth` | 3 |
| `ocr` | `auto` |
| `tables` | `auto` |
| `release_version` | `Version::VERSION` |
| `repository` | `dvrtech-us/Laravel-DocExtract` |

## Access control

- The package does not check a user identity.
- It will not read a result path that resolves outside the output directory.
- It will not send credentials to the release host.
