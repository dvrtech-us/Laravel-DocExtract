#!/usr/bin/env bash
# Fail when the extractor version and the PHP package version disagree.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
props="$(grep -o '<Version>[^<]*</Version>' "$root/extractor/Directory.Build.props" | head -1 | sed 's/<Version>//;s/<\/Version>//')"
phpv="$(grep -o "const VERSION = '[^']*'" "$root/src/Version.php" | head -1 | sed "s/const VERSION = '//;s/'//")"
catalog="$(grep -o 'const string Version = "[^"]*"' "$root/extractor/src/DocExtract/Contract/EngineCatalog.cs" | head -1 | sed 's/const string Version = "//;s/"//')"

if [[ -z "$props" || "$props" != "$phpv" || "$props" != "$catalog" ]]; then
  echo "Version mismatch: Directory.Build.props=$props php=$phpv catalog=$catalog" >&2
  exit 1
fi

echo "$props"
