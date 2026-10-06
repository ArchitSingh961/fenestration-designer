# Builds the customer installer: artifacts\MARK-Setup-<version>.exe
#
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1
#
# 1. Publishes MARK for 64-bit Windows with .NET included (customers need nothing else installed).
# 2. Packs it into src\Mark.Setup\payload.zip, which MARK Setup carries inside itself.
# 3. Publishes MARK Setup as one self-contained file.
# The version comes from src\Directory.Build.props (raise it for each release, then publish it in MARK Owner › Updates).

#   powershell -ExecutionPolicy Bypass -File installer\build.ps1 -ServerUrl https://licence.example.com
# -ServerUrl: the licence server address your customers' MARK signs in to (written to licence-server.txt next to
# MARK.exe). Without it, MARK asks for the address under Connection on the sign-in page.

param([string]$Configuration = "Release", [string]$ServerUrl = "")
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
# The first dotnet with an SDK: C:\dotnet (this workspace), then the one on PATH.
$dotnet = @("C:\dotnet\dotnet.exe", (Get-Command dotnet -ErrorAction SilentlyContinue).Source) |
    Where-Object { $_ -and (Test-Path $_) -and (& $_ --list-sdks 2>$null) } | Select-Object -First 1
if (-not $dotnet) { throw "The .NET 8 SDK (dotnet) was not found." }

$version = ([xml](Get-Content "$root\src\Directory.Build.props")).Project.PropertyGroup.Version
$work = Join-Path $root "artifacts"
$program = Join-Path $work "program"
$setupOut = Join-Path $work "setup"
$payload = Join-Path $root "src\Mark.Setup\payload.zip"

Write-Host "MARK $version"
Remove-Item $program, $setupOut -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Publishing MARK (self-contained, win-x64)..."
& $dotnet publish "$root\src\Mark.App\Mark.App.csproj" -c $Configuration -r win-x64 --self-contained true -o $program -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "Publishing MARK failed." }

if ($ServerUrl) {
    if ($ServerUrl -notmatch '^https?://') { throw "-ServerUrl must start with http:// or https://" }
    Set-Content -Path (Join-Path $program "licence-server.txt") -Value $ServerUrl.TrimEnd('/') -Encoding ascii
    Write-Host "Licence server: $ServerUrl"
} else {
    Write-Warning "No -ServerUrl: customers will have to enter the licence server address under Connection."
}

Write-Host "Packing it into MARK Setup..."
Remove-Item $payload -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($program, $payload, [System.IO.Compression.CompressionLevel]::Optimal, $false)

try {
    Write-Host "Publishing MARK Setup (one file)..."
    & $dotnet publish "$root\src\Mark.Setup\Mark.Setup.csproj" -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -o $setupOut -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "Publishing MARK Setup failed." }
}
finally {
    # The payload is only for this build: a normal build of MARK Setup has none.
    Remove-Item $payload -Force -ErrorAction SilentlyContinue
}

$exe = Join-Path $work "MARK-Setup-$version.exe"
Copy-Item (Join-Path $setupOut "MARK Setup.exe") $exe -Force
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Done: $exe ($size MB)"
