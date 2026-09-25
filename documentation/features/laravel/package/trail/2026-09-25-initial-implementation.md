# Initial Laravel package

- Date: 2026-09-25
- Feature: package
- Related code: `src/`, `config/docextract.php`, `tests/`, `.github/workflows/ci.yml`

## Context

The host application is a Laravel app and should not shell out to DocExtract by hand. The public repository needs one Composer package that installs a verified Windows build and returns typed extraction results.

## Decisions

- Package name `dvrtech/laravel-docextract`, MIT, PHP `^8.2`, `illuminate/support` `^11 || ^12`, `illuminate/http` for the installer, and `symfony/process` `^7`. PSR-4 namespace `Dvrtech\LaravelDocExtract\`.
- The executable stays a separate publishable folder under `extractor/`. The PHP package does not vendor the zip. `docextract:install` downloads `DocExtract-win-x64-<version>.zip` and its `.sha256` from the public GitHub release.
- Artisan reserves `--version`, so the version flag is `--release=`. `--force` replaces an existing install directory.
- Tests use `tests/Fakes/fake-extractor.php`, started with `PHP_BINARY` when `binary_path` ends in `.php`. PHPUnit runs on Orchestra Testbench. Install tests use `Http::fake`.
- `Version::VERSION` is checked in CI against `extractor/Directory.Build.props` and `EngineCatalog.Version`.
- Exception messages carry the stderr code only.

## Alternatives Considered

- Bundling `DocExtract.exe` inside the Composer package: rejected. The win-x64 folder, including tessdata, is large and platform-specific. A verified download keeps the PHP package small.
- Shell strings for the process command: rejected. An argument array keeps file names with spaces and leading dashes out of a shell.
- Naming the install flag `--version`: rejected for the public CLI because `php artisan --version` is handled by Symfony before the command runs.

## Consequences

- Positive: PHP tests cover parsing, UTF-16 paging, each exit-code exception, timeouts, path checks, and the install digest without a Windows executable.
- Negative: A host that is not Windows must build or obtain its own executable and set `binary_path`. The default path is the Windows `DocExtract.exe` installed by the Artisan command.
