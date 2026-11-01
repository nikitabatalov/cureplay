$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Publishing CurePlay 2.1 for Windows x64...' -ForegroundColor Green
dotnet restore .\CurePlay\CurePlay.csproj -r win-x64
if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed' }
dotnet publish .\CurePlay\CurePlay.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false --no-restore -o .\publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Write-Host 'Finished: publish\CurePlay.exe (keep all files in publish folder)' -ForegroundColor Green
