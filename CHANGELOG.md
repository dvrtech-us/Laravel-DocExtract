# Changelog

## 1.0.0 - 2026-09-25

- DocExtract console reads one document into Markdown parts, normalized images, and `result.json`.
- Windows extracts run inside a Job Object. Other operating systems run in-process and print `W_SANDBOX_UNAVAILABLE`.
- Laravel package `dvrtech/laravel-docextract` with `DocExtract::extract()`, `docextract:install`, and `docextract:self-test`.
- `docextract:install` downloads `DocExtract-win-x64-<version>.zip` and its `.sha256` from the public GitHub release, checks the digest, and runs a self-test.
- Release archive name and version come from `extractor/Directory.Build.props`.
