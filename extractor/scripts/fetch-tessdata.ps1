# Downloads the pinned tessdata_fast English model next to a publish folder.
# The traineddata file is not committed.
param(
    [string]$Dest = "tessdata"
)

$ErrorActionPreference = "Stop"
$commit = "923915d4ced2a7235221788285785a29c4a42d4a"
$url = "https://github.com/tesseract-ocr/tessdata_fast/raw/$commit/eng.traineddata"
$expected = "7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2"

New-Item -ItemType Directory -Force -Path $Dest | Out-Null
$target = Join-Path $Dest "eng.traineddata"
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("eng.traineddata." + [guid]::NewGuid().ToString("n"))
try {
    Invoke-WebRequest -Uri $url -OutFile $tmp
    $actual = (Get-FileHash -Algorithm SHA256 -Path $tmp).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "sha256 mismatch: $actual"
    }
    Move-Item -Force $tmp $target
}
finally {
    if (Test-Path $tmp) {
        Remove-Item -Force $tmp
    }
}

Write-Output $target
