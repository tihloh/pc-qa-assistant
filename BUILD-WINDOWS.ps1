# Manual build only. No automated workflows, upload, or release publishing.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot 'PCSoundCapture')
dotnet restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Write-Host "Build output: $PWD\bin\Release\net10.0-windows\win-x64\publish"
