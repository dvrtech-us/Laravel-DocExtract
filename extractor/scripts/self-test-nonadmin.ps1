# Runs DocExtract self-test --require-ocr as a local non-admin account.
param(
    [Parameter(Mandatory = $true)]
    [string]$Exe
)

$ErrorActionPreference = "Stop"
$name = "docextractci"
$password = "DocExtract!CI-9"
$exeFull = (Resolve-Path $Exe).Path
$publishDir = Split-Path $exeFull -Parent
$work = Join-Path $env:TEMP "docextract-ci-work"
New-Item -ItemType Directory -Force -Path $work | Out-Null

$existing = Get-LocalUser -Name $name -ErrorAction SilentlyContinue
if ($existing) {
    Remove-LocalUser -Name $name
}

$secure = ConvertTo-SecureString $password -AsPlainText -Force
New-LocalUser -Name $name -Password $secure -PasswordNeverExpires -UserMayNotChangePassword | Out-Null
icacls $publishDir /grant "${name}:(OI)(CI)RX" | Out-Null
icacls $work /grant "${name}:(OI)(CI)M" | Out-Null

try {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exeFull
    $psi.Arguments = "self-test --require-ocr"
    $psi.WorkingDirectory = $publishDir
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UserName = $name
    $psi.Password = $secure
    $psi.LoadUserProfile = $false
    foreach ($var in @("SystemRoot", "WINDIR", "PATH", "PATHEXT")) {
        $value = [Environment]::GetEnvironmentVariable($var)
        if ($value) { $psi.Environment[$var] = $value }
    }
    $psi.Environment["TEMP"] = $work
    $psi.Environment["TMP"] = $work

    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($stdout) { Write-Output $stdout }
    if ($process.ExitCode -ne 0) {
        Write-Error "self-test exited $($process.ExitCode): $stderr"
        exit $process.ExitCode
    }
}
finally {
    Remove-LocalUser -Name $name -ErrorAction SilentlyContinue
}
