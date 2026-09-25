#!/usr/bin/env bash
# Downloads the pinned tessdata_fast English model next to a publish folder.
# The traineddata file is not committed.
set -euo pipefail

dest="${1:-tessdata}"
commit="923915d4ced2a7235221788285785a29c4a42d4a"
url="https://github.com/tesseract-ocr/tessdata_fast/raw/${commit}/eng.traineddata"
expected="7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2"

mkdir -p "$dest"
target="$dest/eng.traineddata"
tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

curl -fsSL "$url" -o "$tmp"
actual="$(shasum -a 256 "$tmp" | awk '{print $1}')"
if [[ "$actual" != "$expected" ]]; then
  echo "sha256 mismatch: $actual" >&2
  exit 1
fi

mv "$tmp" "$target"
trap - EXIT
echo "$target"
