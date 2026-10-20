param()
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$root = Join-Path $repositoryRoot '.tools/prometheus-3.15.0'
$archive = Join-Path $repositoryRoot '.tools/prometheus-3.15.0.windows-amd64.zip'
$expected = '5d333b385557d9adc2ff015d13da9809baccc52fb800a82d1e5c94b79258f87e'
# Pinned to the official v3.15.0 GitHub release asset digest, verified19October2026. Never execute an unverified archive.
if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) {
    Invoke-WebRequest -Uri 'https://github.com/prometheus/prometheus/releases/download/v3.15.0/prometheus-3.15.0.windows-amd64.zip' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Official Prometheus archive checksum mismatch.' }
New-Item -ItemType Directory -Path $root -Force | Out-Null
$tool = Join-Path $root 'promtool.exe'
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $entry = $zip.GetEntry('prometheus-3.15.0.windows-amd64/promtool.exe')
    if ($null -eq $entry) { throw 'The pinned archive does not contain promtool.' }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $tool, $true)
} finally { $zip.Dispose() }
& $tool --version
if ($LASTEXITCODE -ne 0) { throw 'Verified promtool cannot start.' }
