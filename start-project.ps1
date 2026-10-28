param([switch]$NoBrowser)

$ErrorActionPreference = 'Stop'

$localDotnet = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
$webProject = Join-Path $PSScriptRoot 'app/PurchaseFlow/src/PurchaseFlow.Web'
$certificate = Join-Path $PSScriptRoot '.tools/https.pfx'
$certificatePasswordFile = Join-Path $PSScriptRoot '.tools/https-password.txt'
$projectUrl = 'https://localhost:44345'
$expectedExecutable = Join-Path $webProject 'bin/Debug/net10.0/PurchaseFlow.Web.exe'

$listenerIds = @(Get-NetTCPConnection -LocalPort 44345 -State Listen -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique)
if ($listenerIds.Count -gt 0) {
    $otherProcessIds = @()
    foreach ($listenerId in $listenerIds) {
        $listenerProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $listenerId"
        if ($listenerProcess.ExecutablePath -ne $expectedExecutable) {
            $otherProcessIds += $listenerId
        }
    }
    if ($otherProcessIds.Count -gt 0) {
        throw "Port 44345 is used by another program (PID: $($otherProcessIds -join ', '))."
    }

    Write-Host "PurchaseFlow is already running. Open $projectUrl"
    if (-not $NoBrowser) {
        Start-Process $projectUrl
    }
    return
}

if (Test-Path -LiteralPath $localDotnet) {
    $dotnetCommand = $localDotnet
} else {
    $dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
}

if ((Test-Path -LiteralPath $certificate) -and (Test-Path -LiteralPath $certificatePasswordFile)) {
    $env:ASPNETCORE_Kestrel__Certificates__Default__Path = $certificate
    $env:ASPNETCORE_Kestrel__Certificates__Default__Password =
        (Get-Content -LiteralPath $certificatePasswordFile -Raw).Trim()
}

Write-Host "Open $projectUrl after the server starts."
Write-Host 'Keep this window open while using PurchaseFlow. Press Ctrl+C to stop.'
Push-Location $webProject
try {
    & $dotnetCommand run --no-restore --launch-profile PurchaseFlow.Web
    if ($LASTEXITCODE -ne 0) {
        throw "PurchaseFlow exited with code $LASTEXITCODE."
    }
} finally {
    Pop-Location
}

