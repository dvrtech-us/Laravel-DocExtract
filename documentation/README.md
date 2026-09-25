# Laravel DocExtract documentation

## Feature map

| Module | Feature | What it does | Doc |
|---|---|---|---|
| extraction | doc-extract | Reads one document into Markdown parts, normalized images, and `result.json`. | [features/extraction/doc-extract/doc-extract.md](features/extraction/doc-extract/doc-extract.md) |
| laravel | package | Runs that executable from a Laravel application and installs a verified Windows build. | [features/laravel/package/package.md](features/laravel/package/package.md) |

There is no database and no HTTP surface. The extractor contract is the console exit code plus the files under `--output-dir`. The package adds two Artisan commands.
