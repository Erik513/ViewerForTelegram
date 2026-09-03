<#
    Copies the current ErikwnkWFUI and ErikwnkCore DLLs from the neighbouring
    repos into here (lib\ErikwnkWFUI\).

    Only run this when you changed something in ErikwnkWFUI/ErikwnkCore and
    rebuilt the library. Then rebuild ViewerForTelegram.

    Usage:  powershell -ExecutionPolicy Bypass -File lib\update-erikwnk-libs.ps1
            [-Configuration Release]
#>
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot   = Split-Path $PSScriptRoot -Parent
$sourceRepo = Join-Path (Split-Path $repoRoot -Parent) "ErikwnkWFUI\ErikwnkWFUI\bin\$Configuration\net8.0-windows"
$target     = Join-Path $PSScriptRoot "ErikwnkWFUI"

if (-not (Test-Path $sourceRepo)) {
    throw "Not found: $sourceRepo`nRun 'dotnet build' in the ErikwnkWFUI repo first (configuration: $Configuration)."
}

$files = @(
    "ErikwnkWFUI.dll", "ErikwnkWFUI.xml", "ErikwnkWFUI.pdb",
    "ErikwnkCore.dll", "ErikwnkCore.pdb"
)

foreach ($file in $files) {
    $src = Join-Path $sourceRepo $file
    if (Test-Path $src) {
        Copy-Item $src $target -Force
        Write-Host "copied:  $file"
    } else {
        Write-Warning "missing: $file"
    }
}

Write-Host "`nDone. Now rebuild ViewerForTelegram." -ForegroundColor Green
