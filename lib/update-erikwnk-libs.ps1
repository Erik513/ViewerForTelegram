<#
    Kopiert die aktuellen ErikwnkWFUI- und ErikwnkCore-DLLs aus den
    Nachbar-Repos hierher (lib\ErikwnkWFUI\).

    Nur ausführen, wenn du an ErikwnkWFUI/ErikwnkCore etwas geändert und
    die Lib neu gebaut hast. Danach ViewerForTelegram neu bauen.

    Aufruf:  powershell -ExecutionPolicy Bypass -File lib\update-erikwnk-libs.ps1
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
    throw "Nicht gefunden: $sourceRepo`nErst 'dotnet build' im ErikwnkWFUI-Repo ausführen (Konfiguration: $Configuration)."
}

$files = @(
    "ErikwnkWFUI.dll", "ErikwnkWFUI.xml", "ErikwnkWFUI.pdb",
    "ErikwnkCore.dll", "ErikwnkCore.pdb"
)

foreach ($file in $files) {
    $src = Join-Path $sourceRepo $file
    if (Test-Path $src) {
        Copy-Item $src $target -Force
        Write-Host "kopiert:  $file"
    } else {
        Write-Warning "fehlt:    $file"
    }
}

Write-Host "`nFertig. Jetzt ViewerForTelegram neu bauen." -ForegroundColor Green
