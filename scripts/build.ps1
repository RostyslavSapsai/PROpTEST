param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    dotnet restore PropTest.Engineering.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet build PropTest.Engineering.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $env:PROPTEST_ARTIFACTS = Join-Path $projectRoot 'artifacts\verification'
    dotnet test PropTest.Engineering.sln -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if ($Publish) {
        dotnet publish src/PropTest.Desktop/PropTest.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o artifacts/windows-v2-desktop-045
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        Write-Host 'Ready: artifacts/windows-v2-desktop-045/PropTest.Desktop.exe'
    }
} finally { Pop-Location }













